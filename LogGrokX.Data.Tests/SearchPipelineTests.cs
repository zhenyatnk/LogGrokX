using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;
using LogGrokX.Data.Search;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Data.Tests;

[TestClass]
public class SearchPipelineTests
{
    private const string Format =
        @"^(?'Time'\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}) \[(?'Level'[A-Z]+)\] (?'Message'.*)$";

    private static LogMetaInformation Meta() =>
        TestHelpers.CreateMeta(Format, new[] { "Level" }, "Time", "yyyy-MM-dd HH:mm:ss.fff");

    private static string[] SampleLines() => new[]
    {
        "2024-01-02 03:04:05.000 [INFO] first",
        "2024-01-02 03:04:06.000 [ERROR] second",
        "2024-01-02 03:04:07.000 [WARN] third",
        "2024-01-02 03:04:08.000 [ERROR] fourth"
    };

    [TestMethod]
    public async Task FindsMatchingLines()
    {
        var path = TestHelpers.WriteTempFile(string.Join("\r\n", SampleLines()));
        try
        {
            var model = TestHelpers.LoadFile(path, Meta());
            var (progress, searchIndexer, lineIndex) = LogGrokX.Data.Search.Search.CreateSearchIndex(
                model, new Regex("ERROR"), CancellationToken.None);

            await progress.Completion;

            Assert.IsTrue(progress.IsFinished);
            Assert.AreEqual(2, lineIndex.Count);

            var values = new int[lineIndex.Count];
            lineIndex.Fetch(0, values);
            CollectionAssert.AreEqual(new[] { 1, 3 }, values);

            var (sourceIndex, offset, length) = lineIndex.GetLine(0);
            Assert.AreEqual(1, sourceIndex);
            Assert.IsTrue(offset > 0);
            Assert.IsTrue(length > 0);

            Assert.AreEqual(0, lineIndex.GetIndexByOriginalIndex(1));
            Assert.AreEqual(1, lineIndex.GetIndexByOriginalIndex(3));

            var provider = searchIndexer.GetIndexedLinesProvider(new Dictionary<int, IEnumerable<string>>());
            Assert.AreEqual(2, provider.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    [DataRow("\r\n", DisplayName = "End anchor with CRLF")]
    [DataRow("\n", DisplayName = "End anchor with LF")]
    public async Task RegexEndAnchorMatchesBeforeLineEnding(string newline)
    {
        var lines = new[]
        {
            "2024-01-02 03:04:05.000 [ERROR] Payment failed for order 1",
            "2024-01-02 03:04:06.000 [ERROR] Payment failed for order 2",
            "2024-01-02 03:04:07.000 [ERROR] Payment failed for order 3",
            "2024-01-02 03:04:08.000 [ERROR] Payment failed for order 4 "
        };
        var path = TestHelpers.WriteTempFile(string.Join(newline, lines) + newline);
        try
        {
            var model = TestHelpers.LoadFile(path, Meta());
            var (progress, _, lineIndex) = LogGrokX.Data.Search.Search.CreateSearchIndex(
                model, new Regex(@"order \d+$"), CancellationToken.None);

            await progress.Completion;

            Assert.AreEqual(3, lineIndex.Count);
            var matches = new int[lineIndex.Count];
            lineIndex.Fetch(0, matches);
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, matches);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task SearchRespectsLineRange()
    {
        var path = TestHelpers.WriteTempFile(string.Join("\r\n", SampleLines()));
        try
        {
            var model = TestHelpers.LoadFile(path, Meta());
            var (progress, _, lineIndex) = LogGrokX.Data.Search.Search.CreateSearchIndex(
                model, new Regex("ERROR"), CancellationToken.None, (2, 4));

            await progress.Completion;

            Assert.AreEqual(1, lineIndex.Count);
            Assert.AreEqual(3, lineIndex.GetLine(0).sourceIndex);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task SearchWithoutMatchesCompletes()
    {
        var path = TestHelpers.WriteTempFile(string.Join("\r\n", SampleLines()));
        try
        {
            var model = TestHelpers.LoadFile(path, Meta());
            var (progress, _, lineIndex) = LogGrokX.Data.Search.Search.CreateSearchIndex(
                model, new Regex("NOT-PRESENT"), CancellationToken.None);

            await progress.Completion;

            Assert.AreEqual(0, lineIndex.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task ProgressReportsCompletion()
    {
        var progress = new LogGrokX.Data.Search.Search.Progress();
        Assert.IsFalse(progress.IsFinished);
        Assert.IsFalse(progress.Completion.IsCompleted);

        progress.Value = 0.5;
        progress.IsFinished = true;

        await progress.Completion;
        Assert.IsTrue(progress.IsFinished);
    }

    [TestMethod]
    public void SearchLineIndexMapsOriginalIndices()
    {
        var source = new LineIndex();
        source.Add(0);
        source.Add(10);
        source.Add(20);
        source.Finish(5);

        var searchLineIndex = new SearchLineIndex(source);
        Assert.AreEqual(0, searchLineIndex.Add(1));
        Assert.AreEqual(1, searchLineIndex.Add(2));
        Assert.AreEqual(2, searchLineIndex.Count);

        var values = new int[2];
        searchLineIndex.Fetch(0, values);
        CollectionAssert.AreEqual(new[] { 1, 2 }, values);

        var (sourceIndex, offset, length) = searchLineIndex.GetLine(0);
        Assert.AreEqual(1, sourceIndex);
        Assert.AreEqual(10, offset);
        Assert.AreEqual(10, length);

        Assert.AreEqual(0, searchLineIndex.GetIndexByOriginalIndex(1));
        Assert.AreEqual(1, searchLineIndex.GetIndexByOriginalIndex(2));
    }

    [TestMethod]
    public async Task ValueTaskSourceCompletesWithResult()
    {
        var source = new ValueTaskSource<int>();
        var task = new ValueTask<int>(source, 0);

        source.SetResult(42);

        Assert.AreEqual(42, await task);
        Assert.AreEqual(ValueTaskSourceStatus.Succeeded, source.GetStatus(0));
    }

    [TestMethod]
    public async Task ValueTaskSourcePropagatesException()
    {
        var source = new ValueTaskSource<int>();
        var task = new ValueTask<int>(source, 0);

        source.SetException(new InvalidOperationException("boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await task);
    }
}

