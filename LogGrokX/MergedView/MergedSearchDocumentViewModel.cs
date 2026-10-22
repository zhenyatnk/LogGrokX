using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Input;
using LogGrokX.Controls;
using LogGrokX.Controls.ListControls;
using LogGrokX.Data;
using LogGrokX.Data.Virtualization;
using LogGrokX.Search;
using LogGrokX.Localization;

namespace LogGrokX.MergedView
{
    public sealed class MergedSearchDocumentViewModel : ViewModelBase, ISearchDocument
    {
        public const int MatchBucketCount = 1000;

        private readonly MergedViewModel _merged;
        private readonly object _ctsLock = new();
        private readonly DecodedPartsState _decodedParts = new();

        private CancellationTokenSource? _cts;
        private CancellationTokenSource? _threadFilterCts;
        private SearchPattern _searchPattern;
        private int[] _allMatchedIndices = Array.Empty<int>();
        private int[] _matchedIndices = Array.Empty<int>();
        private MergedViewSnapshot _snapshot = MergedViewSnapshot.Empty;
        private SearchMatches? _cachedMatches;
        private bool[] _matchBuckets = Array.Empty<bool>();
        private int? _currentItemIndex;
        private IEnumerable<object>? _selectedItems;
        private IReadOnlyList<int> _selectedMatchLines = Array.Empty<int>();
        private Regex? _highlightRegex;
        private bool _isSearching;
        private bool _isIndeterminateProgress;
        private double _searchProgress;

        public MergedSearchDocumentViewModel(MergedViewModel merged, SearchPattern searchPattern)
        {
            _merged = merged;
            _searchPattern = searchPattern;

            Lines = new GrowingLogLinesCollection(Array.Empty<ItemViewModel>(), Array.Empty<ItemViewModel>());
            NavigateToLineRequest = new NavigateToLineRequest();
            ItemActivatedCommand = new DelegateCommand(param =>
            {
                if (param is MergedLineViewModel line && line.MergedIndex >= 0)
                    NavigateToIndexRequested?.Invoke(line.MergedIndex);
            });

            _merged.Rebuilt += OnMergedRebuilt;
            _merged.CurrentThreadChanged += OnCurrentThreadChanged;
            StartSearch();
        }

        public Action<int>? NavigateToIndexRequested { get; set; }

        public GrowingLogLinesCollection Lines { get; }

        internal MergedViewSnapshot Snapshot => _snapshot;

        private sealed record SearchMatches(MergedDataSnapshot Data, SearchPattern Pattern, HashSet<int>[] BySource);

        public ViewBase CustomView => _merged.CreateSearchView();

        public ColumnSettings ColumnSettings => _merged.ColumnSettings;

        public NavigateToLineRequest NavigateToLineRequest { get; }

        public ICommand ItemActivatedCommand { get; }

        public string Title => $"{_searchPattern.Pattern} ({_matchedIndices.Length})";

        public SearchPattern SearchPattern
        {
            get => _searchPattern;
            set
            {
                if (_searchPattern.Equals(value))
                    return;

                _searchPattern = value;
                HighlightRegex = _searchPattern.IsEmpty ? null : _searchPattern.GetRegex(RegexOptions.None);
                StartSearch();
                InvokePropertyChanged();
            }
        }

        public Regex? HighlightRegex
        {
            get => _highlightRegex;
            private set => SetAndRaiseIfChanged(ref _highlightRegex, value);
        }

        public bool[] MatchBuckets
        {
            get => _matchBuckets;
            private set => SetAndRaiseIfChanged(ref _matchBuckets, value);
        }

        public string MatchCounterText =>
            _matchedIndices.Length == 0
                ? string.Empty
                : CurrentItemIndex is { } current
                    ? Strings.Format("MatchPosition", current + 1, _matchedIndices.Length)
                    : Strings.Format("MatchCount", _matchedIndices.Length);

        public int CurrentMatchLine =>
            CurrentItemIndex is { } index && index >= 0 && index < _matchedIndices.Length
                ? MapToFull(_matchedIndices[index])
                : -1;

