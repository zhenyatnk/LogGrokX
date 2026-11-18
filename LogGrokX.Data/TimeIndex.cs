using System;
using System.Collections.Generic;
using System.Threading;

namespace LogGrokX.Data
{
    public sealed class TimeIndex
    {
        private const int PageShift = 12;
        private const int PageSize = 1 << PageShift;
        private const int PageMask = PageSize - 1;

        private readonly object _sync = new();
        private Page[] _pages = Array.Empty<Page>();
        private Page? _lastPage;
        private int _count;
        private readonly List<int> _dayBoundaries = new();
        private long _lastTicks;
        private long _lastRawTicks;
        private long _lastDayNumber = long.MinValue;
        private long _dayOffset;
        private long _minTicks = long.MaxValue;
        private long _maxTicks = long.MinValue;

        private bool _hasTime;
        private bool _isMonotonic = true;

        public bool HasTime { get { lock (_sync) return _hasTime; } }

        public bool IsMonotonic { get { lock (_sync) return _isMonotonic; } }

        public int Count { get { lock (_sync) return _count; } }

        public IReadOnlyList<int> DayBoundaries { get { lock (_sync) return _dayBoundaries.ToArray(); } }

        public long MinTicks { get { lock (_sync) return _minTicks; } }

        public long MaxTicks { get { lock (_sync) return _maxTicks; } }

        public void Clear()
        {
            lock (_sync)
            {
                _pages = Array.Empty<Page>();
                _lastPage = null;
                _count = 0;
                _dayBoundaries.Clear();
                _lastTicks = 0;
                _lastRawTicks = 0;
                _lastDayNumber = long.MinValue;
                _dayOffset = 0;
                _minTicks = long.MaxValue;
                _maxTicks = long.MinValue;
                _hasTime = false;
                _isMonotonic = true;
            }
        }

        public void Add(long ticks)
        {
            lock (_sync)
                AddCore(ticks);
        }

        internal void AddRange<T>(IReadOnlyList<T> values, Func<T, long> getTicks, CancellationToken token)
        {
            lock (_sync)
            {
                for (var i = 0; i < values.Count; i++)
                {
                    if ((i & 1023) == 0)
                        token.ThrowIfCancellationRequested();
                    AddCore(getTicks(values[i]));
                }
            }
        }

        private void Append(long ticks)
        {
            var offset = _count & PageMask;
            if (offset == 0)
            {
                var pageIndex = _count >> PageShift;
                if (pageIndex == _pages.Length)
                    Array.Resize(ref _pages, pageIndex == 0 ? 4 : pageIndex * 2);
                _lastPage = new Page(ticks, pageIndex == 0 ? 16 : PageSize);
                _pages[pageIndex] = _lastPage;
            }

            _lastPage!.Add(offset, ticks);
            _count++;
        }

        private void AddCore(long ticks)
        {
            if (ticks < 0)
            {
                Append(_lastTicks);
                return;
            }

            var normalized = ticks;
            if (ticks < TimeSpan.TicksPerDay)
            {
                if (_count > 0 && ticks < _lastRawTicks &&
                    _lastRawTicks - ticks > TimeSpan.TicksPerHour)
                {
                    _dayOffset += TimeSpan.TicksPerDay;
                }

                normalized = ticks + _dayOffset;
                if (normalized < _lastTicks)
                    normalized = _lastTicks;
            }

            if (_count > 0 && normalized < _lastTicks)
                _isMonotonic = false;

            _lastRawTicks = ticks;
            _lastTicks = normalized;
            _hasTime = true;

            var dayNumber = normalized / TimeSpan.TicksPerDay;
            if (_count > 0 && dayNumber != _lastDayNumber)
                _dayBoundaries.Add(_count);
            _lastDayNumber = dayNumber;

            if (normalized < _minTicks) _minTicks = normalized;
            if (normalized > _maxTicks) _maxTicks = normalized;

            Append(normalized);
        }

        public long GetTicksAt(int index)
        {
            lock (_sync)
            {
                ArgumentOutOfRangeException.ThrowIfNegative(index);
                ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _count);
                return ReadTicks(_pages, index);
            }
        }

        public TimeIndexPrefix CapturePrefix(int count)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            lock (_sync)
            {
                var capturedCount = Math.Min(count, _count);
                return new TimeIndexPrefix(_pages, capturedCount, _hasTime, _isMonotonic);
            }
        }

        public (int StartLine, int EndLine)? FindLineRange(long fromTicks, long toTicks)
        {
            lock (_sync)
            {
                if (!_hasTime || !_isMonotonic || _count == 0)
                    return null;

                var start = LowerBound(fromTicks);
                var end = UpperBound(toTicks);
                if (end < start) end = start;

                return (start, end);
            }
        }

        private int LowerBound(long ticks)
        {
            var low = 0;
            var high = _count;
            while (low < high)
            {
                var mid = low + (high - low) / 2;
                if (ReadTicks(_pages, mid) < ticks)
                    low = mid + 1;
                else
                    high = mid;
            }

            return low;
        }

        private int UpperBound(long ticks)
        {
            var low = 0;
            var high = _count;
            while (low < high)
            {
                var mid = low + (high - low) / 2;
                if (ReadTicks(_pages, mid) <= ticks)
                    low = mid + 1;
                else
                    high = mid;
            }

            return low;
        }

        internal static long ReadTicks(Page[] pages, int index) =>
            pages[index >> PageShift].GetTicks(index & PageMask);

        internal sealed class Page(long baseTicks, int initialCapacity)
        {
            // Prefixes can read an old buffer during growth. Its initialized entries must remain unchanged.
            private Array _values = Array.Empty<ushort>();

            public long GetTicks(int index)
            {
                var values = Volatile.Read(ref _values);
                if (values is long[] absolute)
                    return absolute[index];

                var deltas = (ushort[])values;
                return deltas.Length == 0 ? baseTicks : baseTicks + deltas[index] * TimeSpan.TicksPerMillisecond;
            }

            public void Add(int index, long ticks)
            {
                if (_values is long[] absolute)
                {
                    Append(absolute, index, ticks);
                    return;
                }

                var deltas = (ushort[])_values;
                var delta = unchecked((ulong)ticks - (ulong)baseTicks);
                if (delta == 0 && deltas.Length == 0)
                    return;

                var milliseconds = delta / TimeSpan.TicksPerMillisecond;
                if (milliseconds <= ushort.MaxValue && milliseconds * TimeSpan.TicksPerMillisecond == delta)
                {
                    Append(deltas, index, (ushort)milliseconds);
                    return;
                }

                var expanded = new long[GetCapacity(index, deltas.Length)];
                if (deltas.Length == 0)
                    Array.Fill(expanded, baseTicks, 0, index);
                else
                    for (var i = 0; i < index; i++)
                        expanded[i] = baseTicks + deltas[i] * TimeSpan.TicksPerMillisecond;
                expanded[index] = ticks;
                Volatile.Write(ref _values, expanded);
            }

            private void Append<T>(T[] values, int index, T value)
            {
                if (index < values.Length)
                {
                    values[index] = value;
                    return;
                }

                var expanded = new T[GetCapacity(index, values.Length)];
                values.CopyTo(expanded, 0);
                expanded[index] = value;
                Volatile.Write(ref _values, expanded);
            }

            private int GetCapacity(int index, int length) =>
                Math.Min(PageSize, Math.Max(index + 1, length == 0 ? initialCapacity : length * 2));
        }
    }
}
