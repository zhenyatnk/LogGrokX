using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LogGrokX.Colors;
using LogGrokX.Controls;
using LogGrokX.Controls.GridView;
using LogGrokX.Controls.ListControls;
using LogGrokX.Data;
using LogGrokX.Data.Virtualization;
using LogGrokX.Filter;
using LogGrokX.Search;
using ColorRules = LogGrokX.Colors.ColorSettings;

namespace LogGrokX.MergedView
{
    public sealed class MergedViewModel : ViewModelBase, IDisposable
    {
        private const int MaxTimelineSegments = 20000;
        private const string ColumnSettingsKey = "__merged_files_view__";

        private readonly ObservableCollection<DocumentViewModel> _documents;
        private readonly ApplicationSettings _applicationSettings;
        private readonly ThreadGroupingService _threadGroupingService;
        private readonly HashSet<DocumentViewModel> _subscribed = new();
        private readonly List<MergedDocumentItem> _selectedSources = new();
        private readonly List<MergedLineRef> _mergedBuffer = new();
        private readonly List<MergedLineRef> _visibleBuffer = new();
        private readonly List<int> _visibleToFull = new();
        private readonly List<MergeSource> _mergeSources = new();
        private readonly TimeIndex _mergedTimeIndex = new();
        private readonly DispatcherTimer _rebuildTimer;
        private ColorRules _colorSettings;

        private MergedSchema _schema = null!;
        private MergedComponentIndexer _componentIndexer = null!;
        private IFilterSettings _filterSettings = null!;
        private GridViewFactory _viewFactory = null!;
        private GridViewFactory? _searchViewFactory;
        private IReadOnlyList<string> _lastFields = Array.Empty<string>();

        private bool _isActive;
        private bool _isFiltered;
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
            ThreadGroupingService threadGroupingService)
        {
            _documents = documents;
            _applicationSettings = applicationSettings;
            _threadGroupingService = threadGroupingService;
            _colorSettings = new ColorRules(applicationSettings.GetSelectedProfile().ColorSettings ?? new Colors.Configuration.ColorSettings());
            _threadGroupingService.Changed += OnThreadGroupingChanged;

            Search = new SearchViewModel(
                pattern => new MergedSearchDocumentViewModel(this, pattern),
                searchAutocompleteCache,
                savedSearchPatternStore);
            Search.CurrentLineChanged += NavigateToCentered;

            TimeRangeFilter = new MergedTimeRangeFilterViewModel();
            TimeRangeFilter.Changed += ScheduleRebuild;

            ColumnSettings = applicationSettings.GetColumnSettings(ColumnSettingsKey);

            _rebuildTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };
            _rebuildTimer.Tick += (_, _) =>
            {
                _rebuildTimer.Stop();
                Rebuild();
            };

            Lines = new GrowingLogLinesCollection(
                Array.Empty<ItemViewModel>(),
                new VirtualList<MergedLineRef, ItemViewModel>(
                    new ListItemProvider<MergedLineRef>(Array.Empty<MergedLineRef>()), CreateLine));

            _schema = MergedSchema.Build(Array.Empty<MergedDocumentItem>());
            CreateSchemaDependencies();

            NavigateToMarkerCommand = new DelegateCommand(index =>
            {
                if (index is int line)
                    NavigateTo(line);
            });

            ExcludeCommand = DelegateCommand.Create(
                (int fieldIndex) => _filterSettings.AddExclusions(fieldIndex, GetComponentsInSelectedLines(fieldIndex)));

            ExcludeAllButCommand = DelegateCommand.Create(
                (int fieldIndex) => _filterSettings.ExcludeAllExcept(fieldIndex, GetComponentsInSelectedLines(fieldIndex)));

            ClearExclusionsCommand = new DelegateCommand(() => _filterSettings.ClearAllExclusions());

            ClearFiltersCommand = new DelegateCommand(() =>
            {
                _filterSettings.ClearAllExclusions();
                TimeRangeFilter.Reset();
            });

