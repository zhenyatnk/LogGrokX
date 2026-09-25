using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;

namespace LogGrokX.Data.Index
{
   public class CountIndex<TIndex> where TIndex : IIndex<int>
    {
        public const int Granularity = 16384;
        private ImmutableList<List<(IndexKeyNum, int)>> _counts = ImmutableList<List<(IndexKeyNum, int)>>.Empty;
        private readonly IDictionary<IndexKeyNum,TIndex> _indices;
        private bool _isFinished = false;

        // The tail snapshot used to be rebuilt on every single read (and it is read
        // from the UI for every filter item). Cache it and invalidate by version,
        // which only changes when new lines are actually indexed.
        private int _version;
        private int _cachedVersion = -1;
        private IReadOnlyList<List<(IndexKeyNum, int)>>? _cachedCounts;
        private readonly object _cacheLocker = new();

        public IReadOnlyList<List<(IndexKeyNum, int)>> Counts
        {
            get
            {
                if (_isFinished)
                    return _counts;

                var version = Volatile.Read(ref _version);
                lock (_cacheLocker)
                {
                    if (_cachedCounts != null && _cachedVersion == version)
                        return _cachedCounts;

                    var counts = _counts.Add(MakeCountsSnapshot());
                    if (_isFinished)
                        return _counts;

                    _cachedCounts = counts;
                    _cachedVersion = version;
                    return counts;
                }
            }
        }

        public CountIndex(IDictionary<IndexKeyNum, TIndex> indices)
        {
            _indices = indices;
        }

        public void Add(int currentIndex, IDictionary<IndexKeyNum, TIndex> indices)
        {
            Interlocked.Increment(ref _version);
            if (currentIndex % Granularity == 0 && currentIndex != 0)
                UpdateCountsSnapshot();
        }

        public void Finish(IDictionary<IndexKeyNum, TIndex> indices)
        {
            UpdateCountsSnapshot();
            _isFinished = true;
            lock (_cacheLocker)
            {
                _cachedCounts = null;
                _cachedVersion = -1;
            }
        }

        private void UpdateCountsSnapshot()
        {
            _counts = _counts.Add(MakeCountsSnapshot());
            lock (_cacheLocker)
            {
                _cachedCounts = null;
                _cachedVersion = -1;
            }
        }

        private List<(IndexKeyNum, int)> MakeCountsSnapshot()
        {
            var snapshotList = new List<(IndexKeyNum, int)>(_indices.Count);

#pragma warning disable CS8619
            foreach (var (key, value) in _indices)
#pragma warning restore CS8619
            {
                snapshotList.Add((key, value.Count));
            }

            return snapshotList;
        }
    }
}
