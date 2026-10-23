using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Data.Tests;

[TestClass]
public class TimeIndexPrefixTests
{
    [TestMethod]
    public void CapturedPrefixSurvivesGrowthAndClear()
    {
        var index = new TimeIndex();
        var start = new DateTime(2026, 10, 23).Ticks;
        index.Add(start);
        index.Add(start + 10);
        var prefix = index.CapturePrefix(2);
        var source = new MergeSource(2, index);
        for (var i = 2; i < 10000; i++)
            index.Add(start + i * 10);
        index.Add(start - 100);
        var latest = index.CapturePrefix(index.Count);
        index.Clear();
        index.Add(start + 500);

        Assert.AreEqual(2, prefix.Count);
        Assert.IsTrue(prefix.IsMonotonic);
        Assert.AreEqual(start, prefix.GetTicksAt(0));
        Assert.AreEqual(start + 10, prefix.GetTicksAt(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => prefix.GetTicksAt(2));
        Assert.AreEqual(start, latest.GetTicksAt(0));
        Assert.IsFalse(latest.IsMonotonic);
        CollectionAssert.AreEqual(new[] { start, start + 10 },
            MergedLineOrder.Build([source]).Select(line => line.Ticks).ToArray());
    }

    [TestMethod]
    public void SnapshotsRemainConsistentDuringConcurrentAppends()
    {
        var index = new TimeIndex();
        var start = new DateTime(2026, 10, 23).Ticks;
        using var begin = new ManualResetEventSlim();
        var writer = Task.Run(() =>
        {
            begin.Wait();
            for (var i = 0; i < 100000; i++)
                index.Add(start + i * 10);
        });
        begin.Set();
        for (var snapshot = 0; snapshot < 1000; snapshot++)
        {
            var prefix = index.CapturePrefix(int.MaxValue);
            for (var i = 0; i < prefix.Count; i += Math.Max(1, prefix.Count / 32))
                Assert.AreEqual(start + i * 10, prefix.GetTicksAt(i));
            if (prefix.Count > 0)
                Assert.AreEqual(start + (prefix.Count - 1) * 10, prefix.GetTicksAt(prefix.Count - 1));
        }
        writer.GetAwaiter().GetResult();
        Assert.AreEqual(100000, index.Count);
    }

    [TestMethod]
    public void CapturingLargePrefixDoesNotCopyItsValues()
    {
        var index = new TimeIndex();
        for (var i = 0; i < 100000; i++)
            index.Add(i);
        for (var i = 0; i < 16; i++)
            index.CapturePrefix(index.Count);
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var count = 0;
        for (var i = 0; i < 1000; i++)
            count += index.CapturePrefix(100000).Count;
        var bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Assert.AreEqual(100000000, count);
        Assert.IsLessThan(1024L, bytes);
    }
}
