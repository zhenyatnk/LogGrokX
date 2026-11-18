using System;

namespace LogGrokX.Data;

public readonly struct TimeIndexPrefix
{
    private readonly TimeIndex.Page[] _pages;

    internal TimeIndexPrefix(TimeIndex.Page[] pages, int count, bool hasTime, bool isMonotonic)
    {
        _pages = pages;
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
        return TimeIndex.ReadTicks(_pages, index);
    }
}
