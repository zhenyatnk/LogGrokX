using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LogGrokX.Data;
using LogGrokX.Data.Index;
using LogGrokX.Data.Monikers;
using LogGrokX.Data.Virtualization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Data.Tests;

[TestClass]
public class DataUtilitiesTests
{
    [TestMethod]
    public void PooledListAddsGrowsAndIndexes()
    {
        using var list = new PooledList<int>(2);
        for (var i = 0; i < 300; i++)
            list.Add(i);

        Assert.AreEqual(300, list.Count);
        Assert.AreEqual(299, list[299]);
        list[0] = 42;
        Assert.AreEqual(42, list[0]);

        var copy = new int[300];
        list.CopyTo(copy, 0);
        Assert.AreEqual(42, copy[0]);
        Assert.AreEqual(299, copy[299]);
    }

    [TestMethod]
    public void PooledListEnumeratesAndClears()
    {
        using var list = new PooledList<int>();
        list.Add(1);
        list.Add(2);

        var values = new List<int>();
        foreach (var value in list)
            values.Add(value);
        CollectionAssert.AreEqual(new[] { 1, 2 }, values);

        Assert.AreEqual(1, list.IndexOf(2));
        Assert.AreEqual(-1, list.IndexOf(99));
        Assert.IsTrue(list.Contains(2));
        Assert.IsFalse(list.Contains(1));

        list.Clear();
        Assert.AreEqual(0, list.Count);
    }

    [TestMethod]
    public void PooledListAllocateSpanGrows()
    {
        using var list = new PooledList<int>(2);
        var span = list.AllocateSpan(10);
        span[9] = 7;

        Assert.IsTrue(list.Count >= 10);
        Assert.AreEqual(7, list[9]);
    }

    [TestMethod]
    public void PooledListNonGenericMembers()
    {
        using var list = new PooledList<int>();
        IList nonGeneric = list;

        Assert.AreEqual(0, nonGeneric.Add(5));
        nonGeneric.Add(6);
        Assert.AreEqual(2, list.Count);
        Assert.AreEqual(5, nonGeneric[0]);
        Assert.AreEqual(0, nonGeneric.IndexOf(5));
        Assert.IsTrue(nonGeneric.Contains(6));
        Assert.IsFalse(nonGeneric.Contains(5));
        Assert.IsFalse(nonGeneric.IsFixedSize);
        Assert.IsFalse(nonGeneric.IsReadOnly);
        Assert.IsFalse(nonGeneric.IsSynchronized);
        Assert.IsNotNull(nonGeneric.SyncRoot);
    }

    [TestMethod]
    public void PooledListUnsupportedMembersThrow()
    {
        using var list = new PooledList<int>();
        list.Add(1);

        Assert.Throws<NotSupportedException>(() => list.Remove(1));
        Assert.Throws<NotSupportedException>(() => list.Insert(0, 1));
        Assert.Throws<NotSupportedException>(() => list.RemoveAt(0));
        Assert.Throws<NotSupportedException>(() => { list[0] = 1; ((IList)list)[0] = 2; });
        Assert.Throws<NotSupportedException>(() => ((IList)list).Add("wrong"));
        Assert.Throws<NotSupportedException>(() => ((IList)list).Contains("wrong"));
        Assert.Throws<NotSupportedException>(() => ((IList)list).IndexOf("wrong"));
        Assert.Throws<NotSupportedException>(() => ((IList)list).Insert(0, 1));
        Assert.Throws<NotSupportedException>(() => ((IList)list).Remove(1));
        Assert.Throws<NotSupportedException>(() => ((IList)list).CopyTo(Array.Empty<int>(), 0));
    }

    [TestMethod]
    public void StringPoolRentsAndReturns()
    {
        var pool = new StringPool();
        var small = pool.Rent(10);
        Assert.AreEqual(32, small.Length);

        pool.Return(small);
        Assert.AreSame(small, pool.Rent(10));

        var large = pool.Rent(100);
        Assert.AreEqual(128, large.Length);

        var exact = pool.Rent(64);
        Assert.AreEqual(64, exact.Length);

        Assert.Throws<InvalidOperationException>(() => pool.Return("unknown-size-string"));
    }

