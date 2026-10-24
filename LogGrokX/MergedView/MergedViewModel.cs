using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using LogGrokX.Colors;
using LogGrokX.Controls;
using LogGrokX.Controls.ListControls;
using LogGrokX.Data;
using LogGrokX.Platform;
using LogGrokX.Data.Virtualization;
using LogGrokX.Filter;
using LogGrokX.Search;
using LogGrokX.Localization;

namespace LogGrokX.MergedView
{
    public sealed class MergedViewModel : ViewModelBase, IDisposable
    {
        private const int MaxTimelineSegments = 20000;
        private const string ColumnSettingsKey = "__merged_files_view__";

        private readonly ObservableCollection<DocumentViewModel> _documents;
        private readonly ApplicationSettings _applicationSettings;
        private readonly ThreadGroupingService _threadGroupingService;
        private readonly TimelinePlacementService _timelinePlacementService;
        private readonly HashSet<DocumentViewModel> _subscribed = new();
        private MergedViewSnapshot _snapshot = MergedViewSnapshot.Empty;
        private MergedDataSnapshot? _cachedData;
        private MergedSchema? _cachedSchema;
        private int _cachedDataRevision = -1;
        private int _dataRevision;
        private int _requestVersion;
        private CancellationTokenSource? _rebuildCts;
        private bool _rebuildPending;
        private bool _disposed;
        private readonly Func<MergedDocumentItem[], MergeSource[], CancellationToken, MergedDataSnapshot> _buildData;
        private readonly DecodedPartsState _decodedParts = new();
        private readonly IUiTimer _rebuildTimer;
        private readonly IUiDispatcher _dispatcher;
        private ColorRuleSet _colorSettings;
        private string? _rebuildError;

        private MergedSchema _schema = null!;
        private MergedComponentIndexer _componentIndexer = null!;
        private IFilterSettings _filterSettings = null!;
        private LogGridModel _gridModel = null!;
        private LogGridModel? _searchGridModel;

        private bool _isActive;
        private int _nextColorIndex;
        private int _currentItemIndex;
        private int _firstVisibleIndex = -1;
        private int _scrollPosition = -1;
        private int _totalLineCount;
        private IEnumerable? _selectedItems;
        private IReadOnlyList<TimelineSegment> _timelineSegments = Array.Empty<TimelineSegment>();

        public MergedViewModel(ObservableCollection<DocumentViewModel> documents,
            SearchAutocompleteCache searchAutocompleteCache,
            SavedSearchPatternStore savedSearchPatternStore,
            ApplicationSettings applicationSettings,
            ThreadGroupingService threadGroupingService,
            TimelinePlacementService timelinePlacementService, IColorValidator? colorValidator = null, IUiDispatcher? dispatcher = null)
            : this(documents, searchAutocompleteCache, savedSearchPatternStore, applicationSettings,
                threadGroupingService, timelinePlacementService, BuildData, colorValidator, dispatcher)
        {
        }

