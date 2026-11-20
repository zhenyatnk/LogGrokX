using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using LogGrokX.Data.Index;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Data.Tests;

[TestClass]
public class LoaderPipelineTests
{
    private const string Format =
        @"^(?'Time'\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}) \[(?'Level'[A-Z]+)\] (?'Message'.*)$";

    private static LogMetaInformation Meta() =>
        TestHelpers.CreateMeta(Format, new[] { "Level" }, "Time", "yyyy-MM-dd HH:mm:ss.fff");

    private static string[] SampleLines() => new[]
    {
        "2024-01-02 03:04:05.678 [INFO] first message",
        "not a log line",
        "2024-01-02 03:04:06.000 [WARN] second message",
        "2024-01-02 03:04:07.000 [ERROR] third message"
    };

    [TestMethod]
    public void LoadsMatchingLinesAndBuildsIndexes()
    {
        var path = TestHelpers.WriteTempFile(string.Join("\r\n", SampleLines()));
        try
        {
            var model = TestHelpers.LoadFile(path, Meta());

            Assert.AreEqual(3, model.LineCount);
            Assert.IsTrue(model.IsLoaded);
            Assert.AreEqual(100, model.LoadProgress);

            var components = model.Indexer.GetAllComponents(0).ToList();
            CollectionAssert.AreEquivalent(new[] { "INFO", "WARN", "ERROR" }, components);

            Assert.AreEqual(1, model.Indexer.GetIndexCountForComponent(0, "INFO"));
            Assert.AreEqual(2, model.Indexer.GetIndexCountForComponent(0, "WARN") +
                                model.Indexer.GetIndexCountForComponent(0, "ERROR"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void LineProviderReturnsOriginalLines()
    {
        var path = TestHelpers.WriteTempFile(string.Join("\r\n", SampleLines()));
        try
        {
            var model = TestHelpers.LoadFile(path, Meta());

            var lines = new (int, string)[model.LineCount];
            model.LineProvider.Fetch(0, lines);

            Assert.AreEqual("2024-01-02 03:04:05.678 [INFO] first message\r\nnot a log line",
                lines[0].Item2.TrimEnd('\r', '\n'));
            Assert.AreEqual("2024-01-02 03:04:07.000 [ERROR] third message", lines[2].Item2.TrimEnd('\r', '\n'));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void IndexedLinesProviderReturnsAllLines()
    {
        var path = TestHelpers.WriteTempFile(string.Join("\r\n", SampleLines()));
        try
        {
            var model = TestHelpers.LoadFile(path, Meta());
            var provider = model.Indexer.GetIndexedLinesProvider(
                new Dictionary<int, IEnumerable<string>>());

            Assert.AreEqual(3, provider.Count);

            var values = new int[provider.Count];
            provider.Fetch(0, values);
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, values);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void IndexedLinesProviderRespectsExcludedComponents()
    {
        var path = TestHelpers.WriteTempFile(string.Join("\r\n", SampleLines()));
        try
        {
            var model = TestHelpers.LoadFile(path, Meta());
            var excluded = new Dictionary<int, IEnumerable<string>> { [0] = new[] { "INFO" } };
            var provider = model.Indexer.GetIndexedLinesProvider(excluded);

            Assert.AreEqual(2, provider.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void IsLineIncludedChecksExcludedComponents()
    {
        var path = TestHelpers.WriteTempFile(string.Join("\r\n", SampleLines()));
        try
        {
            var model = TestHelpers.LoadFile(path, Meta());
            var excluded = new Dictionary<int, IEnumerable<string>> { [0] = new[] { "INFO" } };

            Assert.IsFalse(model.Indexer.IsLineIncluded(0, excluded));
            Assert.IsTrue(model.Indexer.IsLineIncluded(1, excluded));
            Assert.IsTrue(model.Indexer.IsLineIncluded(-1, excluded));
            Assert.IsTrue(model.Indexer.IsLineIncluded(100, excluded));
            Assert.IsTrue(model.Indexer.IsLineIncluded(0, new Dictionary<int, IEnumerable<string>>()));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void GetIndexKeyNumReturnsKeyOfLine()
    {
        var path = TestHelpers.WriteTempFile(string.Join("\r\n", SampleLines()));
        try
        {
            var model = TestHelpers.LoadFile(path, Meta());
            var keyNum = model.Indexer.GetIndexKeyNum(0);
            var key = model.Indexer.GetIndex(keyNum);

            Assert.AreEqual(1, key.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void LoadsFileWithoutTrailingNewLine()
    {
        var path = TestHelpers.WriteTempFile("2024-01-02 03:04:05.678 [INFO] only line");
        try
        {
            var model = TestHelpers.LoadFile(path, Meta());

            Assert.AreEqual(1, model.LineCount);
            var lines = new (int, string)[1];
            model.LineProvider.Fetch(0, lines);
            Assert.AreEqual("2024-01-02 03:04:05.678 [INFO] only line", lines[0].Item2);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void HandlesLeadingNonMatchingLine()
    {
        var path = TestHelpers.WriteTempFile(
            "garbage before the log\r\n2024-01-02 03:04:05.678 [INFO] real line");
        try
        {
            var model = TestHelpers.LoadFile(path, Meta());

            Assert.AreEqual(1, model.LineCount);
            var lines = new (int, string)[1];
            model.LineProvider.Fetch(0, lines);
            Assert.AreEqual("2024-01-02 03:04:05.678 [INFO] real line", lines[0].Item2);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void LoadsEmptyFile()
    {
        var path = TestHelpers.WriteTempFile(string.Empty);
        try
        {
            var model = TestHelpers.LoadFile(path, Meta());

            Assert.AreEqual(0, model.LineCount);
            Assert.AreEqual(0, model.LoadProgress);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void LoadsEmptyFileWithPlainTextFormat()
    {
        var meta = LogMetaInformation.CreateTextFileMetaInformation();
        var path = TestHelpers.WriteTempFile(string.Empty);
        try
        {
            var model = TestHelpers.LoadFile(path, meta);

            Assert.AreEqual(0, model.LineCount);
            Assert.AreEqual(0, model.LoadProgress);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void HandlesTinyBufferBoundaries()
    {
        var lines = Enumerable.Range(0, 50)
            .Select(i => $"2024-01-02 03:04:{i:D2}.000 [INFO] message number {i}")
            .ToArray();
        var path = TestHelpers.WriteTempFile(string.Join("\r\n", lines));
        try
        {
            var model = TestHelpers.LoadFile(path, Meta(), bufferSize: 16);

            Assert.AreEqual(50, model.LineCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void SwitchesParsedBufferForLargeInput()
    {
        var lines = Enumerable.Range(0, 4000)
            .Select(i => $"2024-01-02 03:04:05.678 [INFO] message number {i} with some padding text")
            .ToArray();
        var path = TestHelpers.WriteTempFile(string.Join("\r\n", lines));
        try
        {
            var model = TestHelpers.LoadFile(path, Meta(), bufferSize: 8192);

            Assert.AreEqual(4000, model.LineCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void LoadsPlainTextFile()
    {
        var meta = LogMetaInformation.CreateTextFileMetaInformation();
        var path = TestHelpers.WriteTempFile("line one\r\nline two\r\nline three");
        try
        {
            var model = TestHelpers.LoadFile(path, meta);

            Assert.AreEqual(3, model.LineCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private const string VennFormat =
        @"^(?<Time>\d{4}-\d{2}-\d{2}\s\d{1,2}:\d{2}:\d{2}\.\d{3})\s-\s\[(?<Level>[A-Z]+)\]\s-\s\[(?<Context>(?:(?<Sid>\d+):)?(?<Pid>\d+):(?<Thread>\d+)(?::[MU-]:[MU-])?|[^\]]*)\](?:\s-\s\[(?<Process>[^\]]*)\]\s-\s\[irql\s(?<Irql>\d+)\](?:\s-\s\[(?<Source>[^\]]*)\])?|\s-\s\[(?<Process>[^\]]*)\]\s-\s\[(?<Source>[^\]]*)\]|\s-\s\[(?<Source>[^\]]*)\])?\s-\s(?<Message>.*)";

    [TestMethod]
    public void LoadsIndexedFieldsThatAppearOutOfTextOrder()
    {
        var lines = new[]
        {
            "2026-05-14 9:05:01.123 - [INFO] - [1:200:300] - [svc.exe] - [Core] - [V01] venn info",
            "2026-05-14 9:05:02.456 - [ERRO] - [1:200:301] - [svc.exe] - [Core] - [V02] venn error",
            "2026-05-14 9:05:03.789 - [WARN] - [1:200:300] - [svc.exe] - [Net] - [V03] venn warn",
            "2026-05-14 9:05:04.000 - [CRIT] - [1:200:302] - [svc.exe] - [irql 2] - [Drv] - [V04] venn crit",
            "2026-05-14 9:05:05.000 - [TRAC] - [1:200:300] - [Core] - [V05] venn trace"
        };
        var meta = TestHelpers.CreateMeta(VennFormat,
            new[] { "Level", "Sid", "Pid", "Thread", "Process", "Source", "Irql" },
            "Time", "yyyy-MM-dd H:mm:ss.fff");
        var path = TestHelpers.WriteTempFile(string.Join("\r\n", lines));
        try
        {
            var model = TestHelpers.LoadFile(path, meta);

            Assert.AreEqual(5, model.LineCount);
            Assert.IsTrue(model.IsLoaded);
            CollectionAssert.Contains(model.Indexer.GetAllComponents(6).ToList(), "2");
            CollectionAssert.AreEquivalent(new[] { "Core", "Net", "Drv" }, model.Indexer.GetAllComponents(5).ToList());
            CollectionAssert.AreEquivalent(new[] { "INFO", "ERRO", "WARN", "CRIT", "TRAC" },
                model.Indexer.GetAllComponents(0).ToList());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void LoadsBomlessUtf16Lines()
    {
        var lines = Enumerable.Range(1, 5)
            .Select(index => $"2024-01-02 03:04:05.678 [INFO] привет ü umlaut №{index}")
            .ToArray();
        var path = TestHelpers.WriteTempBytes(
            System.Text.Encoding.Unicode.GetBytes(string.Join("\r\n", lines)));
        try
        {
            var model = TestHelpers.LoadFile(path, Meta());

            Assert.AreEqual(5, model.LineCount);
            var fetched = new (int, string)[model.LineCount];
            model.LineProvider.Fetch(0, fetched);
            StringAssert.Contains(fetched[2].Item2, "umlaut №3");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void LoaderCompletesAndReportsNotLoading()
    {
        var path = TestHelpers.WriteTempFile(string.Join("\r\n", SampleLines()));
        try
        {
            var meta = Meta();
            var logFile = new LogFile(path, 0);
            var lineIndex = new LineIndex();
            var indexer = new Indexer();
            var stringPool = new StringPool();
            var timeIndex = new TimeIndex();
            using var parsedBufferConsumer = new ParsedBufferConsumer(lineIndex, indexer, meta, stringPool);
            var parser = new RegexBasedLineParser(meta, true);
            var lineProcessor = new LineProcessor(logFile, meta, parser, parsedBufferConsumer, stringPool, timeIndex);

            var loader = new Loader(logFile, lineProcessor, NullLogger.Instance);
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            while (loader.IsLoading && DateTime.UtcNow < deadline)
                Thread.Sleep(5);

            loader.Dispose();
            Assert.IsFalse(loader.IsLoading);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void LoaderCanBeCancelled()
    {
        var lines = Enumerable.Range(0, 200000)
            .Select(i => $"2024-01-02 03:04:05.678 [INFO] message number {i} with padding")
            .ToArray();
        var path = TestHelpers.WriteTempFile(string.Join("\r\n", lines));
        try
        {
            var meta = Meta();
            var logFile = new LogFile(path, 0);
            var lineIndex = new LineIndex();
            var indexer = new Indexer();
            var stringPool = new StringPool();
            var timeIndex = new TimeIndex();
            using var parsedBufferConsumer = new ParsedBufferConsumer(lineIndex, indexer, meta, stringPool);
            var parser = new RegexBasedLineParser(meta, true);
            var lineProcessor = new LineProcessor(logFile, meta, parser, parsedBufferConsumer, stringPool, timeIndex);

            var loader = new Loader(logFile, lineProcessor, NullLogger.Instance);
            loader.Dispose();
            Assert.IsFalse(loader.IsLoading);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