    [TestMethod]
    public void StringRangeBasics()
    {
        var range = StringRange.FromString("abc");
        Assert.AreEqual(3, range.Length);
        Assert.AreEqual(3, range.End);
        Assert.AreEqual("abc", range.ToString());
        Assert.IsFalse(range.IsEmpty);
        Assert.AreEqual("bc", range.Span.Slice(1).ToString());

        Assert.IsTrue(StringRange.Empty.IsEmpty);

        var equal = StringRange.FromString("abc");
        Assert.AreEqual(range, equal);
        Assert.AreEqual(range.GetHashCode(), equal.GetHashCode());
        Assert.AreNotEqual(range, StringRange.FromString("abd"));
        Assert.IsFalse(range.Equals("abc"));
    }

    [TestMethod]
    public void StringTokenizerHandlesEdgeCases()
    {
        Assert.AreEqual(0, string.Empty.Tokenize().Count());

        var tokens = "\r\na\r\n\r\nb\n".Tokenize().Select(t => t.ToString()).ToList();
        CollectionAssert.AreEqual(new[] { "a", "b" }, tokens);

        var singleLine = StringRange.FromString("no newline here");
        Assert.IsTrue(singleLine.IsSingleLine());
        Assert.IsFalse(StringRange.FromString("has\r\nnewline").IsSingleLine());

        var offset = new StringRange { SourceString = "xxa\r\nbxx", Start = 2, Length = 4 };
        CollectionAssert.AreEqual(new[] { "a", "b" }, offset.Tokenize().Select(t => t.ToString()).ToList());
    }

    [TestMethod]
    public void ListExtensionsFindAndSearch()
    {
        var list = new List<int> { 1, 2, 3, 4, 5 };
        IReadOnlyList<int> readOnly = list;

        Assert.AreEqual(2, readOnly.IndexOf(3));
        Assert.AreEqual(-1, readOnly.IndexOf(99));

        Assert.AreEqual(2, readOnly.BinarySearch(3, (element, value) => element.CompareTo(value)));
        Assert.IsTrue(readOnly.BinarySearch(0, (element, value) => element.CompareTo(value)) < 0);
        Assert.AreEqual(2, readOnly.BinarySearch(0, list.Count, 3, (element, value) => element.CompareTo(value)));
    }

    [TestMethod]
    public void AlignRoundsUp()
    {
        Assert.AreEqual(4, Align.Get(4, 2));
        Assert.AreEqual(6, Align.Get(5, 2));
        Assert.AreEqual(6, Align.Get(5, 3));
    }

    [TestMethod]
    public void LogFormatExposesFieldsAndValidity()
    {
        var format = new LogFormat
        {
            Regex = @"^(?'Time'\d{2}:\d{2}:\d{2}) (?'Message'.*)$",
            IndexedFields = new[] { "Message" },
            TimeField = "Time",
            TimeFormat = "HH:mm:ss"
        };

        CollectionAssert.AreEqual(new[] { "Time", "Message" }, format.FieldNames);
        CollectionAssert.AreEqual(new[] { 1 }, format.IndexedFieldNumbers);
        Assert.IsTrue(format.IsCorrect());

        Assert.IsFalse(new LogFormat { Regex = "(" }.IsCorrect());
        Assert.IsFalse(new LogFormat { Regex = "abc", Transformations = new[] { "(" } }.IsCorrect());
    }

    [TestMethod]
    public void LogMetaInformationResolvesFields()
    {
        var plain = LogMetaInformation.CreateTextFileMetaInformation();
        Assert.IsFalse(plain.HasTime);
        Assert.AreEqual(1, plain.ComponentCount);

        var meta = TestHelpers.CreateMeta(@"^(?'Time'\d{2}:\d{2}:\d{2}) (?'Level'\w+) (?'Message'.*)$",
            new[] { "Level" }, "Time", "HH:mm:ss");

        Assert.IsTrue(meta.HasTime);
        Assert.AreEqual(0, meta.TimeFieldIndex);
        Assert.AreEqual(1, meta.TimeGroupNumber);
        Assert.AreEqual(0, meta.GetIndexedFieldIndexByName("Level"));
        Assert.AreEqual(-1, meta.GetIndexedFieldIndexByName("Message"));
        Assert.AreEqual(0, meta.GetIndexedFieldIndexByFieldIndex(1));
        Assert.AreEqual("Level", meta.GetFieldNameByIndexedFieldIndex(0));
        Assert.AreEqual(string.Empty, meta.GetFieldNameByIndexedFieldIndex(5));
        Assert.IsTrue(meta.IsFieldIndexed("Level"));
        Assert.IsFalse(meta.IsFieldIndexed("Message"));
        Assert.IsFalse(meta.IsFieldIndexed("Missing"));
    }

