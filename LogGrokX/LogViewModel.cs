using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using LogGrokX.Controls;
using LogGrokX.Controls.ListControls;
using LogGrokX.Controls.TextRender;
using LogGrokX.Data;
using LogGrokX.Platform;
using LogGrokX.Filter;
using LogGrokX.Localization;

namespace LogGrokX
{
    public class LogViewModel : ViewModelBase, IDisposable
    {
        private readonly IDesktopService _desktop;
        private readonly LogGridModel _gridModel;
        private double _progress;
        private readonly LogModelFacade _logModelFacade;
        private bool _isLoading;
        private IEnumerable? _selectedItems;
        private long _selectedCount;
        private readonly LineViewModelCollectionProvider _lineViewModelCollectionProvider;
        private Regex? _highlightRegex;
        private Func<int, int> _getIndexByValue;
        private readonly FilterSettings _filterSettings;
        private readonly Selection _markedLines;
        private readonly TimelinePlacementService _timelinePlacementService;
        private readonly ThreadGroupingService _threadGroupingService;
        private readonly int _threadFieldIndex;
        private int _currentItemIndex = -1;
        private readonly IReadOnlyList<ItemViewModel> _headerCollection;
        private readonly CancellationTokenSource _loadingCancellation = new();
        private bool _disposed;

        public LogViewModel(
            LogModelFacade logModelFacade,
            LineViewModelCollectionProvider lineViewModelCollectionProvider,
            LogGridModel gridModel,
            FilterSettings filterSettings,
            ColumnSettings columnSettings,
            TimeRangeFilterViewModel timeRangeFilter,
            Selection markedLines,
            TimelinePlacementService timelinePlacementService,
            ThreadGroupingService threadGroupingService,
            TextViewSharedFoldingState foldingState, IDesktopService desktop, BulkOperationService bulkOperations)
        {
            _desktop = desktop ?? throw new ArgumentNullException(nameof(desktop));
            BulkOperations = bulkOperations ?? throw new ArgumentNullException(nameof(bulkOperations));
            _logModelFacade = logModelFacade;
            _filterSettings = filterSettings;
            FoldingState = foldingState;
            TimeRangeFilter = timeRangeFilter;
            _markedLines = markedLines;
            _timelinePlacementService = timelinePlacementService;
            _threadGroupingService = threadGroupingService;
            _threadFieldIndex = Array.IndexOf(logModelFacade.MetaInformation.FieldNames, "Thread");
            _timelinePlacementService.Changed += OnTimelinePlacementChanged;
            _threadGroupingService.Changed += OnThreadGroupingChanged;
            
            var lineProvider = _logModelFacade.LineProvider;
            var lineParser = _logModelFacade.LineParser;
            
            _lineViewModelCollectionProvider = lineViewModelCollectionProvider;
            filterSettings.ExclusionsChanged += UpdateFilteredCollection;
            filterSettings.ExclusionsChanged += RefreshActiveFilters;
            filterSettings.TimeRangeChanged += UpdateFilteredCollection;
            filterSettings.LineRangeChanged += UpdateFilteredCollection;
            markedLines.Changed += RefreshMarkers;

            IReadOnlyList<ItemViewModel> lineCollection;
            (_headerCollection, lineCollection, _getIndexByValue) 
                = lineViewModelCollectionProvider.GetLogLinesCollection(lineProvider);
            
            Lines = new GrowingLogLinesCollection(_headerCollection, lineCollection);
            
            ExcludeCommand = DelegateCommand.Create(
                (int componentIndex) => ExcludeSelectedComponents(componentIndex, false));

            ExcludeAllButCommand = DelegateCommand.Create(
                (int componentIndex) => ExcludeSelectedComponents(componentIndex, true));
            
            ClearExclusionsCommand = new DelegateCommand(() => _filterSettings.ClearAllExclusions());

            ClearFiltersCommand = new DelegateCommand(() =>
            {
                _filterSettings.ClearAllExclusions();
                TimeRangeFilter.Reset();
            });

            NavigateToMarkerCommand = new DelegateCommand(index =>
            {
                if (index is int logLineNumber)
                    NavigateTo(logLineNumber);
            });

            CopySelectedItemsToClipboardCommand = new DelegateCommand(CopySelectedItemsToClipboard, 
                () => SelectedItems?.Cast<object>().Any() ?? false); 
            
            _gridModel = gridModel;
            ColumnSettings = columnSettings;
            RefreshActiveFilters();
            TotalLineCount = _logModelFacade.LineCount;
            RefreshMarkers();
            UpdateDocumentWhileLoading();
            UpdateProgress();
        }