        public IEnumerable<object>? SelectedItems
        {
            get => _selectedItems;
            set
            {
                _selectedItems = value;
                SelectedMatchLines =
                    value?.OfType<MergedLineViewModel>().Select(line => line.MergedIndex).ToArray() ?? Array.Empty<int>();
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

        public int? CurrentItemIndex
        {
            get => _currentItemIndex;
            set
            {
                if (_currentItemIndex == value)
                    return;

                _currentItemIndex = value;
                InvokePropertyChanged();
                InvokePropertyChanged(nameof(MatchCounterText));
                InvokePropertyChanged(nameof(CurrentMatchLine));
            }
        }

        public bool IsSearching
        {
            get => _isSearching;
            private set => SetAndRaiseIfChanged(ref _isSearching, value);
        }

        public bool IsIndeterminateProgress
        {
            get => _isIndeterminateProgress;
            private set => SetAndRaiseIfChanged(ref _isIndeterminateProgress, value);
        }

        public double SearchProgress
        {
            get => _searchProgress;
            private set => SetAndRaiseIfChanged(ref _searchProgress, value);
        }

        public void FindNext() => MoveMatch(1);

        public void FindPrevious() => MoveMatch(-1);

        public void FindNext(int anchorOriginalLine) => MoveMatch(1);

        public void FindPrevious(int anchorOriginalLine) => MoveMatch(-1);

        public void Dispose()
        {
            _merged.Rebuilt -= OnMergedRebuilt;
            _merged.CurrentThreadChanged -= OnCurrentThreadChanged;

            lock (_ctsLock)
            {
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = null;
                _threadFilterCts?.Cancel();
                _threadFilterCts?.Dispose();
                _threadFilterCts = null;
            }
        }

        private void OnMergedRebuilt()
        {
            if (!_searchPattern.IsEmpty)
                StartSearch();
        }

        private void StartSearch()
        {
            var newCts = new CancellationTokenSource();
            lock (_ctsLock)
            {
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = newCts;
                _threadFilterCts?.Cancel();
            }

            HighlightRegex = _searchPattern.IsEmpty ? null : _searchPattern.GetRegex(RegexOptions.None);

            if (_searchPattern.IsEmpty)
            {
                _allMatchedIndices = Array.Empty<int>();
                _matchedIndices = Array.Empty<int>();
                _snapshot = MergedViewSnapshot.Empty;
                _cachedMatches = null;
                Lines.Reset(Array.Empty<ItemViewModel>(), Array.Empty<ItemViewModel>());
                MatchBuckets = Array.Empty<bool>();
                CurrentItemIndex = null;
                IsSearching = false;
                IsIndeterminateProgress = false;
                SearchProgress = 0;
                InvokePropertyChanged(nameof(Title));
                InvokePropertyChanged(nameof(MatchCounterText));
                InvokePropertyChanged(nameof(CurrentMatchLine));
                return;
            }

            var snapshot = _merged.GetSearchSnapshot();
            var regex = _searchPattern.GetRegex(RegexOptions.Compiled);
            var token = newCts.Token;

            if (!ReferenceEquals(snapshot, _snapshot))
            {
                _allMatchedIndices = Array.Empty<int>();
                _matchedIndices = Array.Empty<int>();
                Lines.Reset(Array.Empty<ItemViewModel>(), Array.Empty<ItemViewModel>());
                SelectedItems = null;
                MatchBuckets = Array.Empty<bool>();
            }
            _snapshot = snapshot;
            IsSearching = true;
            IsIndeterminateProgress = true;
            SearchProgress = 0;
            CurrentItemIndex = null;

            _ = RunSearchAsync(snapshot, _searchPattern, regex, token);
        }

        private async Task RunSearchAsync(MergedViewSnapshot snapshot, SearchPattern pattern, Regex regex,
            CancellationToken token)
        {
            var cached = _cachedMatches;
            try
            {
                var (matched, matches) = await Task.Run(() =>
                {
                    var matches = cached;
                    if (matches == null || !ReferenceEquals(matches.Data, snapshot.Data) || !matches.Pattern.Equals(pattern))
                    {
                        var sourceMatches = new HashSet<int>[snapshot.Sources.Length];
                        for (var i = 0; i < snapshot.Sources.Length; i++)
                        {
                            token.ThrowIfCancellationRequested();
                            sourceMatches[i] = new HashSet<int>(snapshot.Sources[i].Document.FindMatchingLines(regex, token));
                        }
                        matches = new SearchMatches(snapshot.Data, pattern, sourceMatches);
                    }

                    var result = new List<int>();
                    for (var i = 0; i < snapshot.Lines.Length; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        var line = snapshot.Lines[i];
                        if (matches.BySource[line.SourceIndex].Contains(line.LineNumber))
                            result.Add(i);
                    }

                    return (result, matches);
                }, token);

                if (token.IsCancellationRequested)
                    return;

                _cachedMatches = matches;
                _allMatchedIndices = matched.ToArray();
                await ApplyThreadFilterAsync(_allMatchedIndices);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                if (!token.IsCancellationRequested)
                {
                    IsSearching = false;
                    IsIndeterminateProgress = false;
                    SearchProgress = 100;
                }
            }
        }

        private void OnCurrentThreadChanged()
        {
            if (_searchPattern.IsEmpty || IsSearching)
                return;

            _ = ApplyThreadFilterAsync(_allMatchedIndices);
        }

        private async Task ApplyThreadFilterAsync(int[] allMatched)
        {
            var cts = new CancellationTokenSource();
            lock (_ctsLock)
            {
                _threadFilterCts?.Cancel();
                _threadFilterCts?.Dispose();
                _threadFilterCts = cts;
            }

            var token = cts.Token;
            var buffer = _snapshot.Lines;
            var sources = _snapshot.Sources;
            var thread = _merged.CurrentThread;
            var threadFieldIndex = _merged.ThreadFieldIndex;

            try
            {
                var filtered = thread == null
                    ? allMatched
                    : await Task.Run(() => FilterByThread(allMatched, buffer, sources, thread, threadFieldIndex, token),
                        token);

                if (token.IsCancellationRequested || !ReferenceEquals(allMatched, _allMatchedIndices))
                    return;

                if (!Nullable.Equals(thread, _merged.CurrentThread))
                {
                    _ = ApplyThreadFilterAsync(allMatched);
                    return;
                }

                _matchedIndices = filtered;
                CurrentItemIndex = null;
                RebuildResults();
            }
            catch (OperationCanceledException)
            {
            }
        }

        internal static int[] FilterByThread(int[] matched, MergedLineRef[] buffer, MergedDocumentItem[] sources,
            MergedThread? thread, int threadFieldIndex, CancellationToken token)
        {
            if (thread is not { } current || threadFieldIndex < 0)
                return matched;

            var result = new List<int>();
            foreach (var index in matched)
            {
                token.ThrowIfCancellationRequested();
                var line = buffer[index];
                var source = sources[line.SourceIndex];
                if (ReferenceEquals(source, current.Source)
                    && source.IsLineInThread(line.LineNumber, threadFieldIndex, current.Thread))
                    result.Add(index);
            }

            return result.ToArray();
        }

        private void RebuildResults()
        {
            var provider = new ListItemProvider<int>(_matchedIndices);
            var list = new VirtualList<int, ItemViewModel>(provider, CreateResult);
            Lines.Reset(Array.Empty<ItemViewModel>(), list);
            MatchBuckets = BuildBuckets();
            InvokePropertyChanged(nameof(Title));
            InvokePropertyChanged(nameof(MatchCounterText));
            InvokePropertyChanged(nameof(CurrentMatchLine));
        }

        private ItemViewModel CreateResult(int mergedIndex)
        {
            var line = _snapshot.Lines[mergedIndex];
            return new MergedLineViewModel(_snapshot.Sources[line.SourceIndex], line.LineNumber, line.Ticks,
                MapToFull(mergedIndex), _decodedParts);
        }

        private int MapToFull(int visibleIndex) =>
            _snapshot.FullIndexMap is { } map && visibleIndex >= 0 && visibleIndex < map.Length
                ? map[visibleIndex]
                : visibleIndex;

        private bool[] BuildBuckets()
        {
            var buckets = new bool[MatchBucketCount];
            var total = _merged.TotalLineCount;
            if (total <= 0)
                return buckets;

            foreach (var index in _matchedIndices)
            {
                var bucket = (int)((long)MapToFull(index) * MatchBucketCount / total);
                if (bucket < 0) bucket = 0;
                if (bucket >= MatchBucketCount) bucket = MatchBucketCount - 1;
                buckets[bucket] = true;
            }

            return buckets;
        }

        private void MoveMatch(int direction)
        {
            var count = _matchedIndices.Length;
            if (count == 0)
                return;

            var current = CurrentItemIndex ?? (direction > 0 ? -1 : 0);
            var next = current + direction;
            if (next >= count) next = 0;
            if (next < 0) next = count - 1;

            SelectMatch(next);
        }

        private void SelectMatch(int resultIndex)
        {
            if (resultIndex < 0 || resultIndex >= _matchedIndices.Length)
                return;

            CurrentItemIndex = resultIndex;
            NavigateToLineRequest.Raise(resultIndex);
            NavigateToIndexRequested?.Invoke(MapToFull(_matchedIndices[resultIndex]));
        }
    }
}