    [TestMethod]
    public void LogMetaInformationFindsTimeByName()
    {
        var meta = TestHelpers.CreateMeta(@"^(?'Time'\d{2}:\d{2}:\d{2}) (?'Message'.*)$",
            Array.Empty<string>(), timeField: null, timeFormat: null);

        Assert.IsTrue(meta.HasTime);
        Assert.AreEqual(0, meta.TimeFieldIndex);
    }

    [TestMethod]
    public void LogMetaInformationResolvesTimeFieldIgnoringCase()
    {
        var meta = TestHelpers.CreateMeta(@"^(?<time>\d{2}:\d{2}:\d{2})\|(?<Message>.*)$",
            Array.Empty<string>(), "Time", "HH:mm:ss");

        Assert.IsTrue(meta.HasTime);
        Assert.AreEqual(0, meta.TimeFieldIndex);
        Assert.AreEqual(1, meta.TimeGroupNumber);
    }

    [TestMethod]
    public void RegexParserParsesComponents()
    {
        var meta = TestHelpers.CreateMeta(@"^(?'A'\w+) (?'B'\w+) (?'C'.*)$");
        var parser = new RegexBasedLineParser(meta);

        var result = parser.Parse("alpha beta the payload");

        Assert.AreEqual(3, result.ComponentCount);
        var components = result.Get().ParsedLineComponents;
        Assert.AreEqual("alpha", components.GetComponent("alpha beta the payload".AsSpan(), 0).ToString());
        Assert.AreEqual("beta", components.GetComponent("alpha beta the payload".AsSpan(), 1).ToString());
        Assert.AreEqual("the payload", components.GetComponent("alpha beta the payload".AsSpan(), 2).ToString());
    }

    [TestMethod]
    public void RegexParserExtractsTimestamp()
    {
        var meta = TestHelpers.CreateMeta(@"^(?'Time'\d{2}:\d{2}:\d{2}) (?'Message'.*)$",
            Array.Empty<string>(), "Time", "HH:mm:ss");
        var parser = new RegexBasedLineParser(meta);
        var placeholder = new int[LineMetaInformation.GetSizeInts(meta.ComponentCount)];
        var lineMeta = new LineMetaInformation(placeholder.AsSpan(), meta.ComponentCount);

        var parsed = parser.TryParse("12:34:56 hello", 0, "12:34:56 hello".Length,
            lineMeta.ParsedLineComponents, out var ticks);

        Assert.IsTrue(parsed);
        Assert.AreEqual(new TimeSpan(12, 34, 56).Ticks, ticks);
    }

    [TestMethod]
    public void RegexParserFailsOnNonMatchingLine()
    {
        var meta = TestHelpers.CreateMeta(@"^(?'A'\w+)$");
        var parser = new RegexBasedLineParser(meta);
        var placeholder = new int[LineMetaInformation.GetSizeInts(meta.ComponentCount)];
        var lineMeta = new LineMetaInformation(placeholder.AsSpan(), meta.ComponentCount);

        Assert.IsFalse(parser.TryParse("has spaces", 0, "has spaces".Length,
            lineMeta.ParsedLineComponents, out _));
    }

    [TestMethod]
    public void LineIndexStoresAndFetchesLines()
    {
        var index = new LineIndex();
        index.Add(0);
        index.Add(10);
        index.Add(25);

        Assert.AreEqual(2, index.Count);
        Assert.IsFalse(index.IsFinished);

        index.Finish(5);
        Assert.AreEqual(3, index.Count);
        Assert.IsTrue(index.IsFinished);

        Assert.AreEqual((0L, 10), index.GetLine(0));
        Assert.AreEqual((10L, 15), index.GetLine(1));
        Assert.AreEqual((25L, 5), index.GetLine(2));

        var values = new (long, int)[3];
        index.Fetch(0, values);
        Assert.AreEqual((0L, 10), values[0]);
        Assert.AreEqual((25L, 5), values[2]);
    }