        public BulkOperationService BulkOperations { get; }

        public ObservableCollection<FilterChipViewModel> ActiveFilters { get; } = new();

        public ObservableCollection<int> Markers { get; } = new();

        public ICommand NavigateToMarkerCommand { get; }

        private int _totalLineCount;
        public int TotalLineCount
        {
            get => _totalLineCount;
            private set => SetAndRaiseIfChanged(ref _totalLineCount, value);
        }

        private bool[] _searchMatches = Array.Empty<bool>();
        public bool[] SearchMatches
        {
            get => _searchMatches;
            private set => SetAndRaiseIfChanged(ref _searchMatches, value);
        }

        public void SetSearchMatches(bool[] buckets) => SearchMatches = buckets;

        private int _searchMatchLine = -1;
        public int SearchMatchLine
        {
            get => _searchMatchLine;
            private set => SetAndRaiseIfChanged(ref _searchMatchLine, value);
        }

        public void SetSearchMatchLine(int line) => SearchMatchLine = line;

        private IReadOnlyList<int> _searchSelectedLines = Array.Empty<int>();
        public IReadOnlyList<int> SearchSelectedLines
        {
            get => _searchSelectedLines;
            private set => SetAndRaiseIfChanged(ref _searchSelectedLines, value);
        }

        public void SetSearchSelectedLines(IReadOnlyList<int> lines) => SearchSelectedLines = lines;

        private int _firstVisibleIndex = -1;
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

        private int _scrollPosition = -1;
        public int ScrollPosition
        {
            get => _scrollPosition;
            private set => SetAndRaiseIfChanged(ref _scrollPosition, value);
        }

        private void UpdateScrollPosition()
        {
            var firstVisible = _firstVisibleIndex;
            if (firstVisible < 0 || firstVisible >= Lines.Count)
            {
                ScrollPosition = -1;
                return;
            }

            for (var i = firstVisible; i < Lines.Count; i++)
            {
                if (Lines[i] is LineViewModel line)
                {
                    ScrollPosition = line.Index;
                    return;
                }
            }

            ScrollPosition = -1;
        }

        public TimeRangeFilterViewModel TimeRangeFilter { get; }

        public bool IsTimelineAtTop => _timelinePlacementService.IsAtTop;

        public bool IsTimelineVisible => _timelinePlacementService.IsVisible;

        private void OnTimelinePlacementChanged()
        {
            InvokePropertyChanged(nameof(IsTimelineAtTop));
            InvokePropertyChanged(nameof(IsTimelineVisible));
        }

        public bool GroupByThread => _threadGroupingService.IsEnabled;

        public bool ShowGroupSeparators => _threadGroupingService.ShowSeparators;

        public int ThreadFieldIndex => _threadFieldIndex;

        private void OnThreadGroupingChanged()
        {
            InvokePropertyChanged(nameof(GroupByThread));
            InvokePropertyChanged(nameof(ShowGroupSeparators));
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            CancelSelectionOperation();
            _loadingCancellation.Cancel();
            _loadingCancellation.Dispose();
            _timelinePlacementService.Changed -= OnTimelinePlacementChanged;
            _threadGroupingService.Changed -= OnThreadGroupingChanged;
            _filterSettings.ExclusionsChanged -= UpdateFilteredCollection;
            _filterSettings.ExclusionsChanged -= RefreshActiveFilters;
            _filterSettings.TimeRangeChanged -= UpdateFilteredCollection;
            _filterSettings.LineRangeChanged -= UpdateFilteredCollection;
            _markedLines.Changed -= RefreshMarkers;
        }

        public bool HaveExclusions => _filterSettings.HaveExclusions;