            _documents.CollectionChanged += OnDocumentsChanged;
            SyncDocuments();
        }

        public string Title => "Merged files";

        public SearchViewModel Search { get; }

        public ColorSettings ColorSettings => _colorSettings;

        public void SetColorSettings(ColorRules settings)
        {
            _colorSettings = settings;
            InvokePropertyChanged(nameof(ColorSettings));
            _rebuildTimer.Stop();
            Rebuild();
        }

        public LogMetaInformation MetaInformation => _schema.MetaInformation;

        public ColumnSettings ColumnSettings { get; }

        public ViewBase CustomView => _viewFactory.CreateView(ColumnSettings.ColumnWidths, SourceStripeTemplate,
            "Source.Document.FoldingState");

        internal ViewBase CreateSearchView()
        {
            _searchViewFactory ??= new GridViewFactory(_schema.MetaInformation, false, null);
            return _searchViewFactory.CreateView(ColumnSettings.ColumnWidths, SourceStripeTemplate,
                "Source.Document.FoldingState");
        }

        public bool CanFilter => true;

        public int ThreadFieldIndex => _schema.ThreadFieldIndex;

        public bool GroupByThread => _threadGroupingService.IsEnabled;

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

        internal IReadOnlyList<MergedLineRef> VisibleLines => _isFiltered ? _visibleBuffer : _mergedBuffer;

        internal int[]? GetVisibleFullIndexMap() => _isFiltered ? _visibleToFull.ToArray() : null;

        internal IReadOnlyList<MergedDocumentItem> Sources => _selectedSources;

        internal int Revision { get; private set; }

        public ObservableCollection<MergedDocumentItem> AvailableDocuments { get; } = new();

        public GrowingLogLinesCollection Lines { get; }

        public NavigateToLineRequest NavigateToLineRequest { get; } = new();

        public DelegateCommand NavigateToMarkerCommand { get; }

        public bool IsLoading => _rebuildTimer.IsEnabled || IsAnySourceLoading();

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
                    Rebuild();
                }
                else
                {
                    _rebuildTimer.Stop();
                }
            }
        }

        public TimeIndex MergedTimeIndex => _mergedTimeIndex;

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
            set => SetAndRaiseIfChanged(ref _currentItemIndex, value);
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
            for (var i = 0; i < _selectedSources.Count; i++)
            {
                if (_selectedSources[i].Document == document)
                {
                    sourceIndex = i;
                    break;
                }
            }

            if (sourceIndex < 0)
                return false;

            for (var i = 0; i < _mergedBuffer.Count; i++)
            {
                var line = _mergedBuffer[i];
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
            _rebuildTimer.Stop();
            _documents.CollectionChanged -= OnDocumentsChanged;
            _threadGroupingService.Changed -= OnThreadGroupingChanged;
            TimeRangeFilter.Changed -= ScheduleRebuild;
            _filterSettings.ExclusionsChanged -= OnFilterChanged;
            Search.CurrentLineChanged -= NavigateToCentered;
            _componentIndexer.Dispose();
            foreach (var document in Search.Documents.ToArray())
                document.Dispose();

            foreach (var document in _subscribed)
                document.LogViewModel.Lines.CollectionGrown -= OnDocumentGrown;

            _subscribed.Clear();
        }

        private static DataTemplate SourceStripeTemplate { get; } = CreateSourceStripeTemplate();

        private static DataTemplate CreateSourceStripeTemplate()
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(FrameworkElement.WidthProperty, 12.0);
            border.SetValue(FrameworkElement.HeightProperty, 14.0);
            border.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            border.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
            border.SetBinding(Border.BackgroundProperty, new Binding("Source.TimelineBrush"));
            border.SetBinding(FrameworkElement.ToolTipProperty, new Binding("SourceTitle"));
            return new DataTemplate(typeof(DependencyObject)) { VisualTree = border };
        }

        private ItemViewModel CreateLine(MergedLineRef line) =>
            new MergedLineViewModel(_selectedSources[line.SourceIndex], line.LineNumber, line.Ticks);

        private void OnThreadGroupingChanged()
        {
            InvokePropertyChanged(nameof(GroupByThread));
            foreach (var item in EnumerateItems())
                if (item is MergedLineViewModel)
                    break;
        }

        private IEnumerable<ItemViewModel> EnumerateItems() => Lines;

        private void OnFilterChanged()
        {
            RefreshActiveFilters();
            InvokePropertyChanged(nameof(HaveExclusions));
            ScheduleRebuild();
        }

        private void OnDocumentsChanged(object? sender, EventArgs e)
        {
            SyncDocuments();
            ScheduleRebuild();
        }

        private void OnDocumentGrown(int _)
        {
            InvokePropertyChanged(nameof(IsLoading));
            ScheduleRebuild();
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
            if (!_isActive)
                return;

            if (_rebuildTimer.IsEnabled)
                return;

            _rebuildTimer.Interval = IsAnySourceLoading()
                ? TimeSpan.FromMilliseconds(1000)
                : TimeSpan.FromMilliseconds(300);
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

        private void Rebuild()
        {
            if (!_isActive)
                return;

            _selectedSources.Clear();
            foreach (var item in AvailableDocuments)
            {
                if (item.IsSelected)
                    _selectedSources.Add(item);
            }

            _selectedSources.Sort(static (left, right) => left.ColorIndex.CompareTo(right.ColorIndex));

            var schemaChanged = EnsureSchema();
            _componentIndexer.UpdateSources(_selectedSources);

            _mergeSources.Clear();
            foreach (var item in _selectedSources)
                _mergeSources.Add(new MergeSource(item.Document.LineCount, item.Document.TimeIndex));

            MergedLineOrder.Build(_mergeSources, _mergedBuffer);
            MergedLineOrder.BuildTimeIndex(_mergedBuffer, _mergedTimeIndex);
            _isFiltered = BuildVisibleBuffer();

            var visibleLines = _isFiltered ? _visibleBuffer : _mergedBuffer;
            Lines.Reset(
                Array.Empty<ItemViewModel>(),
                new VirtualList<MergedLineRef, ItemViewModel>(new ListItemProvider<MergedLineRef>(visibleLines),
                    CreateLine));

            TimeRangeFilter.Refresh(_mergedTimeIndex, _mergedBuffer.Count);
            TimelineSegments = BuildSegments(_mergedBuffer, _selectedSources);
            TotalLineCount = _mergedBuffer.Count;
            UpdateScrollPosition();
            Revision++;
            if (schemaChanged)
            {
                InvokePropertyChanged(nameof(CustomView));
                InvokePropertyChanged(nameof(MetaInformation));
                InvokePropertyChanged(nameof(ColumnSettings));
            }

            InvokePropertyChanged(nameof(IsLoading));
            Rebuilt?.Invoke();
        }

        private bool EnsureSchema()
        {
            var fields = MergedSchema.BuildFields(AvailableDocuments);

            if (fields.SequenceEqual(_lastFields))
            {
                _schema.ApplyTo(AvailableDocuments);
                return false;
            }

            _lastFields = fields;
            _schema = MergedSchema.Build(AvailableDocuments);
            _schema.ApplyTo(AvailableDocuments);
            ColumnSettings.ColumnWidths = null;
            CreateSchemaDependencies();
            InvokePropertyChanged(nameof(ThreadFieldIndex));
            return true;
        }

        private void CreateSchemaDependencies()
        {
            if (_filterSettings != null)
                _filterSettings.ExclusionsChanged -= OnFilterChanged;

            _componentIndexer?.Dispose();
            _componentIndexer = new MergedComponentIndexer(_schema);
            _filterSettings = new FilterSettings(_componentIndexer, _schema.MetaInformation);
            _filterSettings.ExclusionsChanged += OnFilterChanged;
            _viewFactory = new GridViewFactory(_schema.MetaInformation, true,
                fieldName => new FilterViewModel(fieldName, _filterSettings, _componentIndexer, _schema.MetaInformation));
            _searchViewFactory = null;
            RefreshActiveFilters();
        }

        private void RefreshActiveFilters()
        {
            ActiveFilters.Clear();
            foreach (var (mergedFieldIndex, values) in _filterSettings.Exclusions)
            {
                var excludedValues = values.ToList();
                if (excludedValues.Count == 0)
                    continue;

                if (mergedFieldIndex < 0 || mergedFieldIndex >= _schema.Fields.Count)
                    continue;

                ActiveFilters.Add(new FilterChipViewModel(_schema.Fields[mergedFieldIndex], mergedFieldIndex,
                    excludedValues, _filterSettings));
            }

            InvokePropertyChanged(nameof(HaveExclusions));
        }

        private IEnumerable<string> GetComponentsInSelectedLines(int fieldIndex)
        {
            var lines = SelectedItems?.OfType<MergedLineViewModel>() ?? Enumerable.Empty<MergedLineViewModel>();
            return lines.Select(line => line[fieldIndex].OriginalText ?? string.Empty).Distinct();
        }

        private bool BuildVisibleBuffer()
        {
            _visibleBuffer.Clear();
            _visibleToFull.Clear();

            var exclusions = _filterSettings.Exclusions;
            var timeRange = TimeRangeFilter.TimeRange;
            var lineRange = TimeRangeFilter.LineRange;

            if (exclusions.Count == 0 && timeRange == null && lineRange == null)
                return false;

            var sourceExclusions = BuildSourceExclusions(exclusions);

            for (var i = 0; i < _mergedBuffer.Count; i++)
            {
                var line = _mergedBuffer[i];

                if (lineRange is { } range && (i < range.From || i >= range.To))
                    continue;

                if (timeRange is { } time && line.Ticks >= 0 && (line.Ticks < time.From || line.Ticks > time.To))
                    continue;

                if (sourceExclusions.TryGetValue(line.SourceIndex, out var documentExclusions) &&
                    documentExclusions.Count > 0 &&
                    !_selectedSources[line.SourceIndex].Document.Indexer.IsLineIncluded(line.LineNumber, documentExclusions))
                    continue;

                _visibleBuffer.Add(line);
                _visibleToFull.Add(i);
            }

            return true;
        }

        private Dictionary<int, IReadOnlyDictionary<int, IEnumerable<string>>> BuildSourceExclusions(
            IReadOnlyDictionary<int, IEnumerable<string>> exclusions)
        {
            var result = new Dictionary<int, IReadOnlyDictionary<int, IEnumerable<string>>>();
            if (exclusions.Count == 0)
                return result;

            for (var sourceIndex = 0; sourceIndex < _selectedSources.Count; sourceIndex++)
            {
                var item = _selectedSources[sourceIndex];
                Dictionary<int, IEnumerable<string>>? translated = null;

                foreach (var (mergedFieldIndex, values) in exclusions)
                {
                    if (mergedFieldIndex < 0 || mergedFieldIndex >= _schema.Fields.Count)
                        continue;

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

            if (_isFiltered)
            {
                ScrollPosition = firstVisible < _visibleToFull.Count ? _visibleToFull[firstVisible] : -1;
                return;
            }

            ScrollPosition = firstVisible < _mergedBuffer.Count ? firstVisible : -1;
        }

        private int GetVisibleIndex(int mergedIndex)
        {
            if (!_isFiltered)
                return mergedIndex;

            if (_visibleToFull.Count == 0)
                return -1;

            var index = _visibleToFull.BinarySearch(mergedIndex);
            if (index >= 0)
                return index;

            var insertion = ~index;
            return insertion < _visibleToFull.Count ? insertion : _visibleToFull.Count - 1;
        }

        private static IReadOnlyList<TimelineSegment> BuildSegments(IReadOnlyList<MergedLineRef> lines,
            IReadOnlyList<MergedDocumentItem> sources)
        {
            if (lines.Count == 0 || sources.Count == 0)
                return Array.Empty<TimelineSegment>();

            var segments = new List<TimelineSegment>();
            var start = 0;
            var current = lines[0].SourceIndex;

            for (var i = 1; i <= lines.Count; i++)
            {
                var sourceIndex = i < lines.Count ? lines[i].SourceIndex : -1;
                if (sourceIndex == current)
                    continue;

                segments.Add(new TimelineSegment(start, i, sources[current].TimelineBrush));
                if (segments.Count > MaxTimelineSegments)
                    return BuildDownsampledSegments(lines, sources);

                start = i;
                current = sourceIndex;
            }

            return segments;
        }

        private static IReadOnlyList<TimelineSegment> BuildDownsampledSegments(IReadOnlyList<MergedLineRef> lines,
            IReadOnlyList<MergedDocumentItem> sources)
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

                var brush = sources[dominant].TimelineBrush;
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