        internal MergedViewModel(ObservableCollection<DocumentViewModel> documents,
            SearchAutocompleteCache searchAutocompleteCache,
            SavedSearchPatternStore savedSearchPatternStore,
            ApplicationSettings applicationSettings,
            ThreadGroupingService threadGroupingService,
            TimelinePlacementService timelinePlacementService,
            Func<MergedDocumentItem[], MergeSource[], CancellationToken, MergedDataSnapshot> buildData, IColorValidator? colorValidator = null, IUiDispatcher? dispatcher = null)
        {
            _dispatcher = dispatcher ?? UiDispatcher.Current;
            _buildData = buildData;
            _documents = documents;
            _applicationSettings = applicationSettings;
            _threadGroupingService = threadGroupingService;
            _timelinePlacementService = timelinePlacementService;
            _colorSettings = new ColorRuleSet(applicationSettings.GetSelectedProfile().ColorSettings ?? new Colors.Configuration.ColorSettings(), colorValidator);
            _threadGroupingService.Changed += OnThreadGroupingChanged;
            _timelinePlacementService.Changed += OnTimelinePlacementChanged;

            Search = new SearchViewModel(
                pattern => new MergedSearchDocumentViewModel(this, pattern),
                searchAutocompleteCache,
                savedSearchPatternStore, _dispatcher);
            Search.CurrentLineChanged += NavigateToCentered;

            TimeRangeFilter = new MergedTimeRangeFilterViewModel();
            TimeRangeFilter.Changed += ScheduleFilter;

            ColumnSettings = applicationSettings.GetColumnSettings(ColumnSettingsKey);

            _rebuildTimer = _dispatcher.CreateTimer(TimeSpan.FromMilliseconds(300), () =>
            {
                _rebuildTimer!.Stop();
                _ = RebuildAsync();
            }, UiTimerPriority.Background);

            Lines = new GrowingLogLinesCollection(
                Array.Empty<ItemViewModel>(), Array.Empty<ItemViewModel>());

            _schema = MergedSchema.Build(Array.Empty<MergedDocumentItem>());
            CreateSchemaDependencies();

            NavigateToMarkerCommand = new DelegateCommand(index =>
            {
                if (index is int line)
                    NavigateTo(line);
            });

            ExcludeCommand = DelegateCommand.Create(
                (int fieldIndex) => _filterSettings.AddExclusions(
                    MetaInformation.GetIndexedFieldIndexByFieldIndex(fieldIndex), GetComponentsInSelectedLines(fieldIndex)));

            ExcludeAllButCommand = DelegateCommand.Create(
                (int fieldIndex) => _filterSettings.ExcludeAllExcept(
                    MetaInformation.GetIndexedFieldIndexByFieldIndex(fieldIndex), GetComponentsInSelectedLines(fieldIndex)));

            ClearExclusionsCommand = new DelegateCommand(() => _filterSettings.ClearAllExclusions());
            RetryRebuildCommand = new DelegateCommand(ScheduleRebuild);

            ClearFiltersCommand = new DelegateCommand(() =>
            {
                _filterSettings.ClearAllExclusions();
                TimeRangeFilter.Reset();
            });

            _documents.CollectionChanged += OnDocumentsChanged;
            SyncDocuments();
        }

        public string Title => Strings.Get("MergedFiles");

        public bool IsTimelineAtTop => _timelinePlacementService.IsAtTop;

        public bool IsTimelineVisible => _timelinePlacementService.IsVisible;

        private void OnTimelinePlacementChanged()
        {
            InvokePropertyChanged(nameof(IsTimelineAtTop));
            InvokePropertyChanged(nameof(IsTimelineVisible));
        }

        public SearchViewModel Search { get; }

        public ColorRuleSet ColorSettings => _colorSettings;

        public void SetColorSettings(ColorRuleSet settings)
        {
            _colorSettings = settings;
            InvokePropertyChanged(nameof(ColorSettings));
        }

        public string? RebuildError
        {
            get => _rebuildError;
            private set => SetAndRaiseIfChanged(ref _rebuildError, value);
        }

        public ICommand RetryRebuildCommand { get; }

        public LogMetaInformation MetaInformation => _schema.MetaInformation;

        public ColumnSettings ColumnSettings { get; }

        public LogGridModel CustomView => _gridModel;

        internal LogGridModel GetSearchGrid()
        {
            _searchGridModel ??= new LogGridModel(_schema.MetaInformation, ColumnSettings, hasSourceColumn: true);
            return _searchGridModel;
        }

        public bool CanFilter => true;

        public int ThreadFieldIndex => _schema.ThreadFieldIndex;

        public bool GroupByThread => _threadGroupingService.IsEnabled;

        public bool ShowGroupSeparators => _threadGroupingService.ShowSeparators;

        public MergedTimeRangeFilterViewModel TimeRangeFilter { get; }

        public ObservableCollection<FilterChipViewModel> ActiveFilters { get; } = new();

        public bool HaveExclusions => _filterSettings.HaveExclusions;

        public ICommand ClearFiltersCommand { get; }

        public ICommand ClearExclusionsCommand { get; }

        public ICommand ExcludeCommand { get; }

        public ICommand ExcludeAllButCommand { get; }

        public IEnumerable? SelectedItems
        {
            get => _selectedItems;
            set
            {
                if (_selectedItems == null && value == null) return;
                if (_selectedItems != null && value != null &&
                    _selectedItems.Cast<object>().SequenceEqual(value.Cast<object>())) return;
                _selectedItems = value;
                InvokePropertyChanged();
            }
        }

        public event Action? Rebuilt;

        private IReadOnlyList<MergedDocumentItem> Sources => _snapshot.Sources;

        internal MergedViewSnapshot GetSearchSnapshot() => _snapshot;

        internal int Revision { get; private set; }

        public ObservableCollection<MergedDocumentItem> AvailableDocuments { get; } = new();

