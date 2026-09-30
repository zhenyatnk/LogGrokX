using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Input;
using LogGrokX.Controls;
using LogGrokX.Controls.GridView;
using LogGrokX.Controls.ListControls;
using LogGrokX.Controls.TextRender;
using LogGrokX.Data;
using LogGrokX.Data.Index;
using LogGrokX.Data.Search;
using LogGrokX.Data.Virtualization;
using LogGrokX.Filter;

namespace LogGrokX.Search
{
    public class SearchDocumentViewModel : ViewModelBase, ISearchDocument
    {
        public const int MatchBucketCount = 1000;

        private readonly GridViewFactory _viewFactory;
        private SearchPattern _searchPattern;
        private bool[] _matchBuckets = Array.Empty<bool>();
        
        public Action<int>? NavigateToIndexRequested { get; set; }
        private  bool _isIndeterminateProgress;

        private readonly object _cancellationTokenSourceLock = new();
        private CancellationTokenSource? _currentSearchCancellationTokenSource;
        private bool _isSearching;
        private double _progress;
        private Regex? _highlightRegex;
        private readonly FilterSettings _filterSettings;
     
        private SubIndexer? _currentSearchIndexer;
        private int? _currentItemIndex;
        private IEnumerable<object>? _selectedItems;
        private IReadOnlyList<int> _selectedMatchLines = Array.Empty<int>();
        private readonly LogModelFacade _logModelFacade;
        private readonly TimeIndex _timeIndex;
        private SearchLineIndex? _currentSearchLineIndex;
        private readonly Selection _markedLines;
        private readonly TransformationPerformer _transformationPerformer;

        public SearchDocumentViewModel(LogModelFacade logModelFacade,
            FilterSettings filterSettings,
            GridViewFactory viewFactory,
            SearchPattern searchPattern,
            Selection markedLines,
            ColumnSettings columnSettings,
            TransformationPerformer transformationPerformer,
            TimeIndex timeIndex,
            TextViewSharedFoldingState foldingState)
        {
            
            _viewFactory = viewFactory;
            FoldingState = foldingState;

            _logModelFacade = logModelFacade;
            _timeIndex = timeIndex;

            _filterSettings = filterSettings;
            _filterSettings.ExclusionsChanged += UpdateLines;
            _filterSettings.TimeRangeChanged += StartSearch;
            _filterSettings.LineRangeChanged += StartSearch;

            _markedLines = markedLines;
            _transformationPerformer = transformationPerformer;

            ColumnSettings = columnSettings;

            SearchPattern = searchPattern;
            ItemActivatedCommand = new DelegateCommand(
                param =>
                {
                    if (param is not BaseLogLineViewModel itemViewModel)
                        return;

                    var resultIndex = _currentSearchLineIndex?.GetIndexByOriginalIndex(itemViewModel.Index) ?? -1;
                    if (resultIndex >= 0 && resultIndex < Lines.Count
                        && ReferenceEquals(Lines[resultIndex], itemViewModel))
                        CurrentItemIndex = resultIndex;

                    NavigateToIndexRequested?.Invoke(itemViewModel.Index);
                });
        }

        public event Action? MatchPositionChanged;

        public bool[] MatchBuckets
        {
            get => _matchBuckets;
            private set
            {
                _matchBuckets = value;
                InvokePropertyChanged();
            }
        }

        public string MatchCounterText
        {
            get
            {
                var count = Lines.Count;
                if (count == 0)
                    return string.Empty;

                return CurrentItemIndex is { } current
                    ? $"{current + 1} of {count}"
                    : $"{count} matches";
            }
        }

        public void FindNext() => MoveMatch(1, -1);

        public void FindPrevious() => MoveMatch(-1, -1);

        public void FindNext(int anchorOriginalLine) => MoveMatch(1, anchorOriginalLine);

        public void FindPrevious(int anchorOriginalLine) => MoveMatch(-1, anchorOriginalLine);

