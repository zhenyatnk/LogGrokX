using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Data.Tests;

[TestClass]
public class MergedLineOrderTests
{
    [TestMethod]
    public void CancelledBuildLeavesExistingResultUntouched()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var result = new List<MergedLineRef> { new(0, 0, At(1)) };

        Assert.Throws<OperationCanceledException>(() =>
            MergedLineOrder.Build(new[] { new MergeSource(1, Timed(1)) }, result, cancellation.Token));

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(At(1), result[0].Ticks);
    }

    [TestMethod]
    public void BuildObservesCancellationDuringMerge()
    {
        using var cancellation = new CancellationTokenSource();
        var sources = new ObservedList<MergeSource>(new[] { new MergeSource(5000, new TimeIndex()) },
            reads => { if (reads == 50) cancellation.Cancel(); });
        var result = new List<MergedLineRef>();

        Assert.Throws<OperationCanceledException>(() => MergedLineOrder.Build(sources, result, cancellation.Token));

        Assert.IsGreaterThan(0, result.Count);
        Assert.IsLessThan(5000, result.Count);
    }

    [TestMethod]
    public void BuildTimeIndexObservesCancellationDuringTraversal()
    {
        using var cancellation = new CancellationTokenSource();
        var lines = new ObservedList<MergedLineRef>(Enumerable.Range(0, 5000)
                .Select(line => new MergedLineRef(0, line, At(line))).ToArray(),
            reads => { if (reads == 50) cancellation.Cancel(); });
        var target = new TimeIndex();

        Assert.Throws<OperationCanceledException>(() => MergedLineOrder.BuildTimeIndex(lines, target, cancellation.Token));

        Assert.IsGreaterThan(0, target.Count);
        Assert.IsLessThan(5000, target.Count);
    }

    [TestMethod]
    public void CapturedSourceKeepsPublishedPrefixWhileTimeIndexGrows()
    {
        var timeIndex = Timed(10, 20);
        var source = new MergeSource(2, timeIndex);
        timeIndex.Add(At(5));

        var result = MergedLineOrder.Build(new[] { source });

        CollectionAssert.AreEqual(new[] { At(10), At(20) }, result.Select(line => line.Ticks).ToArray());
    }

    private sealed class ObservedList<T>(T[] items, Action<int> onRead) : IReadOnlyList<T>
    {
        private int _reads;
        public int Count => items.Length;
        public T this[int index]
        {
            get
            {
                onRead(++_reads);
                return items[index];
            }
        }
        public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)items).GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static long At(int seconds) => new DateTime(2024, 1, 1).AddSeconds(seconds).Ticks;

    private static TimeIndex Timed(params int[] seconds)
    {
        var index = new TimeIndex();
        foreach (var second in seconds)
            index.Add(At(second));
        return index;
    }

    [TestMethod]
    public void MergesTimedSourcesByTicks()
    {
        var sources = new List<MergeSource>
        {
            new(2, Timed(10, 30)),
            new(2, Timed(20, 40))
        };

        var result = MergedLineOrder.Build(sources);

        CollectionAssert.AreEqual(
            new[] { (0, 0), (1, 0), (0, 1), (1, 1) },
            result.Select(r => (r.SourceIndex, r.LineNumber)).ToArray());
    }

    [TestMethod]
    public void InterleavesThreeSourcesInOrder()
    {
        var sources = new List<MergeSource>
        {
            new(3, Timed(1, 4, 7)),
            new(3, Timed(2, 5, 8)),
            new(3, Timed(3, 6, 9))
        };

        var result = MergedLineOrder.Build(sources);

        CollectionAssert.AreEqual(
            new[] { (0, 0), (1, 0), (2, 0), (0, 1), (1, 1), (2, 1), (0, 2), (1, 2), (2, 2) },
            result.Select(r => (r.SourceIndex, r.LineNumber)).ToArray());
    }

    [TestMethod]
    public void UntimedSourceIsPlacedAfterTimedSources()
    {
        var sources = new List<MergeSource>
        {
            new(2, new TimeIndex()),
            new(2, Timed(5, 6))
        };

        var result = MergedLineOrder.Build(sources);

        CollectionAssert.AreEqual(
            new[] { (1, 0), (1, 1), (0, 0), (0, 1) },
            result.Select(r => (r.SourceIndex, r.LineNumber)).ToArray());
        Assert.AreEqual(-1, result[2].Ticks);
        Assert.AreEqual(-1, result[3].Ticks);
    }

    [TestMethod]
    public void NonMonotonicSourceFallsBackToSequentialOrder()
    {
        var nonMonotonic = new TimeIndex();
        nonMonotonic.Add(At(10));
        nonMonotonic.Add(At(5));

        var sources = new List<MergeSource>
        {
            new(2, nonMonotonic),
            new(1, Timed(1))
        };

        var result = MergedLineOrder.Build(sources);

        CollectionAssert.AreEqual(
            new[] { (1, 0), (0, 0), (0, 1) },
            result.Select(r => (r.SourceIndex, r.LineNumber)).ToArray());
    }

    [TestMethod]
    public void EmptyAndMissingSourcesAreSkipped()
    {
        var sources = new List<MergeSource>
        {
            new(0, new TimeIndex()),
            new(1, Timed(3))
        };

        var result = MergedLineOrder.Build(sources);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(1, result[0].SourceIndex);
    }

    [TestMethod]
    public void BuildTimeIndexAlignsWithMergedLines()
    {
        var sources = new List<MergeSource>
        {
            new(2, Timed(10, 30)),
            new(2, Timed(20, 40))
        };

        var lines = MergedLineOrder.Build(sources);
        var index = MergedLineOrder.BuildTimeIndex(lines);

        Assert.AreEqual(4, index.Count);
        Assert.IsTrue(index.HasTime);
        Assert.IsTrue(index.IsMonotonic);
        CollectionAssert.AreEqual(
            new[] { At(10), At(20), At(30), At(40) },
            Enumerable.Range(0, index.Count).Select(index.GetTicksAt).ToArray());
    }

    [TestMethod]
    public void BuildTimeIndexStaysUntimedWhenNoSourceHasTime()
    {
        var sources = new List<MergeSource>
        {
            new(2, new TimeIndex()),
            new(1, new TimeIndex())
        };

        var lines = MergedLineOrder.Build(sources);
        var index = MergedLineOrder.BuildTimeIndex(lines);

        Assert.AreEqual(3, index.Count);
        Assert.IsFalse(index.HasTime);
    }
}