        public GrowingLogLinesCollection Lines { get; }

        public NavigateToLineRequest NavigateToLineRequest { get; } = new();

        public DelegateCommand NavigateToMarkerCommand { get; }

        public bool IsLoading => _rebuildTimer.IsEnabled || _rebuildCts != null || IsAnySourceLoading();

        public bool IsActive
        {
            get => _isActive;
            set
            {
                if (_isActive == value)
                    return;

                _isActive = value;
                InvokePropertyChanged();

                if (value)
                {
                    _dataRevision++;
                    _requestVersion++;
                    _ = RebuildAsync();
                }
                else
                {
                    _rebuildTimer.Stop();
                    _rebuildPending = false;
                    _requestVersion++;
                    _rebuildCts?.Cancel();
                }
                InvokePropertyChanged(nameof(IsLoading));
            }
        }

        public TimeIndex MergedTimeIndex => _snapshot.Data.TimeIndex;

        public IReadOnlyList<TimelineSegment> TimelineSegments
        {
            get => _timelineSegments;
            private set => SetAndRaiseIfChanged(ref _timelineSegments, value);
        }

        public int TotalLineCount
        {
            get => _totalLineCount;
            private set => SetAndRaiseIfChanged(ref _totalLineCount, value);
        }

        public int CurrentItemIndex
        {
            get => _currentItemIndex;
            set
            {
                if (_currentItemIndex == value)
                    return;

                _currentItemIndex = value;
                InvokePropertyChanged();
                UpdateCurrentThread();
            }
        }

        public MergedThread? CurrentThread { get; private set; }

        public event Action? CurrentThreadChanged;

        private void UpdateCurrentThread()
        {
            MergedThread? thread = null;
            var index = CurrentItemIndex;
            if (GroupByThread && ThreadFieldIndex >= 0 && index >= 0 && index < Lines.Count
                && Lines[index] is MergedLineViewModel line)
                thread = new MergedThread(line.Source, line.GetFieldText(ThreadFieldIndex));

            if (Nullable.Equals(CurrentThread, thread))
                return;

            CurrentThread = thread;
            CurrentThreadChanged?.Invoke();
        }

        public int FirstVisibleIndex
        {
            get => _firstVisibleIndex;
            set
            {
                if (_firstVisibleIndex == value)
                    return;

                _firstVisibleIndex = value;
                InvokePropertyChanged();
                UpdateScrollPosition();
            }
        }

        public int ScrollPosition
        {
            get => _scrollPosition;
            private set => SetAndRaiseIfChanged(ref _scrollPosition, value);
        }

        public void NavigateTo(int lineNumber)
        {
            var index = GetVisibleIndex(lineNumber);
            if (index >= 0)
                NavigateToLineRequest.Raise(index);
        }

        public void NavigateToCentered(int lineNumber)
        {
            var index = GetVisibleIndex(lineNumber);
            if (index >= 0)
                NavigateToLineRequest.Raise(index, true);
        }

        public bool TryNavigateToDocumentLine(DocumentViewModel document, int lineNumber)
        {
            var sourceIndex = -1;
            for (var i = 0; i < Sources.Count; i++)
            {
                if (Sources[i].Document == document)
                {
                    sourceIndex = i;
                    break;
                }
            }

            if (sourceIndex < 0)
                return false;

            var mergedLines = _snapshot.Data.Lines;
            for (var i = 0; i < mergedLines.Length; i++)
            {
                var line = mergedLines[i];
                if (line.SourceIndex == sourceIndex && line.LineNumber == lineNumber)
                {
                    NavigateToCentered(i);
                    return true;
                }
            }

            return false;
        }

        public void Dispose()
        {
            _disposed = true;
            _rebuildPending = false;
            _requestVersion++;
            _rebuildCts?.Cancel();
            _rebuildTimer.Dispose();
            _documents.CollectionChanged -= OnDocumentsChanged;
            _threadGroupingService.Changed -= OnThreadGroupingChanged;
            _timelinePlacementService.Changed -= OnTimelinePlacementChanged;
            TimeRangeFilter.Changed -= ScheduleFilter;
            _filterSettings.ExclusionsChanged -= OnFilterChanged;
            Search.CurrentLineChanged -= NavigateToCentered;
            _gridModel.Dispose();
            _searchGridModel?.Dispose();
            _componentIndexer.Dispose();
            Search.Dispose();

            foreach (var document in _subscribed)
                document.LogViewModel.Lines.CollectionGrown -= OnDocumentGrown;

            _subscribed.Clear();
        }