        private void MoveMatch(int direction, int anchorOriginalLine)
        {
            var count = Lines.Count;
            if (count == 0)
                return;

            if (anchorOriginalLine >= 0 && _currentSearchLineIndex != null)
            {
                var index = direction > 0
                    ? _currentSearchLineIndex.GetIndexByOriginalIndex(anchorOriginalLine + 1)
                    : _currentSearchLineIndex.GetIndexByOriginalIndex(anchorOriginalLine) - 1;

                if (index < 0) index = count - 1;
                if (index >= count) index = 0;

                SelectMatch(index);
                return;
            }

            var current = CurrentItemIndex ?? (direction > 0 ? -1 : 0);
            var next = current + direction;
            if (next >= count) next = 0;
            if (next < 0) next = count - 1;

            SelectMatch(next);
        }

        private void SelectMatch(int resultIndex)
        {
            if (resultIndex < 0 || resultIndex >= Lines.Count)
                return;

            CurrentItemIndex = resultIndex;
            NavigateToLineRequest.Raise(resultIndex);

            if (Lines[resultIndex] is LineViewModel lineViewModel)
                NavigateToIndexRequested?.Invoke(lineViewModel.Index);
        }

        public ColumnSettings ColumnSettings { get; }
        
        public TextViewSharedFoldingState FoldingState { get; }
        
        public string Title => $"{SearchPattern.Pattern} ({Lines.Count})";

        public NavigateToLineRequest NavigateToLineRequest { get; } = new();

        public bool IsIndeterminateProgress
        {
            get => _isIndeterminateProgress;
            set => SetAndRaiseIfChanged(ref _isIndeterminateProgress, value); 
        }

        public bool IsSearching         
        {
            get => _isSearching;
            set => SetAndRaiseIfChanged(ref _isSearching, value); 
        }

        public double SearchProgress 
        {
            get => _progress;
            set => SetAndRaiseIfChanged(ref _progress, value); 
        }

        public Regex? HighlightRegex
        {
            get => _highlightRegex;
            set => SetAndRaiseIfChanged(ref _highlightRegex, value);
        }

        public GrowingLogLinesCollection Lines { get; } = 
            new(new List<ItemViewModel>(), new List<ItemViewModel>());
        
        public ViewBase CustomView => _viewFactory.CreateView(ColumnSettings.ColumnWidths);

        public SearchPattern SearchPattern
        {
            get => _searchPattern;
            set
            {
                if (_searchPattern.Equals(value)) return;
                _searchPattern = value;
                HighlightRegex = _searchPattern.GetRegex(RegexOptions.None);
                StartSearch();
            }
        }

        public int? CurrentItemIndex
        {
            get => _currentItemIndex;
            set
            {
                var normalized = value < 0 ? null : value;
                if (_currentItemIndex == normalized) return;

                _currentItemIndex = normalized;
                InvokePropertyChanged();
                InvokePropertyChanged(nameof(MatchCounterText));
                InvokePropertyChanged(nameof(CurrentMatchLine));
                MatchPositionChanged?.Invoke();
            }
        }

        public IEnumerable<object>? SelectedItems
        {
            get => _selectedItems;
            set
            {
                _selectedItems = value;
                SelectedMatchLines =
                    value?.OfType<LineViewModel>().Select(line => line.Index).ToArray() ?? Array.Empty<int>();
            }
        }

        public IReadOnlyList<int> SelectedMatchLines
        {
            get => _selectedMatchLines;
            private set
            {
                if (_selectedMatchLines.SequenceEqual(value)) return;
                _selectedMatchLines = value;
                InvokePropertyChanged();
            }
        }

        public int CurrentMatchLine
        {
            get
            {
                if (CurrentItemIndex is not { } index || index < 0 || index >= Lines.Count)
                    return -1;

                return Lines[index] is LineViewModel line ? line.Index : -1;
            }
        }

        public ICommand ItemActivatedCommand
        {
            get;
            private set;
        }
        