    [TestMethod]
    public async Task LineIndexFetchRangesYieldsFinishedRange()
    {
        var index = new LineIndex();
        for (var i = 0; i < 300; i++)
            index.Add(i * 10);
        index.Finish(10);

        var ranges = new List<(int, int)>();
        await foreach (var range in index.FetchRanges(CancellationToken.None))
            ranges.Add(range);

        CollectionAssert.AreEqual(new[] { (0, 300) }, ranges);
    }

    [TestMethod]
    public async Task LineIndexFetchRangesStopsWhenCancelledBeforeDelay()
    {
        var index = new LineIndex();
        index.Add(0);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var ranges = new List<(int, int)>();
        await foreach (var range in index.FetchRanges(cts.Token))
            ranges.Add(range);

        Assert.AreEqual(0, ranges.Count);
    }

    [TestMethod]
    public async Task LineIndexFetchRangesStopsWhenCancelledAfterFinished()
    {
        var index = new LineIndex();
        index.Add(0);
        index.Finish(10);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var ranges = new List<(int, int)>();
        await foreach (var range in index.FetchRanges(cts.Token))
            ranges.Add(range);

        Assert.AreEqual(0, ranges.Count);
    }

    [TestMethod]
    public void MaskedStreamRoundTripsThroughLogFile()
    {
        var original = Encoding.UTF8.GetBytes("secret log content");
        var masked = original.Select(b => (byte)(b ^ 0x55)).ToArray();
        var path = TestHelpers.WriteTempBytes(masked);
        try
        {
            var logFile = new LogFile(path, 0x55);
            using var stream = logFile.OpenForSequentialRead();
            var buffer = new byte[original.Length];
            var read = stream.Read(buffer, 0, buffer.Length);

            Assert.AreEqual(original.Length, read);
            CollectionAssert.AreEqual(original, buffer);

            using var direct = logFile.Open();
            Assert.AreEqual(masked.Length, direct.Length);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void MaskedStreamUnsupportedAndMetadata()
    {
        var path = TestHelpers.WriteTempBytes(new byte[] { 1, 2, 3 });
        try
        {
            var logFile = new LogFile(path, 0x10);
            using var stream = logFile.OpenForSequentialRead();

            Assert.IsTrue(stream.CanRead);
            Assert.IsTrue(stream.CanSeek);
            Assert.IsFalse(stream.CanWrite);
            Assert.AreEqual(3, stream.Length);

            stream.Position = 1;
            Assert.AreEqual(1, stream.Position);
            stream.Seek(0, SeekOrigin.Begin);
            Assert.AreEqual(0, stream.Position);

            stream.Flush();
            Assert.Throws<NotSupportedException>(() => stream.Write(new byte[1], 0, 1));
            Assert.Throws<NotSupportedException>(() => stream.SetLength(0));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void LogFileDetectsUtf8Bom()
    {
        var path = WriteWithPreamble(Encoding.UTF8, "hello world");
        try
        {
            var logFile = new LogFile(path, 0);
            Assert.AreEqual(Encoding.UTF8, logFile.Encoding);
            Assert.IsTrue(logFile.FileSize > 0);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void LogFileDetectsUnicodeBom()
    {
        var path = WriteWithPreamble(Encoding.Unicode, "hello world");
        try
        {
            Assert.AreEqual(Encoding.Unicode, new LogFile(path, 0).Encoding);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void LogFileDetectsBigEndianUnicodeBom()
    {
        var path = WriteWithPreamble(Encoding.BigEndianUnicode, "hello world");
        try
        {
            Assert.AreEqual(Encoding.BigEndianUnicode, new LogFile(path, 0).Encoding);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void LogFileDetectsUtf32Bom()
    {
        var path = WriteWithPreamble(Encoding.UTF32, "hello world");
        try
        {
            Assert.AreEqual(Encoding.UTF32, new LogFile(path, 0).Encoding);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void LogFileDetectsBomlessUnicodeByNulls()
    {
        var path = TestHelpers.WriteTempBytes(Encoding.Unicode.GetBytes(new string('a', 5000)));
        try
        {
            Assert.AreEqual(Encoding.Unicode, new LogFile(path, 0).Encoding);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void LogFileDetectsBomlessBigEndianByNulls()
    {
        var path = TestHelpers.WriteTempBytes(Encoding.BigEndianUnicode.GetBytes(new string('a', 5000)));
        try
        {
            Assert.AreEqual(Encoding.BigEndianUnicode, new LogFile(path, 0).Encoding);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void LogFileFallsBackToUtf8()
    {
        var path = TestHelpers.WriteTempBytes(Encoding.UTF8.GetBytes("plain ascii text with no bom"));
        try
        {
            Assert.AreEqual(Encoding.UTF8, new LogFile(path, 0).Encoding);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void VirtualListConvertsAndCaches()
    {
        var provider = new FakeItemProvider(1400);
        var list = new VirtualList<int, string>(provider, i => $"item-{i}");

        Assert.AreEqual(1400, list.Count);
        Assert.AreEqual("item-0", list[0]);
        Assert.AreEqual("item-0", list[0]);
        Assert.AreEqual("item-1399", list[1399]);

        var count = 0;
        foreach (var item in list)
        {
            Assert.IsTrue(item.StartsWith("item-"));
            count++;
        }

        Assert.AreEqual(1400, count);
    }

    [TestMethod]
    public void VirtualListExtendsCachedPageWhenCountGrows()
    {
        var provider = new FakeItemProvider(1);
        var list = new VirtualList<int, string>(provider, i => $"item-{i}");

        Assert.AreEqual("item-0", list[0]);

        provider.Count = 200;
        Assert.AreEqual("item-50", list[50]);
    }

    [TestMethod]
    public void VirtualListNonGenericAndUnsupportedMembers()
    {
        var provider = new FakeItemProvider(10);
        var list = new VirtualList<int, string>(provider, i => $"item-{i}");
        IList nonGeneric = list;

        Assert.AreEqual("item-1", nonGeneric[1]);
        Assert.IsFalse(nonGeneric.Contains("anything"));
        Assert.AreEqual(0, nonGeneric.IndexOf("anything"));
        Assert.IsTrue(list.IsReadOnly);
        Assert.IsFalse(list.IsFixedSize);
        Assert.IsFalse(list.IsSynchronized);
        Assert.Throws<NotSupportedException>(() => { _ = list.SyncRoot; });
        Assert.Throws<NotSupportedException>(() => nonGeneric.Add("x"));
        Assert.Throws<NotSupportedException>(() => nonGeneric.Remove("x"));
        Assert.Throws<NotSupportedException>(() => nonGeneric.Insert(0, "x"));
        Assert.Throws<NotSupportedException>(() => nonGeneric.RemoveAt(0));
        Assert.Throws<NotSupportedException>(() => nonGeneric.Clear());
        Assert.Throws<NotSupportedException>(() => nonGeneric.CopyTo(Array.Empty<string>(), 0));
        Assert.Throws<NotSupportedException>(() => list.Add("x"));
        Assert.Throws<NotSupportedException>(() => list.Contains("x"));
        Assert.Throws<NotSupportedException>(() => list.IndexOf("x"));
        Assert.Throws<NotSupportedException>(() => list.Insert(0, "x"));
        Assert.Throws<NotSupportedException>(() => list.RemoveAt(0));
        Assert.Throws<NotSupportedException>(() => list.Remove("x"));
        Assert.Throws<NotSupportedException>(() => list.CopyTo(Array.Empty<string>(), 0));
    }

    private static string WriteWithPreamble(Encoding encoding, string content)
    {
        var preamble = encoding.GetPreamble();
        var bytes = encoding.GetBytes(content);
        var all = new byte[preamble.Length + bytes.Length];
        preamble.CopyTo(all, 0);
        bytes.CopyTo(all, preamble.Length);
        return TestHelpers.WriteTempBytes(all);
    }

    private sealed class FakeItemProvider : IItemProvider<int>
    {
        public FakeItemProvider(int count) => Count = count;

        public int Count { get; set; }

        public void Fetch(int start, Span<int> values)
        {
            for (var i = 0; i < values.Length; i++)
                values[i] = start + i;
        }
    }
}