        private void OnThreadGroupingChanged()
        {
            InvokePropertyChanged(nameof(GroupByThread));
            InvokePropertyChanged(nameof(ShowGroupSeparators));
            UpdateCurrentThread();
            foreach (var item in EnumerateItems())
                if (item is MergedLineViewModel)
                    break;
        }

        private IEnumerable<ItemViewModel> EnumerateItems() => Lines;

        private void OnFilterChanged()
        {
            RefreshActiveFilters();
            InvokePropertyChanged(nameof(HaveExclusions));
            ScheduleFilter();
        }

        private void OnDocumentsChanged(object? sender, EventArgs e)
        {
            SyncDocuments();
            ScheduleRebuild();
        }

        private void OnDocumentGrown(int _)
        {
            InvokePropertyChanged(nameof(IsLoading));
            _dataRevision++;
            ScheduleNextRebuild();
        }

        private void SyncDocuments()
        {
            var current = new HashSet<DocumentViewModel>(_documents);
            var previousByPath = AvailableDocuments.ToDictionary(item => item.Document.DocumentId,
                StringComparer.OrdinalIgnoreCase);

            for (var i = AvailableDocuments.Count - 1; i >= 0; i--)
            {
                if (!current.Contains(AvailableDocuments[i].Document))
                    AvailableDocuments.RemoveAt(i);
            }

            foreach (var document in _documents)
            {
                if (AvailableDocuments.Any(item => item.Document == document))
                    continue;

                previousByPath.TryGetValue(document.DocumentId, out var previous);
                var item = new MergedDocumentItem(document,
                    previous?.ColorIndex ?? _nextColorIndex++ % MergedViewPalette.Count)
                {
                    IsSelected = previous?.IsSelected ?? true
                };
                item.UpdateFieldMap(_schema.Fields);
                item.SelectionChanged += _ => ScheduleRebuild();
                AvailableDocuments.Add(item);
            }

            foreach (var document in _subscribed.Where(document => !current.Contains(document)).ToArray())
            {
                document.LogViewModel.Lines.CollectionGrown -= OnDocumentGrown;
                _subscribed.Remove(document);
            }
            foreach (var document in _documents)
            {
                if (!_subscribed.Add(document))
                    continue;

                document.LogViewModel.Lines.CollectionGrown += OnDocumentGrown;
            }
        }

        private void ScheduleRebuild()
        {
            _dataRevision++;
            ScheduleFilter();
        }

        private void ScheduleFilter()
        {
            _requestVersion++;
            _rebuildCts?.Cancel();
            ScheduleNextRebuild();
        }

        private void ScheduleNextRebuild()
        {
            if (!_isActive || _disposed)
                return;

            _rebuildPending = true;
            if (_rebuildCts != null || _rebuildTimer.IsEnabled)
                return;

            _rebuildTimer.Interval = IsAnySourceLoading()
                ? TimeSpan.FromMilliseconds(1000)
                : TimeSpan.FromMilliseconds(50);
            _rebuildTimer.Start();
            InvokePropertyChanged(nameof(IsLoading));
        }

        private bool IsAnySourceLoading()
        {
            foreach (var item in AvailableDocuments)
            {
                if (item.IsSelected && item.Document.LogViewModel.IsLoading)
                    return true;
            }

            return false;
        }

