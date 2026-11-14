using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace LogGrokX.Controls
{
    public class Selection : IEnumerable<int>
    {
        internal readonly record struct SelectionRange(int Start, int End) : IEnumerable<int>
        {
            public IEnumerator<int> GetEnumerator()
            {
                for (var value = (long)Start; value <= End; value++)
                    yield return (int)value;
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        public readonly struct SelectionSnapshot
        {
            private readonly SelectionRange[]? _ranges;

            private SelectionSnapshot(SelectionRange[] ranges) => _ranges = ranges;

            public static SelectionSnapshot FromRanges(IEnumerable<(int Start, int End)> ranges) =>
                new(Normalize(ranges.Select(range => new SelectionRange(range.Start, range.End))));

            internal SelectionRange[] Ranges => _ranges ?? [];

            internal static SelectionSnapshot Create(SelectionRange[] ranges) => new(ranges);
        }

        private readonly List<WeakReference<Action>> _weakHandlers = new();
        private List<SelectionRange> _ranges = new();
        private int _bulkUpdateDepth;
        private bool _changedDuringBulkUpdate;

        public (int min, int max)? Bounds => _ranges.Count == 0
            ? null
            : (_ranges[0].Start, _ranges[^1].End);

        public long Count => _ranges.Sum(range => (long)range.End - range.Start + 1);

        public void SubscribeWeak(Action handler) => _weakHandlers.Add(new WeakReference<Action>(handler));

        public IDisposable BeginBulkUpdate()
        {
            _bulkUpdateDepth++;
            return new BulkUpdateScope(this);
        }

        private void RaiseChanged()
        {
            if (_bulkUpdateDepth > 0)
            {
                _changedDuringBulkUpdate = true;
                return;
            }

            Changed?.Invoke();

            for (var i = _weakHandlers.Count - 1; i >= 0; i--)
            {
                if (_weakHandlers[i].TryGetTarget(out var handler))
                    handler();
                else
                    _weakHandlers.RemoveAt(i);
            }
        }

        public void Add(int index) => AddRange(index, index);

        public void SetRange(int start, int end)
        {
            if (start > end)
                (start, end) = (end, start);
            if (_ranges is [{ Start: var currentStart, End: var currentEnd }] &&
                currentStart == start && currentEnd == end)
                return;
            _ranges = [new SelectionRange(start, end)];
            RaiseChanged();
        }

        public void AddRangeToValue(int selectedValue)
        {
            if (Bounds is not { min: var min, max: var max })
            {
                Add(selectedValue);
                return;
            }

            var valueFrom = selectedValue > max ? max : min;
            AddRange(Math.Min(valueFrom, selectedValue), Math.Max(valueFrom, selectedValue));
        }

        private void AddRange(int start, int end)
        {
            var merged = new List<SelectionRange>();
            var inserted = false;
            foreach (var current in _ranges)
            {
                if (current.End < (long)start - 1)
                {
                    merged.Add(current);
                }
                else if (current.Start > (long)end + 1)
                {
                    if (!inserted)
                    {
                        merged.Add(new SelectionRange(start, end));
                        inserted = true;
                    }
                    merged.Add(current);
                }
                else
                {
                    start = Math.Min(start, current.Start);
                    end = Math.Max(end, current.End);
                }
            }
            if (!inserted)
                merged.Add(new SelectionRange(start, end));

            if (merged.SequenceEqual(_ranges))
                return;
            _ranges = merged;
            RaiseChanged();
        }

        public void Clear()
        {
            if (_ranges.Count == 0)
                return;
            _ranges.Clear();
            RaiseChanged();
        }

        public void Remove(in int index)
        {
            for (var i = 0; i < _ranges.Count; i++)
            {
                var range = _ranges[i];
                if (index < range.Start)
                    return;
                if (index > range.End)
                    continue;

                _ranges.RemoveAt(i);
                if (range.Start < index)
                    _ranges.Insert(i++, new SelectionRange(range.Start, index - 1));
                if (index < range.End)
                    _ranges.Insert(i, new SelectionRange(index + 1, range.End));
                RaiseChanged();
                return;
            }
        }

        public void Set(int index)
        {
            if (_ranges is [{ Start: var start, End: var end }] && start == index && end == index)
                return;
            _ranges = [new SelectionRange(index, index)];
            RaiseChanged();
        }

        public bool Contains(int index)
        {
            var low = 0;
            var high = _ranges.Count - 1;
            while (low <= high)
            {
                var middle = low + (high - low) / 2;
                var range = _ranges[middle];
                if (index < range.Start)
                    high = middle - 1;
                else if (index > range.End)
                    low = middle + 1;
                else
                    return true;
            }
            return false;
        }

        public SelectionSnapshot CaptureSnapshot() => SelectionSnapshot.Create(_ranges.ToArray());

        public void Restore(SelectionSnapshot original, SelectionSnapshot affected)
        {
            if (affected.Ranges.Length == 0)
                return;
            var ranges = Subtract(_ranges, affected.Ranges);
            ranges.AddRange(Intersect(original.Ranges, affected.Ranges));
            var normalized = Normalize(ranges).ToList();
            if (normalized.SequenceEqual(_ranges))
                return;
            _ranges = normalized;
            RaiseChanged();
        }

        private static List<SelectionRange> Subtract(IReadOnlyList<SelectionRange> source,
            IReadOnlyList<SelectionRange> excluded)
        {
            var result = new List<SelectionRange>();
            var excludedIndex = 0;
            foreach (var range in source)
            {
                var start = (long)range.Start;
                while (excludedIndex < excluded.Count && excluded[excludedIndex].End < start)
                    excludedIndex++;
                var currentIndex = excludedIndex;
                while (currentIndex < excluded.Count && excluded[currentIndex].Start <= range.End && start <= range.End)
                {
                    var current = excluded[currentIndex++];
                    if (current.Start > start)
                        result.Add(new SelectionRange((int)start, (int)Math.Min(range.End, (long)current.Start - 1)));
                    start = Math.Max(start, (long)current.End + 1);
                }
                if (start <= range.End)
                    result.Add(new SelectionRange((int)start, range.End));
            }
            return result;
        }

        private static List<SelectionRange> Intersect(IReadOnlyList<SelectionRange> first,
            IReadOnlyList<SelectionRange> second)
        {
            var result = new List<SelectionRange>();
            var firstIndex = 0;
            var secondIndex = 0;
            while (firstIndex < first.Count && secondIndex < second.Count)
            {
                var start = Math.Max(first[firstIndex].Start, second[secondIndex].Start);
                var end = Math.Min(first[firstIndex].End, second[secondIndex].End);
                if (start <= end)
                    result.Add(new SelectionRange(start, end));
                if (first[firstIndex].End < second[secondIndex].End)
                    firstIndex++;
                else
                    secondIndex++;
            }
            return result;
        }

        private static SelectionRange[] Normalize(IEnumerable<SelectionRange> ranges)
        {
            var sorted = ranges.OrderBy(range => range.Start).ThenBy(range => range.End).ToArray();
            var normalized = new List<SelectionRange>();
            foreach (var range in sorted)
            {
                if (range.Start > range.End)
                    continue;
                if (normalized.Count == 0 || normalized[^1].End < (long)range.Start - 1)
                    normalized.Add(range);
                else
                    normalized[^1] = new SelectionRange(normalized[^1].Start, Math.Max(normalized[^1].End, range.End));
            }
            return normalized.ToArray();
        }

        public IEnumerable<int> Snapshot() => _ranges.ToArray().SelectMany(range => range);

        public event Action? Changed;

        public IEnumerator<int> GetEnumerator() => _ranges.ToArray().SelectMany(range => range).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private sealed class BulkUpdateScope(Selection owner) : IDisposable
        {
            private Selection? _owner = owner;

            public void Dispose()
            {
                var selection = Interlocked.Exchange(ref _owner, null);
                if (selection == null)
                    return;
                selection._bulkUpdateDepth--;
                if (selection._bulkUpdateDepth != 0 || !selection._changedDuringBulkUpdate)
                    return;
                selection._changedDuringBulkUpdate = false;
                selection.RaiseChanged();
            }
        }
    }
}
