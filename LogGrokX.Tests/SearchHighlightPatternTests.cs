using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Tests;

[TestClass]
public class SearchHighlightPatternTests
{
    [TestMethod]
    public void ExplicitPartsAreBrightAndWildcardContentIsDim()
    {
        const string text = "alpha middle beta";
        var regex = new Regex("alpha.*beta");
        var match = regex.Match(text);

        var ranges = new SearchHighlightPattern(regex).GetRanges(match);

        CollectionAssert.AreEqual(new[] { "alpha", "beta" }, ranges
            .Where(r => !r.isWildcard).Select(r => text.Substring(r.start, r.length)).ToArray());
        CollectionAssert.AreEqual(new[] { " middle " }, ranges
            .Where(r => r.isWildcard).Select(r => text.Substring(r.start, r.length)).ToArray());
    }

    [TestMethod]
    public void MultipleAndLazyWildcardsAreClassifiedIndependently()
    {
        const string text = "a12b34b";
        var regex = new Regex("a.*?b.*?b");
        var match = regex.Match(text);

        var ranges = new SearchHighlightPattern(regex).GetRanges(match);

        CollectionAssert.AreEqual(new[] { "a", "b", "b" }, ranges
            .Where(r => !r.isWildcard).Select(r => text.Substring(r.start, r.length)).ToArray());
        CollectionAssert.AreEqual(new[] { "12", "34" }, ranges
            .Where(r => r.isWildcard).Select(r => text.Substring(r.start, r.length)).ToArray());
    }

    [TestMethod]
    public void EmptyWildcardDoesNotCreateDimRange()
    {
        var regex = new Regex("a.*b");
        var ranges = new SearchHighlightPattern(regex).GetRanges(regex.Match("ab"));

        Assert.AreEqual(1, ranges.Count);
        Assert.AreEqual((0, 2, false), ranges.Single());
    }

    [TestMethod]
    [DataRow(@"a\.\*b", "a.*b")]
    [DataRow("[.*]", "*")]
    [DataRow("(?=a.*b)a.*b", "a12b")]
    [DataRow("(a).*\\1", "a12a")]
    public void UnsupportedOrLiteralPatternsStayBright(string pattern, string text)
    {
        var regex = new Regex(pattern);
        var match = regex.Match(text);

        var ranges = new SearchHighlightPattern(regex).GetRanges(match);

        Assert.AreEqual(1, ranges.Count);
        Assert.AreEqual((0, match.Length, false), ranges.Single());
    }

    [TestMethod]
    public void NamedBackreferenceDoesNotPreventClassification()
    {
        const string text = "a12a";
        var regex = new Regex("(?<first>a).*\\k<first>");
        var match = regex.Match(text);

        var ranges = new SearchHighlightPattern(regex).GetRanges(match);

        CollectionAssert.AreEqual(new[] { "a", "a" }, ranges
            .Where(r => !r.isWildcard).Select(r => text.Substring(r.start, r.length)).ToArray());
        var wildcard = ranges.Single(r => r.isWildcard);
        Assert.AreEqual("12", text.Substring(wildcard.start, wildcard.length));
    }

    [TestMethod]
    public void PatternWithoutWildcardKeepsWholeMatchBright()
    {
        var regex = new Regex("alpha");
        var ranges = new SearchHighlightPattern(regex).GetRanges(regex.Match("alpha"));

        Assert.AreEqual((0, 5, false), ranges.Single());
    }
}
