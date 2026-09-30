using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Input;
using LogGrokX.Controls;
using LogGrokX.Controls.GridView;
using LogGrokX.Controls.ListControls;
using LogGrokX.Controls.TextRender;
using LogGrokX.Data;
using LogGrokX.Filter;

namespace LogGrokX
{
    public class LogViewModel : ViewModelBase, IDisposable
    {
        private readonly GridViewFactory _viewFactory;
        private double _progress;
        private readonly LogModelFacade _logModelFacade;
        private bool _isLoading;
        private IEnumerable? _selectedItems;
        private readonly LineViewModelCollectionProvider _lineViewModelCollectionProvider;
        private Regex? _highlightRegex;
        private Func<int, int> _getIndexByValue;
        private readonly FilterSettings _filterSettings;
        private readonly Selection _markedLines;
        private readonly TimelinePlacementService _timelinePlacementService;
        private readonly ThreadGroupingService _threadGroupingService;
        private readonly int _threadFieldIndex;
        private int _currentItemIndex;
        private readonly IReadOnlyList<ItemViewModel> _headerCollection;

        public LogViewModel(
            LogModelFacade logModelFacade,
            LineViewModelCollectionProvider lineViewModelCollectionProvider,
            GridViewFactory viewFactory,
            FilterSettings filterSettings,
            ColumnSettings columnSettings,
            TimeRangeFilterViewModel timeRangeFilter,
            Selection markedLines,
            TimelinePlacementService timelinePlacementService,
            ThreadGroupingService threadGroupingService,
            TextViewSharedFoldingState foldingState)
        {
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
                    (int componentIndex) => {
                        _filterSettings.AddExclusions(
                            logModelFacade.MetaInformation.GetIndexedFieldIndexByFieldIndex(componentIndex),
                            GetComponentsInSelectedLines(componentIndex));
                    });    
            
            ExcludeAllButCommand = DelegateCommand.Create(
                (int componentIndex) =>
                {
                    
                    _filterSettings.ExcludeAllExcept(
                        logModelFacade.MetaInformation.GetIndexedFieldIndexByFieldIndex(componentIndex),
                        GetComponentsInSelectedLines(componentIndex));
                });
            
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
            
            _viewFactory = viewFactory;
            ColumnSettings = columnSettings;
            RefreshActiveFilters();
            TotalLineCount = _logModelFacade.LineCount;
            RefreshMarkers();
            UpdateDocumentWhileLoading();
            UpdateProgress();
        }

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

        public Dock TimelineDock =>
            _timelinePlacementService.IsAtTop ? Dock.Top : Dock.Bottom;

        private void OnTimelinePlacementChanged() => InvokePropertyChanged(nameof(TimelineDock));

        public bool GroupByThread => _threadGroupingService.IsEnabled;

        public int ThreadFieldIndex => _threadFieldIndex;

        private void OnThreadGroupingChanged() => InvokePropertyChanged(nameof(GroupByThread));

        public void Dispose()
        {
            _timelinePlacementService.Changed -= OnTimelinePlacementChanged;
            _threadGroupingService.Changed -= OnThreadGroupingChanged;
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
            set => SetAndRaiseIfChanged(ref _currentItemIndex, value);
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

        public ViewBase CustomView => _viewFactory.CreateView(ColumnSettings.ColumnWidths);

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
        
        private IEnumerable<string> GetComponentsInSelectedLines(int componentIndex)
        {
            var lineViewModels = SelectedItems?.OfType<LineViewModel>() ?? Enumerable.Empty<LineViewModel>();
            return lineViewModels.Select(line => line[componentIndex].OriginalText ?? string.Empty).Distinct();
        }

        private async void UpdateDocumentWhileLoading()
        {
            var delay = 10;
            IsLoading = true;
            while (!_logModelFacade.IsLoaded)
            {
                Lines.UpdateCount();
                TotalLineCount = _logModelFacade.LineCount;
                RefreshMarkers();
                await Task.Delay(delay);
                if (delay < 500)
                    delay *= 2;
            }

            Lines.UpdateCount();
            TotalLineCount = _logModelFacade.LineCount;
            TimeRangeFilter.Refresh();
            RefreshMarkers();
            IsLoading = false;
        }

        private async void UpdateProgress()
        {
            while (!_logModelFacade.IsLoaded)
            {
                await Task.Delay(200);
                Progress = _logModelFacade.LoadProgress;
            }

            Progress = 100;
        }
        
        private void CopySelectedItemsToClipboard()
        {
            if (SelectedItems == null) return;
            var orderedLines =
                SelectedItems.OfType<LogHeaderViewModel>().Select(h => h.ToString())
                    .Concat(SelectedItems.OfType<LineViewModel>().OrderBy(ln => ln.Index).Select(ln => ln.ToString()));
            
            var  text = new StringBuilder();
            foreach (var line in orderedLines)
            {
                _ = text.Append(line);
                _ = text.Append("\r\n");
            }
            _ = text.Replace("\0", string.Empty);

            TextCopy.ClipboardService.SetText(text.ToString());
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
                    _filterSettings.Exclusions,
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