using System;
using System.Collections.Generic;
using System.Threading;

namespace LogGrokX.Data
{
    public sealed class TimeIndex
    {
        private readonly object _sync = new();
        private long[] _ticks = Array.Empty<long>();
        private int _count;
        // Captured prefixes share this array; Clear must detach before reusing it.
        private bool _prefixCaptured;
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
                if (_prefixCaptured)
                    _ticks = Array.Empty<long>();
                _prefixCaptured = false;
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
            if (_count == _ticks.Length)
            {
                Array.Resize(ref _ticks, _count == 0 ? 4 : checked(_count * 2));
                _prefixCaptured = false;
            }
            _ticks[_count++] = ticks;
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
                return _ticks[index];
            }
        }

        public TimeIndexPrefix CapturePrefix(int count)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            lock (_sync)
            {
                var capturedCount = Math.Min(count, _count);
                _prefixCaptured |= capturedCount > 0;
                return new TimeIndexPrefix(_ticks, capturedCount, _hasTime, _isMonotonic);
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
                if (_ticks[mid] < ticks)
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
                if (_ticks[mid] <= ticks)
                    low = mid + 1;
                else
                    high = mid;
            }

            return low;
        }
    }
}