        public void Dispose()
        {
            _filterSettings.ExclusionsChanged -= UpdateLines;
            _filterSettings.TimeRangeChanged -= StartSearch;
            _filterSettings.LineRangeChanged -= StartSearch;

            lock (_cancellationTokenSourceLock)
            {
                _currentSearchCancellationTokenSource?.Cancel();
                _currentSearchCancellationTokenSource = null;
            }
        }
        
        private void StartSearch()
        {
            lock (_cancellationTokenSourceLock)
            {
                if (_currentSearchCancellationTokenSource != null &&
                    _currentSearchCancellationTokenSource.IsCancellationRequested)
                    return;
                
                _currentSearchCancellationTokenSource?.Cancel();
            }

            var newCancellationTokenSource = new CancellationTokenSource();

            SearchProgress = 0;
            IsIndeterminateProgress = true;
            CurrentItemIndex = null;
            
            (int StartLine, int EndLine)? lineRange = null;
            if (_filterSettings.TimeRange is { } timeRange)
                lineRange = _timeIndex.FindLineRange(timeRange.From, timeRange.To);
            else if (_filterSettings.LineRange is { } fallbackLineRange)
                lineRange = fallbackLineRange;

            var (progress, searchIndexer, searchLineIndex) = Data.Search.Search.CreateSearchIndex(
                _logModelFacade,
                _searchPattern.GetRegex(RegexOptions.Compiled),
                newCancellationTokenSource.Token,
                lineRange);

            _currentSearchIndexer = searchIndexer;
            _currentSearchLineIndex = searchLineIndex;

            lock (_cancellationTokenSourceLock)
            {
                _currentSearchCancellationTokenSource = newCancellationTokenSource;
            }

            UpdateLines();
            UpdateDocumentWhileLoading(progress, newCancellationTokenSource.Token);
            InvokePropertyChanged(nameof(Title));
        }

        private IReadOnlyList<ItemViewModel> GetLineCollection(
            SubIndexer searchIndexer,          // components -> searchResultLineNumber
            SearchLineIndex searchLineIndex, // searchResultLineNumber -> originalLogLineNumber mapping

            IReadOnlyDictionary<int, IEnumerable<string>> exclusions)
        {
            var lineProvider = _logModelFacade.LineProvider; // originalLogLineNumber -> string 
            var lineParser = _logModelFacade.LineParser;

            var filteredSearchResultLineNumbersProvider = searchIndexer.GetIndexedLinesProvider(exclusions); // collection of filtered searchResultLineNumbers
            // to map from search result line numbers to original line numbers
            var filteredOriginalLineNumbersProvider =
                new ItemProviderMapper<int>(filteredSearchResultLineNumbersProvider, searchLineIndex);

            var filteredLineWithOriginalLineNumber =
                new ItemProviderMapper<(int originalLogLineNumber, string str)>(
                    filteredOriginalLineNumbersProvider, lineProvider);

            return new VirtualList<(int originalLogLineNumber, string str), ItemViewModel>(
                    filteredLineWithOriginalLineNumber, indexAndString =>
                        new LineViewModel(indexAndString.originalLogLineNumber, indexAndString.str, lineParser, 
                            _markedLines, _transformationPerformer));
        } 
        
        private DateTime _isSearchingShowStartTime = DateTime.Now;
        private bool _localIsSearching;
        