        private void RefreshActiveFilters()
        {
            ActiveFilters.Clear();
            foreach (var (indexedComponentIndex, values) in _filterSettings.Exclusions)
            {
                var excludedValues = values.ToList();
                if (excludedValues.Count == 0) continue;

                var fieldName = MetaInformation.GetFieldNameByIndexedFieldIndex(indexedComponentIndex);
                ActiveFilters.Add(new FilterChipViewModel(fieldName, indexedComponentIndex,
                    excludedValues, _filterSettings));
            }

            InvokePropertyChanged(nameof(HaveExclusions));
        }

        private void RefreshMarkers()
        {
            Markers.Clear();

            var totalLineCount = TotalLineCount;
            var exclusions = _filterSettings.Exclusions;
            foreach (var logLineIndex in _markedLines)
            {
                if (logLineIndex < 0 || (totalLineCount > 0 && logLineIndex >= totalLineCount))
                    continue;

                if (!_logModelFacade.Indexer.IsLineIncluded(logLineIndex, exclusions))
                    continue;

                Markers.Add(logLineIndex);
            }
        }

        public ColumnSettings ColumnSettings
        {
            get;
        }

        public TextViewSharedFoldingState FoldingState { get; }

        public int CurrentItemIndex
        {
            get => _currentItemIndex;
            set
            {
                if (_currentItemIndex == value)
                    return;

                _currentItemIndex = value;
                InvokePropertyChanged();
            }
        }

        public int CurrentOriginalLine
        {
            get
            {
                var index = CurrentItemIndex;
                if (index < 0 || index >= Lines.Count)
                    return -1;

                return Lines[index] is LineViewModel line ? line.Index : -1;
            }
        }

        public bool CanFilter => true;

        public LogMetaInformation MetaInformation => _logModelFacade.MetaInformation;

        public ICommand CopySelectedItemsToClipboardCommand { get; }
        
        public ICommand ExcludeCommand { get; }

        public ICommand ExcludeAllButCommand { get; }
        
        public ICommand ClearExclusionsCommand { get; }

        public ICommand ClearFiltersCommand { get; }

        public long SelectedCount
        {
            get => _selectedCount;
            set => SetAndRaiseIfChanged(ref _selectedCount, value);
        }

        public IEnumerable? SelectedItems
        {
            get => _selectedItems;
            set
            {
                if (_selectedItems == null && value == null) return;
                _selectedItems = value;
                InvokePropertyChanged();
            }
        }

        public Regex? HighlightRegex
        {
            get => _highlightRegex;
            set => SetAndRaiseIfChanged(ref _highlightRegex, value);
        }