        private async Task RebuildAsync()
        {
            if (!_isActive || _disposed)
                return;

            if (_rebuildCts != null)
            {
                _rebuildPending = true;
                return;
            }
            _rebuildPending = false;
            RebuildError = null;
            using var cts = new CancellationTokenSource();
            _rebuildCts = cts;
            var token = cts.Token;
            var version = _requestVersion;
            var dataRevision = _dataRevision;
            InvokePropertyChanged(nameof(IsLoading));
            try
            {
                var data = _cachedDataRevision == dataRevision ? _cachedData : null;
                var schema = data != null ? _cachedSchema! : MergedSchema.Build(AvailableDocuments);
                var schemaChanged = !HasSameSchema(schema);
                var exclusions = schemaChanged
                    ? new Dictionary<int, IEnumerable<string>>()
                    : _filterSettings.Exclusions.ToDictionary(pair => pair.Key,
                        pair => (IEnumerable<string>)pair.Value.ToArray());
                var range = CaptureRange();
                if (data == null)
                {
                    var sources = AvailableDocuments.Where(item => item.IsSelected).OrderBy(item => item.ColorIndex)
                        .Select(item => new MergedDocumentItem(item.Document, item.ColorIndex)).ToArray();
                    schema.ApplyTo(sources);
                    var mergeSources = sources.Select(item =>
                        new MergeSource(item.Document.LineCount, item.Document.TimeIndex)).ToArray();
                    data = await Task.Run(() => _buildData(sources, mergeSources, token), token);
                    token.ThrowIfCancellationRequested();
                    _cachedData = data;
                    _cachedSchema = schema;
                    _cachedDataRevision = dataRevision;
                }

                var sourceExclusions = BuildSourceExclusions(exclusions, data.Sources, schema.MetaInformation);
                var snapshot = await Task.Run(() => BuildVisibleSnapshot(data, sourceExclusions, range, token), token);
                if (token.IsCancellationRequested || version != _requestVersion || !_isActive || _disposed)
                    return;

                _snapshot = snapshot;
                if (schemaChanged)
                {
                    _schema = schema;
                    _schema.ApplyTo(AvailableDocuments);
                    CreateSchemaDependencies();
                    ColumnSettings.ColumnWidths = null;
                }
                _componentIndexer.UpdateSources(snapshot.Sources);
                TimeRangeFilter.Refresh(data.TimeIndex, data.Lines.Length);
                TotalLineCount = data.Lines.Length;
                TimelineSegments = data.TimelineSegments;
                Lines.Reset(Array.Empty<ItemViewModel>(),
                    new VirtualList<MergedLineRef, ItemViewModel>(new ListItemProvider<MergedLineRef>(snapshot.Lines),
                        line => new MergedLineViewModel(snapshot.Sources[line.SourceIndex], line.LineNumber, line.Ticks,
                            decodedParts: _decodedParts)));
                if (schemaChanged)
                {
                    InvokePropertyChanged(nameof(CustomView));
                    InvokePropertyChanged(nameof(MetaInformation));
                    InvokePropertyChanged(nameof(ColumnSettings));
                    InvokePropertyChanged(nameof(ThreadFieldIndex));
                }
                InvokePropertyChanged(nameof(MergedTimeIndex));
                UpdateScrollPosition();
                Revision++;
                UpdateCurrentThread();
                Rebuilt?.Invoke();
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                Trace.TraceError($"Cannot rebuild merged view: {exception}");
                if (!token.IsCancellationRequested && version == _requestVersion && _isActive && !_disposed)
                    RebuildError = Strings.Get("MergedViewFailed");
            }
            finally
            {
                if (ReferenceEquals(_rebuildCts, cts))
                {
                    _rebuildCts = null;
                    if (_rebuildPending)
                        ScheduleNextRebuild();
                    InvokePropertyChanged(nameof(IsLoading));
                }
            }
        }

        private bool HasSameSchema(MergedSchema schema) =>
            schema.Fields.SequenceEqual(_schema.Fields) &&
            schema.MetaInformation.IndexedFieldNumbers.SequenceEqual(_schema.MetaInformation.IndexedFieldNumbers);

        private static MergedDataSnapshot BuildData(MergedDocumentItem[] sources, MergeSource[] mergeSources,
            CancellationToken token)
        {
            var lines = MergedLineOrder.Build(mergeSources, new List<MergedLineRef>(), token).ToArray();
            var timeIndex = MergedLineOrder.BuildTimeIndex(lines, new TimeIndex(), token);
            return new MergedDataSnapshot(lines, sources, timeIndex, BuildSegments(lines, sources, token),
                mergeSources.All(source => source.HasTime));
        }

        private void CreateSchemaDependencies()
        {
            if (_filterSettings != null)
                _filterSettings.ExclusionsChanged -= OnFilterChanged;

            _gridModel?.Dispose();
            _searchGridModel?.Dispose();
            _componentIndexer?.Dispose();
            _componentIndexer = new MergedComponentIndexer(_schema);
            _filterSettings = new FilterSettings(_componentIndexer, _schema.MetaInformation);
            _filterSettings.ExclusionsChanged += OnFilterChanged;
            _gridModel = new LogGridModel(_schema.MetaInformation, ColumnSettings,
                fieldName => new FilterViewModel(fieldName, _filterSettings, _componentIndexer,
                    _schema.MetaInformation, _dispatcher), hasSourceColumn: true);
            _searchGridModel = null;
            RefreshActiveFilters();
        }

