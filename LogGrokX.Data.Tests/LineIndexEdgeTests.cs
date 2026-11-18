using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Data.Tests;

[TestClass]
public class LineIndexEdgeTests
{
    [TestMethod]
    public void FetchThrowsWhenStartBeyondEnd()
    {
        var index = new LineIndex();
        index.Add(0);
        index.Add(10);
        index.Add(20);
        index.Finish(5);

        Assert.Throws<IndexOutOfRangeException>(() => index.Fetch(3, new (long, int)[1]));
    }

    [TestMethod]
    public void FetchThrowsWhenBufferExceedsRemainingOnUnfinishedIndex()
    {
        var index = new LineIndex();
        index.Add(0);
        index.Add(10);
        index.Add(20);

        Assert.Throws<IndexOutOfRangeException>(() => index.Fetch(0, new (long, int)[5]));
    }

    [TestMethod]
    public void FetchSpansOffsetBlocksBeyondFourGiB()
    {
        var index = new LineIndex();
        const long first = 1L << 32;
        for (var i = 0; i < 515; i++)
            index.Add(first + i * 100);
        index.Finish(7);

        var lines = new (long, int)[4];
        index.Fetch(511, lines);

        CollectionAssert.AreEqual(new[]
        {
            (first + 51100, 100), (first + 51200, 100), (first + 51300, 100), (first + 51400, 7)
        }, lines);
    }

    [TestMethod]
    public void WideOffsetBlockPreservesAlreadyPublishedOffsets()
    {
        var index = new LineIndex();
        const int largeLine = 1_500_000_000;
        index.Add(30);
        index.Add(31);
        for (var i = 1; i <= 2; i++)
            index.Add(31 + (long)i * largeLine);

        Assert.AreEqual((31L, largeLine), index.GetLine(1));

        index.Add(31 + 3L * largeLine);
        index.Finish(9);

        var lines = new (long, int)[5];
        index.Fetch(0, lines);
        CollectionAssert.AreEqual(new[]
        {
            (30L, 1), (31L, largeLine), (31L + largeLine, largeLine),
            (31L + 2L * largeLine, largeLine), (31L + 3L * largeLine, 9)
        }, lines);
    }

    [TestMethod]
    public async Task FetchRangesRefreshesCountAfterDelay()
    {
        var index = new LineIndex();
        index.Add(0);

        var finisher = Task.Run(async () =>
        {
            await Task.Delay(50);
            index.Finish(10);
        });

        var ranges = new List<(int, int)>();
        await foreach (var range in index.FetchRanges(CancellationToken.None))
            ranges.Add(range);

        await finisher;

        CollectionAssert.AreEqual(new[] { (0, 1) }, ranges);
    }

    [TestMethod]
    public async Task FetchRangesWakesWhenEnoughLinesArrive()
    {
        var index = new LineIndex();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await using var ranges = index.FetchRanges(cancellation.Token).GetAsyncEnumerator();
        var next = ranges.MoveNextAsync().AsTask();

        for (var i = 0; i <= 256; i++)
            index.Add(i * 10L);

        Assert.IsTrue(await next.WaitAsync(TimeSpan.FromMilliseconds(200)));
        Assert.AreEqual((0, 256), ranges.Current);
        index.Finish(10);
    }

    [TestMethod]
    public async Task FetchRangesPublishesCompleteBatchesAndFinalPartialRange()
    {
        var index = new LineIndex();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var ranges = index.FetchRanges(cancellation.Token).GetAsyncEnumerator();
        var next = ranges.MoveNextAsync().AsTask();
        for (var i = 0; i < 256; i++)
            index.Add(i * 10L);
        Assert.IsFalse(next.IsCompleted);

        index.Add(2560);
        Assert.IsTrue(await next.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.AreEqual((0, 256), ranges.Current);

        next = ranges.MoveNextAsync().AsTask();
        for (var i = 257; i <= 512; i++)
            index.Add(i * 10L);
        Assert.IsTrue(await next.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.AreEqual((256, 256), ranges.Current);

        next = ranges.MoveNextAsync().AsTask();
        index.Add(5130);
        Assert.IsFalse(next.IsCompleted);
        index.Finish(7);
        Assert.IsTrue(await next.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.AreEqual((512, 2), ranges.Current);
        Assert.IsFalse(await ranges.MoveNextAsync());
    }

    [TestMethod]
    public async Task FetchRangesCanCancelWhileWaitingForBatch()
    {
        var index = new LineIndex();
        using var cancellation = new CancellationTokenSource();
        await using var ranges = index.FetchRanges(cancellation.Token).GetAsyncEnumerator();
        var next = ranges.MoveNextAsync().AsTask();
        index.Add(0);
        cancellation.Cancel();

        Assert.IsFalse(await next.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [TestMethod]
    public async Task FetchRangesWakesWhenFinishedBelowBatchSize()
    {
        var index = new LineIndex();
        index.Add(0);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await using var ranges = index.FetchRanges(cancellation.Token).GetAsyncEnumerator();
        var next = ranges.MoveNextAsync().AsTask();

        index.Finish(10);

        Assert.IsTrue(await next.WaitAsync(TimeSpan.FromMilliseconds(200)));
        Assert.AreEqual((0, 1), ranges.Current);
    }
}
