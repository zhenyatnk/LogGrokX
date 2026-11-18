using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Data.Tests;

[TestClass]
public class TimeIndexTests
{
    private static long At(int seconds) => new DateTime(2024, 1, 1).AddSeconds(seconds).Ticks;

    [TestMethod]
    public void TracksBoundsForSequence()
    {
        var index = new TimeIndex();
        index.Add(At(0));
        index.Add(At(10));
        index.Add(At(5));

        Assert.IsTrue(index.HasTime);
        Assert.AreEqual(3, index.Count);
        Assert.AreEqual(At(0), index.MinTicks);
        Assert.AreEqual(At(10), index.MaxTicks);
    }

    [TestMethod]
    public void DetectsNonMonotonicSequence()
    {
        var index = new TimeIndex();
        index.Add(At(10));
        index.Add(At(5));

        Assert.IsFalse(index.IsMonotonic);
    }

    [TestMethod]
    public void NormalizesDayRolloverForTimeOfDayLogs()
    {
        var index = new TimeIndex();
        index.Add(TimeSpan.FromHours(23).Add(TimeSpan.FromMinutes(59)).Ticks);
        index.Add(TimeSpan.FromMinutes(1).Ticks);

        Assert.IsTrue(index.HasTime);
        Assert.IsTrue(index.IsMonotonic);
        Assert.IsTrue(index.MaxTicks > index.MinTicks);
        Assert.IsNotNull(index.FindLineRange(index.MinTicks, index.MaxTicks));
    }

    [TestMethod]
    public void ClampsSmallOutOfOrderTimeOfDayTicks()
    {
        var index = new TimeIndex();
        index.Add(TimeSpan.FromSeconds(10).Ticks);
        index.Add(TimeSpan.FromSeconds(9).Ticks);

        Assert.IsTrue(index.IsMonotonic);
        Assert.AreEqual(TimeSpan.FromSeconds(10).Ticks, index.GetTicksAt(1));
    }

    [TestMethod]
    public void TracksDayBoundaryForTimeOfDayRollover()
    {
        var index = new TimeIndex();
        index.Add(TimeSpan.FromHours(23).Add(TimeSpan.FromMinutes(59)).Ticks);
        index.Add(TimeSpan.FromMinutes(1).Ticks);
        index.Add(TimeSpan.FromMinutes(2).Ticks);

        Assert.AreEqual(1, index.DayBoundaries.Count);
        Assert.AreEqual(1, index.DayBoundaries[0]);
    }

    [TestMethod]
    public void TracksDayBoundaryForAbsoluteTicks()
    {
        var index = new TimeIndex();
        index.Add(At(0));
        index.Add(At(86400));

        Assert.AreEqual(1, index.DayBoundaries.Count);
        Assert.AreEqual(1, index.DayBoundaries[0]);
    }

    [TestMethod]
    public void HasNoDayBoundaryWithinSameDay()
    {
        var index = new TimeIndex();
        index.Add(TimeSpan.FromHours(8).Ticks);
        index.Add(TimeSpan.FromHours(9).Ticks);

        Assert.AreEqual(0, index.DayBoundaries.Count);
    }

    [TestMethod]
    public void MissingTimestampReusesPreviousValue()
    {
        var index = new TimeIndex();
        index.Add(At(10));
        index.Add(-1);
        index.Add(At(20));

        Assert.AreEqual(At(10), index.GetTicksAt(1));
        Assert.IsTrue(index.IsMonotonic);
    }

    [TestMethod]
    public void FindLineRangeReturnsNullWhenUnavailable()
    {
        var empty = new TimeIndex();
        Assert.IsNull(empty.FindLineRange(0, long.MaxValue));

        var nonMonotonic = new TimeIndex();
        nonMonotonic.Add(At(10));
        nonMonotonic.Add(At(5));
        Assert.IsNull(nonMonotonic.FindLineRange(At(0), At(20)));
    }

    [TestMethod]
    public void FindLineRangeReturnsInclusiveBounds()
    {
        var index = new TimeIndex();
        for (var i = 0; i < 5; i++)
            index.Add(At(i * 10));

        var range = index.FindLineRange(At(20), At(40));

        Assert.IsNotNull(range);
        Assert.AreEqual(2, range.Value.StartLine);
        Assert.AreEqual(5, range.Value.EndLine);
    }

    [TestMethod]
    public void FindLineRangeClampsToAvailableLines()
    {
        var index = new TimeIndex();
        for (var i = 0; i < 5; i++)
            index.Add(At(i * 10));

        var range = index.FindLineRange(At(-100), At(1000));

        Assert.IsNotNull(range);
        Assert.AreEqual(0, range.Value.StartLine);
        Assert.AreEqual(5, range.Value.EndLine);
    }

    [TestMethod]
    public void DenseMillisecondTimestampsUseCompactPages()
    {
        const int count = 200000;
        const long step = 3 * TimeSpan.TicksPerMillisecond;
        var index = new TimeIndex();
        var start = At(0);
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < count; i++)
            index.Add(start + i * step);
        var bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;

        Assert.IsLessThan(600000L, bytes);
        Assert.AreEqual(start + (count - 1) * step, index.GetTicksAt(count - 1));
        Assert.AreEqual((4000, 4201), index.FindLineRange(start + 4000 * step, start + 4200 * step));
    }
}