        private void RefreshActiveFilters()
        {
            ActiveFilters.Clear();
            foreach (var (indexedFieldIndex, values) in _filterSettings.Exclusions)
            {
                var excludedValues = values.ToList();
                if (excludedValues.Count == 0)
                    continue;

                var fieldName = MetaInformation.GetFieldNameByIndexedFieldIndex(indexedFieldIndex);
                if (string.IsNullOrEmpty(fieldName))
                    continue;

                ActiveFilters.Add(new FilterChipViewModel(fieldName, indexedFieldIndex,
                    excludedValues, _filterSettings));
            }

            InvokePropertyChanged(nameof(HaveExclusions));
        }

        private IEnumerable<string> GetComponentsInSelectedLines(int fieldIndex)
        {
            var lines = SelectedItems?.OfType<MergedLineViewModel>() ?? Enumerable.Empty<MergedLineViewModel>();
            return lines.Select(line => line[fieldIndex].OriginalText ?? string.Empty).Distinct();
        }

        private (bool Active, double Lower, double Upper) CaptureRange() =>
            (TimeRangeFilter.IsAvailable && (TimeRangeFilter.LowerValue > TimeRangeFilter.Minimum ||
                                            TimeRangeFilter.UpperValue < TimeRangeFilter.Maximum),
                TimeRangeFilter.LowerValue, TimeRangeFilter.UpperValue);

        private static MergedViewSnapshot BuildVisibleSnapshot(MergedDataSnapshot data,
            IReadOnlyDictionary<int, IReadOnlyDictionary<int, IEnumerable<string>>> sourceExclusions,
            (bool Active, double Lower, double Upper) range, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var hasTime = data.TimeIndex.HasTime && data.TimeIndex.IsMonotonic &&
                          data.TimeIndex.Count > 0 && data.TimeIndex.MaxTicks > data.TimeIndex.MinTicks;
            var minimum = hasTime ? (double)data.TimeIndex.MinTicks : 0;
            var maximum = hasTime ? (double)data.TimeIndex.MaxTicks : data.Lines.Length;
            var lower = range.Active ? Math.Clamp(range.Lower, minimum, maximum) : minimum;
            var upper = range.Active ? Math.Clamp(range.Upper, minimum, maximum) : maximum;
            var hasRange = lower > minimum || upper < maximum;
            if (sourceExclusions.Count == 0 && !hasRange)
                return new MergedViewSnapshot(data, data.Lines, null);

            var lines = new List<MergedLineRef>();
            var fullIndexMap = new List<int>();
            var start = 0;
            var end = data.Lines.Length;
            if (hasRange)
            {
                if (!hasTime)
                {
                    start = (int)lower;
                    end = (int)upper;
                }
                else if (data.AllSourcesHaveTime &&
                         data.TimeIndex.FindLineRange((long)lower, (long)upper) is { } timeRange)
                {
                    start = timeRange.StartLine;
                    end = timeRange.EndLine;
                }
            }

            for (var i = start; i < end; i++)
            {
                if ((i & 1023) == 0)
                    token.ThrowIfCancellationRequested();
                var line = data.Lines[i];
                if (hasRange && hasTime && line.Ticks >= 0 && (line.Ticks < (long)lower || line.Ticks > (long)upper))
                    continue;

                if (sourceExclusions.TryGetValue(line.SourceIndex, out var documentExclusions) &&
                    documentExclusions.Count > 0 &&
                    !data.Sources[line.SourceIndex].Document.Indexer.IsLineIncluded(line.LineNumber, documentExclusions))
                    continue;

                lines.Add(line);
                fullIndexMap.Add(i);
            }

            return new MergedViewSnapshot(data, lines.ToArray(), fullIndexMap.ToArray());
        }

