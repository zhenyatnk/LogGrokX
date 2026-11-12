using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Data.Tests;

[TestClass]
public class SearchTermExtractorTests
{
    [TestMethod]
    [DataRow("error in module", "error", "error")]
    [DataRow("error in module", "module", "module")]
    [DataRow("Event = 1244", "Event", "Event = 1244")]
    [DataRow("Event = 1244", "1244", "Event = 1244")]
    [DataRow("Event = 1244", "=", "Event = 1244")]
    [DataRow("Event=1244", "Event", "Event=1244")]
    [DataRow("Event=1244", "1244", "Event=1244")]
    [DataRow("Event   =    1244 next", "1244", "Event   =    1244")]
    [DataRow("Value: tests", "Value", "Value: tests")]
    [DataRow("Value: tests", "tests", "Value: tests")]
    [DataRow("Value:tests", "tests", "Value:tests")]
    [DataRow("Value :\ttests", "Value", "Value :\ttests")]
    [DataRow("a=1, b=2", "b", "b=2")]
    [DataRow("a=1, b=2", "1", "a=1")]
    [DataRow("user.name = john.doe.", "doe", "user.name = john.doe")]
    [DataRow("retry = -5", "5", "retry = -5")]
    [DataRow("open http://host/path", "http", "http")]
    [DataRow("Value: (tests)", "tests", "tests")]
    public void ExtractsTermUnderPosition(string text, string clicked, string expected)
    {
        var position = text.IndexOf(clicked, System.StringComparison.Ordinal) + clicked.Length / 2;

        Assert.AreEqual(expected, SearchTermExtractor.GetTerm(text, position));
    }

    [TestMethod]
    [DataRow("a  b", 2)]
    [DataRow("x = ", 2)]
    [DataRow("", 0)]
    public void ReturnsNullWithoutTerm(string text, int position)
    {
        Assert.IsNull(SearchTermExtractor.GetTerm(text, position));
    }

    [TestMethod]
    public void AppendsTermsWithWildcard()
    {
        var pattern = SearchTermExtractor.AppendToPattern(string.Empty, false, "Event = 1244");
        Assert.AreEqual("Event = 1244", pattern);

        pattern = SearchTermExtractor.AppendToPattern(pattern, true, "Value: a.b");
        Assert.AreEqual(@"Event = 1244.*Value: a\.b", pattern);
        Assert.IsTrue(Regex.IsMatch("x Event = 1244 y Value: a.b", pattern));
        Assert.IsFalse(Regex.IsMatch("Value: a.b Event = 1244", pattern));
    }

    [TestMethod]
    public void EscapesPlainTextPatternBeforeAppending()
    {
        Assert.AreEqual(@"a\+b.*c", SearchTermExtractor.AppendToPattern("a+b", false, "c"));
    }
}
