using System;
using LogGrokX.Data.Monikers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Data.Tests;

[TestClass]
public class LogFormatEdgeTests
{
    [TestMethod]
    public void IsCorrectAcceptsValidTransformations()
    {
        var format = new LogFormat
        {
            Regex = @"(?'A'\w+)",
            Transformations = new[] { @"(?<Base64Decode>.+)" }
        };

        Assert.IsTrue(format.IsCorrect());
    }
}

[TestClass]
public class LogMetaInformationEdgeTests
{
    [TestMethod]
    public void ExposesTransformationsAndXorMask()
    {
        var format = new LogFormat
        {
            Regex = @"^(?'A'\w+)$",
            Transformations = new[] { "x" },
            XorMask = 0x2A
        };

        var meta = new LogMetaInformation(format);

        Assert.AreEqual(0x2A, meta.XorMask);
        CollectionAssert.AreEqual(new[] { "x" }, meta.Transformations);
    }
}

[TestClass]
public class RegexBasedLineParserEdgeTests
{
    [TestMethod]
    public void ParseThrowsWhenLineDoesNotMatch()
    {
        var parser = new RegexBasedLineParser(TestHelpers.CreateMeta(@"^(?'A'\w+)$"));

        Assert.Throws<InvalidOperationException>(() => parser.Parse("has spaces"));
    }

    [TestMethod]
    public void TryParseHandlesMissingOptionalGroup()
    {
        var meta = TestHelpers.CreateMeta(@"^(?'A'\w+)(?: (?'B'\w+))?$");
        var parser = new RegexBasedLineParser(meta);
        var placeholder = new int[LineMetaInformation.GetSizeInts(meta.ComponentCount)];
        var lineMeta = new LineMetaInformation(placeholder.AsSpan(), meta.ComponentCount);

        Assert.IsTrue(parser.TryParse("alpha", 0, "alpha".Length, lineMeta.ParsedLineComponents, out _));
        Assert.AreEqual(0, lineMeta.ParsedLineComponents.ComponentLength(1));
    }

    [TestMethod]
    [DataRow(false, "", "")]
    [DataRow(true, "", "")]
    [DataRow(true, "Thread", "HH:mm:ss.fff")]
    public void ClassicParserMatchesRegexCapturesAndTime(bool onlyIndexed, string timeField, string timeFormat)
    {
        const string pattern =
            @"^(?<Time>\d{4}-\d{2}-\d{2}\s[^\s]+)\s+(?<Level>[^\s]+)\s+(?<Thread>[^\s]+)\s+(?<Component>[^\s]+)\s+(?<Message>.*)";
        string[] indexedFields = ["Component", "Message", "Time", "Level", "Thread"];
        var parser = CreateParser(pattern);
        var reference = CreateParser($"(?:{pattern})");
        string[] lines =
        [
            "2024-01-02 03:04:05.678 INF Thread-01 Core ordinary message\r\n",
            "2024-01-02 03:04:05 INF 01:02:03.004 Core ordinary message",
            "2024-01-02\t3:04:05.67\u00A0INF  \tThread-01\u2003Core\u0085лог",
            "2024-01-02 03:04:05.678 INF Thread-01 Core \t\r\n",
            "2024-01-02 03:04:05.678 INF Thread-01 Core message\r\n  continuation\n",
            "2024-01-02 03:04:05.678 INF Thread-01 Core\r\n  continuation\n",
            "２０２４-０１-０２ 03:04:05.678 INF Thread-01 Core unicode digits",
            "2024-01-02 03:04:05.678 INF Thread-01 Core",
            "2024-01-02 03:04:05.678 INF Thread-01",
            "not a Classic line"
        ];

        foreach (var line in lines)
        {
            const string prefix = "outside the match ";
            var input = prefix + line + " outside the match";
            var expectedParts = new int[10];
            var parts = new int[10];
            var expected = reference.TryParse(input, prefix.Length, line.Length,
                new ParsedLineComponents(expectedParts), out var expectedTicks);
            var actual = parser.TryParse(input, prefix.Length, line.Length,
                new ParsedLineComponents(parts), out var ticks);

            Assert.AreEqual(expected, actual, line);
            Assert.AreEqual(expectedTicks, ticks, line);
            CollectionAssert.AreEqual(expectedParts, parts, line);
        }

        RegexBasedLineParser CreateParser(string regex) => new(new LogMetaInformation(new LogFormat
        {
            Regex = regex,
            IndexedFields = indexedFields,
            TimeField = timeField,
            TimeFormat = timeFormat
        }), onlyIndexed);
    }
}