        private static Dictionary<int, IReadOnlyDictionary<int, IEnumerable<string>>> BuildSourceExclusions(
            IReadOnlyDictionary<int, IEnumerable<string>> exclusions, IReadOnlyList<MergedDocumentItem> sources,
            LogMetaInformation metaInformation)
        {
            var result = new Dictionary<int, IReadOnlyDictionary<int, IEnumerable<string>>>();
            if (exclusions.Count == 0)
                return result;

            var mergedIndexedFields = metaInformation.IndexedFieldNumbers;
            for (var sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
            {
                var item = sources[sourceIndex];
                Dictionary<int, IEnumerable<string>>? translated = null;

                foreach (var (mergedIndexedFieldIndex, values) in exclusions)
                {
                    if (mergedIndexedFieldIndex < 0 || mergedIndexedFieldIndex >= mergedIndexedFields.Length)
                        continue;

                    var mergedFieldIndex = mergedIndexedFields[mergedIndexedFieldIndex];
                    var sourceFieldIndex = item.GetSourceFieldIndex(mergedFieldIndex);
                    if (sourceFieldIndex < 0)
                        continue;

                    var indexedFieldIndex = item.Document.MetaInformation.GetIndexedFieldIndexByFieldIndex(sourceFieldIndex);
                    if (indexedFieldIndex < 0)
                        continue;

                    translated ??= new Dictionary<int, IEnumerable<string>>();
                    translated[indexedFieldIndex] = values;
                }

                if (translated != null)
                    result[sourceIndex] = translated;
            }

            return result;
        }

        private void UpdateScrollPosition()
        {
            var firstVisible = _firstVisibleIndex;
            if (firstVisible < 0)
            {
                ScrollPosition = -1;
                return;
            }

            if (_snapshot.FullIndexMap is { } map)
            {
                ScrollPosition = firstVisible < map.Length ? map[firstVisible] : -1;
                return;
            }

            ScrollPosition = firstVisible < _snapshot.Data.Lines.Length ? firstVisible : -1;
        }

        private int GetVisibleIndex(int mergedIndex)
        {
            if (_snapshot.FullIndexMap is not { } map)
                return mergedIndex;

            if (map.Length == 0)
                return -1;

            var index = Array.BinarySearch(map, mergedIndex);
            if (index >= 0)
                return index;

            var insertion = ~index;
            return insertion < map.Length ? insertion : map.Length - 1;
        }

        private static IReadOnlyList<TimelineSegment> BuildSegments(IReadOnlyList<MergedLineRef> lines,
            IReadOnlyList<MergedDocumentItem> sources, CancellationToken token)
        {
            if (lines.Count == 0 || sources.Count == 0)
                return Array.Empty<TimelineSegment>();

            var segments = new List<TimelineSegment>();
            var start = 0;
            var current = lines[0].SourceIndex;

            for (var i = 1; i <= lines.Count; i++)
            {
                if ((i & 1023) == 0)
                    token.ThrowIfCancellationRequested();
                var sourceIndex = i < lines.Count ? lines[i].SourceIndex : -1;
                if (sourceIndex == current)
                    continue;

                segments.Add(new TimelineSegment(start, i, sources[current].TimelineColor));
                if (segments.Count > MaxTimelineSegments)
                    return BuildDownsampledSegments(lines, sources, token);

                start = i;
                current = sourceIndex;
            }

            return segments;
        }

        private static IReadOnlyList<TimelineSegment> BuildDownsampledSegments(IReadOnlyList<MergedLineRef> lines,
            IReadOnlyList<MergedDocumentItem> sources, CancellationToken token)
        {
            var lineCount = lines.Count;
            var bucketCount = Math.Min(MaxTimelineSegments, lineCount);
            var counts = new int[sources.Count];
            var segments = new List<TimelineSegment>();
            var bucketStart = 0;
            var bucketSize = (double)lineCount / bucketCount;
            var lastDominant = -1;

            for (var bucket = 0; bucket < bucketCount; bucket++)
            {
                token.ThrowIfCancellationRequested();
                var bucketEnd = bucket == bucketCount - 1
                    ? lineCount
                    : (int)Math.Round((bucket + 1) * bucketSize);
                if (bucketEnd <= bucketStart)
                    continue;

                Array.Clear(counts, 0, counts.Length);
                for (var i = bucketStart; i < bucketEnd; i++)
                    counts[lines[i].SourceIndex]++;

                var dominant = 0;
                for (var s = 1; s < counts.Length; s++)
                {
                    if (counts[s] > counts[dominant])
                        dominant = s;
                }

                var brush = sources[dominant].TimelineColor;
                if (segments.Count > 0 && dominant == lastDominant)
                    segments[^1] = new TimelineSegment(segments[^1].StartLine, bucketEnd, brush);
                else
                    segments.Add(new TimelineSegment(bucketStart, bucketEnd, brush));

                lastDominant = dominant;
                bucketStart = bucketEnd;
            }

            return segments;
        }
    }
}
