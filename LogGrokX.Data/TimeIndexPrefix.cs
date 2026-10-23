using System;

namespace LogGrokX.Data;

public readonly struct TimeIndexPrefix
{
    private readonly long[] _ticks;

    internal TimeIndexPrefix(long[] ticks, int count, bool hasTime, bool isMonotonic)
    {
        _ticks = ticks;
        Count = count;
        HasTime = hasTime;
        IsMonotonic = isMonotonic;
    }

    public int Count { get; }

    public bool HasTime { get; }

    public bool IsMonotonic { get; }

    public long GetTicksAt(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
        return _ticks[index];
    }
}