        public double Progress
        {
            get => _progress;
            set => SetAndRaiseIfChanged(ref _progress, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetAndRaiseIfChanged(ref _isLoading, value);
        }

        public event Action? LoadingCompleted;

        public LogGridModel CustomView => _gridModel;

        public NavigateToLineRequest NavigateToLineRequest { get; } = new();
        
        public GrowingLogLinesCollection Lines { get; }

        public void NavigateTo(in int logLineNumber, bool center = false)
        {
            NavigateToLineRequest.Raise(_getIndexByValue(logLineNumber) + _headerCollection.Count, center);
        }

        public void NavigateToScrollIndex(int scrollIndex)
        {
            NavigateToLineRequest.Raise(scrollIndex);
        }

        public void RestorePosition(int currentLine, int scrollPosition)
        {
            int GetCollectionIndex(int line) => line >= 0 && line < _logModelFacade.LineCount
                ? _getIndexByValue(line) + _headerCollection.Count
                : -1;
            NavigateToLineRequest.RaiseRestorePosition(GetCollectionIndex(currentLine), GetCollectionIndex(scrollPosition));
        }
        
        private async void ExcludeSelectedComponents(int componentIndex, bool excludeAllBut)
        {
            var selectedItems = SelectedItems;
            var cancellationToken = BeginSelectionOperation(excludeAllBut ? "ExcludeAllBut" : "Exclude");
            try
            {
                var values = new List<string>();
                var seen = new HashSet<string>();
                await BulkSelection.ProcessAsync(
                    selectedItems?.OfType<LineViewModel>() ?? Enumerable.Empty<LineViewModel>(), line =>
                    {
                        var value = line[componentIndex].OriginalText ?? string.Empty;
                        if (seen.Add(value))
                            values.Add(value);
                    }, cancellationToken, BulkOperations.Report);
                var indexedFieldIndex = _logModelFacade.MetaInformation.GetIndexedFieldIndexByFieldIndex(componentIndex);
                if (excludeAllBut)
                    _filterSettings.ExcludeAllExcept(indexedFieldIndex, values);
                else
                    _filterSettings.AddExclusions(indexedFieldIndex, values);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            finally
            {
                BulkOperations.Complete(cancellationToken);
            }
        }

        private CancellationToken BeginSelectionOperation(string titleKey) => BulkOperations.Begin(Strings.Get(titleKey), SelectedCount, this);

        private void CancelSelectionOperation() => BulkOperations.Cancel(this);

        private async void UpdateDocumentWhileLoading()
        {
            var cancellationToken = _loadingCancellation.Token;
            try
            {
                var delay = 10;
                IsLoading = true;
                while (!_logModelFacade.IsLoaded)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Lines.UpdateCount();
                    TotalLineCount = _logModelFacade.LineCount;
                    RefreshMarkers();
                    await Task.Delay(delay, cancellationToken);
                    if (delay < 500)
                        delay *= 2;
                }

                cancellationToken.ThrowIfCancellationRequested();
                Lines.UpdateCount();
                TotalLineCount = _logModelFacade.LineCount;
                TimeRangeFilter.Refresh();
                RefreshMarkers();
                IsLoading = false;
                LoadingCompleted?.Invoke();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }

        private async void UpdateProgress()
        {
            var cancellationToken = _loadingCancellation.Token;
            try
            {
                while (!_logModelFacade.IsLoaded)
                {
                    await Task.Delay(200, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    Progress = _logModelFacade.LoadProgress;
                }

                cancellationToken.ThrowIfCancellationRequested();
                Progress = 100;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }
        
        private async void CopySelectedItemsToClipboard()
        {
            var selectedItems = SelectedItems;
            if (selectedItems == null) return;
            var cancellationToken = BeginSelectionOperation("Copy");
            try
            {
                await BulkSelection.CopyAsync(selectedItems.Cast<object>().Select(item => item.ToString()),
                    _desktop.SetClipboardText, cancellationToken, BulkOperations.Report);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (ExternalException)
            {
            }
            finally
            {
                BulkOperations.Complete(cancellationToken);
            }
        }

        private async void UpdateFilteredCollection()
        {
            var currentItemIndex = CurrentItemIndex;
            var item = currentItemIndex > 0 && currentItemIndex < Lines.Count ? Lines[currentItemIndex] : null;
            var originalLineIndex = (item as LineViewModel)?.Index;

            var exclusionsCopy = _filterSettings.Exclusions.ToDictionary(kv 
                => kv.Key, kv => kv.Value);
            var timeRangeCopy = _filterSettings.TimeRange;
            var lineRangeCopy = _filterSettings.LineRange;
            var (headerCollection, linesCollection, getIndexByValue) 
                = await Task.Factory.StartNew(() => _lineViewModelCollectionProvider.GetLogLinesCollection(
                    _logModelFacade.Indexer,
                    exclusionsCopy,
                    timeRangeCopy,
                    lineRangeCopy));

            var newExclusionsCopy = _filterSettings.Exclusions.ToList();
            if (!exclusionsCopy.SequenceEqual(newExclusionsCopy) ||
                _filterSettings.TimeRange != timeRangeCopy ||
                _filterSettings.LineRange != lineRangeCopy)
            {
                return;
            }
            
            _getIndexByValue = getIndexByValue;
            Lines.Reset(headerCollection, linesCollection);
            TotalLineCount = _logModelFacade.LineCount;
            RefreshMarkers();
            UpdateScrollPosition();

            if (originalLineIndex is { } index)
            {
                NavigateTo(index);
            }
        }
    }
}
