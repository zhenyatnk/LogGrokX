using System;
using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Data.Tests;

[TestClass]
public class TimestampParserTests
{
    [TestMethod]
    public void ParsesTimeOnlyWithExplicitFormat()
    {
        var expected = new TimeSpan(0, 12, 34, 56, 789).Ticks;

        var parsed = TimestampParser.TryGetTicks("12:34:56.789", "HH:mm:ss.fff", out var ticks);

        Assert.IsTrue(parsed);
        Assert.AreEqual(expected, ticks);
    }

    [TestMethod]
    public void ParsesFullTimestampWithoutExplicitFormat()
    {
        var expected = new DateTime(2024, 1, 2, 3, 4, 5, 678).Ticks;

        var parsed = TimestampParser.TryGetTicks("2024-01-02 03:04:05.678", (string?)null, out var ticks);

        Assert.IsTrue(parsed);
        Assert.AreEqual(expected, ticks);
    }

    [TestMethod]
    public void ReturnsFalseForInvalidInput()
    {
        var parsed = TimestampParser.TryGetTicks("not a timestamp", "HH:mm:ss.fff", out var ticks);

        Assert.IsFalse(parsed);
        Assert.AreEqual(-1L, ticks);
    }

    [TestMethod]
    public void MatchesReferenceForCommonFormats()
    {
        var formats = new[]
        {
            "yyyy-MM-dd HH:mm:ss.fff",
            "yyyy-MM-dd HH:mm:ss",
            "HH:mm:ss.fff",
            "HH:mm:ss"
        };

        var inputs = new[]
        {
            "2024-01-02 03:04:05.678",
            "2024-01-02 03:04:05",
            "2024-12-31 23:59:59.999",
            "0001-01-01 00:00:00.000",
            "9999-12-31 23:59:59.999",
            "2024-02-29 12:00:00.001",
            "12:34:56.789",
            "12:34:56",
            "00:00:00.000",
            "23:59:59.999",
            "2023-02-29 12:00:00.001",
            "2024-13-01 00:00:00.000",
            "2024-00-10 00:00:00.000",
            "2024-01-32 00:00:00.000",
            "2024-01-02 24:00:00.000",
            "2024-01-02 03:60:05.678",
            "2024-01-02 03:04:60.678",
            "24:00:00.000",
            "12:60:56.789",
            "12:34:60.789",
            "2024/01/02 03:04:05.678",
            "2024-01-02T03:04:05.678",
            "2024-1-2 3:4:5.678",
            "not a timestamp",
            "2024-01-02 03:04:05.67",
            "2024-01-02 03:04:05.6789",
            "",
            " 2024-01-02 03:04:05.678"
        };

        foreach (var format in formats)
        {
            foreach (var input in inputs)
            {
                var expected = ReferenceTryGetTicks(input, format, out var expectedTicks);
                var actual = TimestampParser.TryGetTicks(input, format, out var actualTicks);

                Assert.AreEqual(expected, actual, $"result mismatch for '{input}' with format '{format}'.");
                Assert.AreEqual(expectedTicks, actualTicks, $"ticks mismatch for '{input}' with format '{format}'.");
            }
        }
    }

    private static bool ReferenceTryGetTicks(string text, string format, out long ticks)
    {
        ticks = -1;
        if (text.Length == 0)
            return false;

        if (!DateTime.TryParseExact(text, format, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var timestamp))
            return false;

        ticks = format.IndexOf('y') < 0 && format.IndexOf('M') < 0 && format.IndexOf('d') < 0
            ? timestamp.TimeOfDay.Ticks
            : timestamp.Ticks;
        return true;
    }

    [TestMethod]
    public void FormatsTimeOnly()
    {
        var ticks = new TimeSpan(0, 12, 34, 56, 789).Ticks;

        Assert.AreEqual("12:34:56.789", TimestampParser.Format(ticks));
    }

    [TestMethod]
    public void FormatsFullTimestamp()
    {
        var ticks = new DateTime(2024, 1, 2, 3, 4, 5, 678).Ticks;

        Assert.AreEqual("2024-01-02 03:04:05.678", TimestampParser.Format(ticks));
    }

    [TestMethod]
    public void FormatsEmptyForMissingTicks()
    {
        Assert.AreEqual(string.Empty, TimestampParser.Format(-1));
    }
}