        private async void SetIsSearching(bool isSearching)
        {
            var minProgressShowTimeMs = 500;
            var delayBeforeShowProgress = 500;
            
            if (isSearching == _localIsSearching)
                return;
                
            _localIsSearching = isSearching;
            Trace.TraceInformation($"localIsSearching = {_localIsSearching}");

            if (!IsSearching)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(delayBeforeShowProgress));
                if (!_localIsSearching) return;
                IsSearching = true;
                _isSearchingShowStartTime = DateTime.Now;
            }
            else
            {
                var showTime = DateTime.Now - _isSearchingShowStartTime;
                var minShowTime = minProgressShowTimeMs;
                if (DateTime.Now -  _isSearchingShowStartTime < TimeSpan.FromMilliseconds(minShowTime))
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(minShowTime - showTime.TotalMilliseconds));
                } 
                    
                IsSearching = false;
            }
        }
        
        private async void UpdateDocumentWhileLoading(Data.Search.Search.Progress progress,
            CancellationToken cancellationToken)
        {
            try
            {
                SetIsSearching(true);

                var delay = 10;
                while (!progress.IsFinished)
                {
                    var count = Lines?.Count;
                    Lines?.UpdateCount();
                    if (count != Lines?.Count)
                    {
                        InvokePropertyChanged(nameof(Title));
                        InvokePropertyChanged(nameof(MatchCounterText));
                    }

                    IsIndeterminateProgress = false;
                    SearchProgress = progress.Value * 100.0;

                    await Task.WhenAny(Task.Delay(delay, cancellationToken), progress.Completion);
                    if (cancellationToken.IsCancellationRequested)
                        return;
                    if (delay < 150)
                        delay *= 2;
                }

                if (cancellationToken.IsCancellationRequested)
                    return;

                Lines?.UpdateCount();
                SearchProgress = progress.Value * 100.0;
                SetIsSearching(false);
                MatchBuckets = BuildBuckets();
                InvokePropertyChanged(nameof(Title));
                InvokePropertyChanged(nameof(MatchCounterText));
            }
            catch (OperationCanceledException)
            {
            }
        }

        private void UpdateLines()
        {
            if (_currentSearchIndexer == null || _currentSearchLineIndex == null)
                return;
            
            var currentItemIndex = CurrentItemIndex;
            var originalLineIndex = currentItemIndex switch
            {
                { } ind => (Lines[ind] as LineViewModel)?.Index,
                _ => null
            };

            var foundLines = GetLineCollection(
                _currentSearchIndexer, _currentSearchLineIndex, _filterSettings.Exclusions);
            
            Lines.Reset(new List<ItemViewModel>(), foundLines);
            MatchBuckets = BuildBuckets();
            InvokePropertyChanged(nameof(MatchCounterText));
            InvokePropertyChanged(nameof(CurrentMatchLine));

            if (originalLineIndex is not { } index) return;
            
            var nearestIndex = _currentSearchLineIndex.GetIndexByOriginalIndex(index);
            if (nearestIndex < 0 || nearestIndex >= Lines.Count)
                CurrentItemIndex = null;
            else
                NavigateToLineRequest.Raise(_currentSearchLineIndex.GetIndexByOriginalIndex(index));
        }

        private bool[] BuildBuckets()
        {
            var buckets = new bool[MatchBucketCount];
            var totalLines = _logModelFacade.LineCount;
            if (_currentSearchIndexer == null || _currentSearchLineIndex == null
                || totalLines <= 0 || _currentSearchLineIndex.Count == 0)
                return buckets;

            var filteredSearchResults =
                _currentSearchIndexer.GetIndexedLinesProvider(_filterSettings.Exclusions);
            var originalLineNumbers = new ItemProviderMapper<int>(filteredSearchResults, _currentSearchLineIndex);
            var matchCount = originalLineNumbers.Count;
            if (matchCount == 0)
                return buckets;

            const int chunkSize = 8192;
            var buffer = ArrayPool<int>.Shared.Rent(Math.Min(chunkSize, matchCount));
            try
            {
                for (var start = 0; start < matchCount; start += chunkSize)
                {
                    var count = Math.Min(chunkSize, matchCount - start);
                    originalLineNumbers.Fetch(start, buffer.AsSpan(0, count));
                    for (var i = 0; i < count; i++)
                    {
                        var bucket = (int)((long)buffer[i] * MatchBucketCount / totalLines);
                        if (bucket < 0) bucket = 0;
                        if (bucket >= MatchBucketCount) bucket = MatchBucketCount - 1;
                        buckets[bucket] = true;
                    }
                }
            }
            finally
            {
                ArrayPool<int>.Shared.Return(buffer);
            }

            return buckets;
        }
    }
}