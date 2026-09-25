using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using LogGrokX.Data.Index;
using LogGrokX.Data.Search;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Data.Tests;

[TestClass]
public class OptimizationTests
{
    private const string LineRegex =
        @"^(?<Time>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}) \[(?<Level>[A-Z]+)\] (?<Message>.*)$";

    [DataTestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public void LoadingKeepsLineOrderAndCount(bool parallelParsing, bool parallelIndexing)
    {
        const int lineCount = 20_000;
        var meta = TestHelpers.CreateMeta(LineRegex, new[] { "Level" }, "Time", "yyyy-MM-dd HH:mm:ss.fff");

        var builder = new StringBuilder();
        var levels = new[] { "INFO", "WARN", "ERROR" };
        for (var i = 0; i < lineCount; i++)
        {
            builder.Append(
                $"2024-01-15 08:32:{i % 60:00}.{i % 1000:000} [{levels[i % levels.Length]}] line {i}\r\n");
        }

        var path = TestHelpers.WriteTempFile(builder.ToString());
        LineProcessor.ParallelParsingOverride = parallelParsing;
        ParsedBufferConsumer.ParallelIndexingOverride = parallelIndexing;
        try
        {
            var model = TestHelpers.LoadFile(path, meta);
            Assert.AreEqual(lineCount, model.LineCount);

            // line numbers must still address the very same lines, in order
            foreach (var lineNumber in new[] { 0, 1, 7, 1023, 1024, 9999, lineCount - 1 })
            {
                var items = new (int, string)[1];
                model.LineProvider.Fetch(lineNumber, items);
                StringAssert.Contains(items[0].Item2, $"line {lineNumber}");
            }

            var components = model.Indexer.GetAllComponents(0).ToList();
            CollectionAssert.AreEquivalent(levels, components);

            var perLevel = levels.Sum(level => model.Indexer.GetIndexCountForComponent(0, level));
            Assert.AreEqual(lineCount, perLevel);
        }
        finally
        {
            LineProcessor.ParallelParsingOverride = null;
            ParsedBufferConsumer.ParallelIndexingOverride = null;
            File.Delete(path);
        }
    }

    [TestMethod]
    public void ParallelIndexingProducesSameIndexesAsSequential()
    {
        var meta = TestHelpers.CreateMeta(LineRegex, new[] { "Level" }, "Time", "yyyy-MM-dd HH:mm:ss.fff");
        var levels = new[] { "INFO", "WARN", "ERROR" };
        var builder = new StringBuilder();
        for (var i = 0; i < 30_000; i++)
            builder.Append($"2024-01-15 08:32:11.482 [{levels[i % levels.Length]}] line {i}\r\n");

        var path = TestHelpers.WriteTempFile(builder.ToString());
        try
        {
            var sequential = LoadWithModes(path, meta, false, false);
            var parallel = LoadWithModes(path, meta, true, true);

            Assert.AreEqual(sequential.LineCount, parallel.LineCount);
            foreach (var level in levels)
            {
                Assert.AreEqual(
                    sequential.Indexer.GetIndexCountForComponent(0, level),
                    parallel.Indexer.GetIndexCountForComponent(0, level),
                    $"line count for {level} differs");
            }

            var excluded = new Dictionary<int, IEnumerable<string>> { [0] = new[] { "INFO" } };
            for (var lineNumber = 0; lineNumber < 1000; lineNumber++)
            {
                Assert.AreEqual(
                    sequential.Indexer.IsLineIncluded(lineNumber, excluded),
                    parallel.Indexer.IsLineIncluded(lineNumber, excluded),
                    $"filtering differs at line {lineNumber}");
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static LogModelFacade LoadWithModes(string path, LogMetaInformation meta,
        bool parallelParsing, bool parallelIndexing)
    {
        LineProcessor.ParallelParsingOverride = parallelParsing;
        ParsedBufferConsumer.ParallelIndexingOverride = parallelIndexing;
        try
        {
            return TestHelpers.LoadFile(path, meta);
        }
        finally
        {
            LineProcessor.ParallelParsingOverride = null;
            ParsedBufferConsumer.ParallelIndexingOverride = null;
        }
    }

    [TestMethod]
    public void LoaderHandlesPartialStreamReads()
    {
        var lines = Enumerable.Range(0, 500)
            .Select(i => $"2024-01-15 08:32:11.482 [INFO] line {i}\r\n");
        var data = Encoding.UTF8.GetBytes(string.Concat(lines));

        var consumer = new CountingConsumer();
        var loader = new LoaderImpl(4096, consumer);
        using var stream = new ChoppyStream(data, 7);
        loader.Load(stream, Encoding.UTF8.GetBytes("\r"), Encoding.UTF8.GetBytes("\n"), CancellationToken.None);

        Assert.AreEqual(500, consumer.LineCount);
        Assert.AreEqual(data.Length, consumer.TotalBytes);
    }

    [TestMethod]
    public void IndexDoesNotYieldRentedTail()
    {
        using var index = new LogGrokX.Data.Index.Index(3);
        var values = new[] { 1, 5, 9, 11, 42 };
        foreach (var value in values)
            index.Add(value);

        Assert.AreEqual(values.Length, index.Count);
        CollectionAssert.AreEqual(values, index.GetEnumerableFromValue(0).ToArray());
        CollectionAssert.AreEqual(new[] { 9, 11, 42 }, index.GetEnumerableFromValue(9).ToArray());
        CollectionAssert.AreEqual(Array.Empty<int>(), index.GetEnumerableFromValue(100).ToArray());
    }

    [TestMethod]
    public void StringPoolRetentionIsBounded()
    {
        var pool = new StringPool();
        const int size = 1024 * 1024;
        const int count = 256;

        var returned = new HashSet<string>(ReferenceComparer.Instance);
        for (var i = 0; i < count; i++)
        {
            var buffer = pool.Rent(size);
            returned.Add(buffer);
            pool.Return(buffer);
        }

        var reused = 0;
        for (var i = 0; i < count; i++)
        {
            if (returned.Contains(pool.Rent(size)))
                reused++;
        }

        // Retention is capped, so the pool cannot hand back every returned buffer.
        Assert.IsTrue(reused > 0, "pool should reuse buffers");
        Assert.IsTrue(reused < count, $"pool retained too many buffers: {reused}");
    }

    [TestMethod]
    public void TimestampFormatIsEquivalentToFormatString()
    {
        const string format = "yyyy-MM-dd HH:mm:ss.fff";
        var precompiled = TimestampFormat.Create(format);

        Assert.IsTrue(TimestampParser.TryGetTicks("2024-01-15 08:32:11.482", precompiled, out var fast));
        Assert.IsTrue(TimestampParser.TryGetTicks("2024-01-15 08:32:11.482", format, out var slow));
        Assert.AreEqual(slow, fast);

        Assert.IsTrue(TimestampParser.TryGetTicks("08:32:11", TimestampFormat.Create("HH:mm:ss"), out var timeOnly));
        Assert.AreEqual(new TimeSpan(8, 32, 11).Ticks, timeOnly);

        Assert.IsFalse(TimestampParser.TryGetTicks("not a time", precompiled, out _));
    }

    [DataTestMethod]
    [DataRow("simple text", "simple text")]
    [DataRow(@"error\: \d+", "error: ")]
    [DataRow(@"^\[WARN\] payload", "[WARN] payload")]
    [DataRow(@"abc.*defgh", "defgh")]
    [DataRow(@"colou?rful", "colo")]
    public void PrefilterExtractsRequiredLiteral(string pattern, string expected)
    {
        Assert.IsTrue(SearchPrefilter.TryGetRequiredLiteral(pattern, out var literal));
        Assert.AreEqual(expected, literal);
    }

    [DataTestMethod]
    [DataRow(@"foo|bar")]
    [DataRow(@"\d+")]
    [DataRow(@"(?i)abc")]
    [DataRow(@"a")]
    public void PrefilterRefusesPatternsWithoutRequiredLiteral(string pattern)
    {
        Assert.IsFalse(SearchPrefilter.TryGetRequiredLiteral(pattern, out _));
    }

    [TestMethod]
    public void PrefilterNeverRejectsMatchingData()
    {
        var patterns = new[] { "payload", @"code\: \d+", "ERROR", @"user-\w+" };
        var samples = new[]
        {
            "some payload here",
            "code: 42",
            "[ERROR] failed",
            "user-alpha logged in",
            "nothing interesting"
        };

        foreach (var pattern in patterns)
        {
            var regex = new Regex(pattern);
            var prefilter = SearchPrefilter.TryCreate(regex, Encoding.UTF8);
            if (prefilter == null)
                continue;

            foreach (var sample in samples.Where(s => regex.IsMatch(s)))
            {
                Assert.IsTrue(prefilter.MayContainMatch(Encoding.UTF8.GetBytes(sample)),
                    $"prefilter rejected matching line for pattern {pattern}");
            }
        }
    }

    [TestMethod]
    public void PrefilteredSearchFindsSameLinesAsPlainSearch()
    {
        var meta = TestHelpers.CreateMeta(LineRegex, new[] { "Level" }, "Time", "yyyy-MM-dd HH:mm:ss.fff");
        var builder = new StringBuilder();
        for (var i = 0; i < 5000; i++)
        {
            var level = i % 500 == 0 ? "ERROR" : "INFO";
            builder.Append($"2024-01-15 08:32:11.482 [{level}] payload {i}\r\n");
        }

        var path = TestHelpers.WriteTempFile(builder.ToString());
        try
        {
            var model = TestHelpers.LoadFile(path, meta);
            var (progress, _, lineIndex) = LogGrokX.Data.Search.Search.CreateSearchIndex(model,
                new Regex("payload 1500"), CancellationToken.None);
            progress.Completion.Wait(TimeSpan.FromSeconds(30));

            Assert.AreEqual(1, lineIndex.Count);
            Assert.AreEqual(1500, lineIndex.GetLine(0).sourceIndex);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class CountingConsumer : ILineDataConsumer
    {
        public int LineCount { get; private set; }

        public long TotalBytes { get; private set; }

        public void AddLineData(long offset, Span<byte> lineData) => LineCount++;

        public void CompleteAdding(long totalBytesRead) => TotalBytes = totalBytesRead;
    }

    private sealed class ReferenceComparer : IEqualityComparer<string>
    {
        public static readonly ReferenceComparer Instance = new();

        public bool Equals(string? x, string? y) => ReferenceEquals(x, y);

        public int GetHashCode(string obj) => obj.Length;
    }

    /// <summary>
    /// Returns at most <c>chunk</c> bytes per Read call, like network or
    /// decorated streams are allowed to do.
    /// </summary>
    private sealed class ChoppyStream : Stream
    {
        private readonly byte[] _data;
        private readonly int _chunk;
        private int _position;

        public ChoppyStream(byte[] data, int chunk)
        {
            _data = data;
            _chunk = chunk;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var available = Math.Min(Math.Min(_chunk, buffer.Length), _data.Length - _position);
            if (available <= 0)
                return 0;
            _data.AsSpan(_position, available).CopyTo(buffer);
            _position += available;
            return available;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _data.Length;

        public override long Position
        {
            get => _position;
            set => _position = (int)value;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            _position = origin switch
            {
                SeekOrigin.Begin => (int)offset,
                SeekOrigin.Current => _position + (int)offset,
                _ => _data.Length + (int)offset
            };
            return _position;
        }

        public override void Flush()
        {
        }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
