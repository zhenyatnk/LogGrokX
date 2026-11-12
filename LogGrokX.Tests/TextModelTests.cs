using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Tests
{
    [TestClass]
    public class TextModelTests
    {
        private const string Json = "{\"a\":1,\"b\":[1,2]}";

        [TestMethod]
        public void CollapsedTailOfManyLinesIsCopiedFully()
        {
            var lines = Enumerable.Range(0, 50).Select(i => $"line {i}").ToArray();
            var model = new TextModel(1, string.Join("\n", lines));
            Assert.IsNotNull(model.CollapsibleRanges);

            var collapsed = model.CollapsibleRanges!.Select(r => r.start).ToHashSet();
            var displayed = model.GetDisplayedText(collapsed);

            Assert.IsFalse(displayed.Contains(">>>"), displayed);
            StringAssert.Contains(displayed, "line 49");
            StringAssert.Contains(displayed, "line 25");
        }

        [TestMethod]
        public void DisplayedTextOfLongSingleLineIsNotTruncated()
        {
            var source = new string('x', 200_000);
            var model = new TextModel(1, source);

            Assert.AreEqual(source, model.GetDisplayedText(null));
        }

        [TestMethod]
        public void DisplayedTextOfLongMultiLineIsNotTruncated()
        {
            var longLine = new string('y', 200_000);
            var source = "first\n" + longLine + "\nlast";
            var model = new TextModel(1, source);

            var displayed = model.GetDisplayedText(null);

            StringAssert.Contains(displayed, longLine);
            Assert.IsFalse(displayed.Contains("..."));
            StringAssert.StartsWith(displayed, "first");
            StringAssert.EndsWith(displayed, "last");
        }

        [TestMethod]
        public void ExpandedJsonIsIndented()
        {
            var model = new TextModel(1, Json);

            var displayed = model.GetDisplayedText(new HashSet<int>());

            StringAssert.Contains(displayed, "\"a\": 1");
            StringAssert.Contains(displayed, "\"b\": [");
        }

        [TestMethod]
        public void CollapsedOuterJsonIsInlined()
        {
            var model = new TextModel(1, Json);
            Assert.IsNotNull(model.CollapsibleRanges);

            var root = model.CollapsibleRanges!.OrderByDescending(r => r.length).First();

            var displayed = model.GetDisplayedText(new HashSet<int> { root.start });

            StringAssert.Contains(displayed, "\"a\": 1");
            StringAssert.Contains(displayed, "\"b\": [1,2]");
            Assert.IsFalse(displayed.Contains('\n'), $"Expected inline json but got: {displayed}");
        }

        [TestMethod]
        public void CollapsedInnerJsonIsInlined()
        {
            var model = new TextModel(1, Json);
            Assert.IsNotNull(model.CollapsibleRanges);

            var inner = model.CollapsibleRanges!.OrderBy(r => r.length).First();

            var displayed = model.GetDisplayedText(new HashSet<int> { inner.start });

            StringAssert.Contains(displayed, "\"b\": [1,2]");
            StringAssert.Contains(displayed, "\"a\": 1");
        }

        [TestMethod]
        public void WholeLineAndMessageComponentHaveSameCollapsibleStarts()
        {
            const string prefix = "2024-01-01 10:00:00 INF app ";
            const string json = "{\"a\":{\"b\":1,\"c\":2}}";
            var whole = new TextModel(1, prefix + json);
            var component = new TextModel(2, json);

            CollectionAssert.AreEqual(
                whole.CollapsibleRanges!.Select(r => r.start).ToList(),
                component.CollapsibleRanges!.Select(r => r.start).ToList());
        }

        [TestMethod]
        public void NullFoldingStateKeepsExpandedJson()
        {
            var model = new TextModel(1, Json);

            var displayed = model.GetDisplayedText(null);

            StringAssert.Contains(displayed, "\"a\": 1");
        }
        [TestMethod]
        [DataRow("{\"key\":\"value\"}", "\"key\": \"value\"", "\"key\":\"value\"")]
        [DataRow("{\"key\" : \"a  b\"}", "\"key\": \"a  b\"", "\"key\" : \"a  b\"")]
        [DataRow("{\"a\":1, /* keep  spaces */ \"b\":2}", "\"b\": 2", "\"b\":2")]
        public void OriginalJsonTextPreservesSourceWhitespace(string source, string displayedTerm, string expected)
        {
            AssertOriginalText(source, displayedTerm, expected);
        }

        [TestMethod]
        public void OriginalJsonTextUsesOccurrencePositionForDuplicateKeys()
        {
            AssertOriginalText("{\"a\":1,\"a\":2}", "\"a\": 1", "\"a\":1");
            AssertOriginalText("{\"a\":1,\"a\":2}", "\"a\": 2", "\"a\":2");
        }

        [TestMethod]
        public void OriginalJsonTextUsesCorrectInlineFragment()
        {
            AssertOriginalText("before {\"a\":1} middle {\"b\":2} after", "\"b\": 2", "\"b\":2");
        }

        [TestMethod]
        public void OriginalJsonTextSupportsCollapsedSubstitution()
        {
            var model = new TextModel(1, Json, true);
            var root = model.CollapsibleRanges!.OrderByDescending(r => r.length).First();
            var collapsed = model.GetCollapsedTextSubstitution(root.start).ToString();
            const string displayedTerm = "\"a\": 1";
            var start = collapsed.IndexOf(displayedTerm);

            Assert.IsTrue(model.TryGetOriginalText(root.start, start, displayedTerm.Length, true, out var original));
            Assert.AreEqual("\"a\":1", original);
        }

        [TestMethod]
        [DataRow("{\"a\":1,}")]
        [DataRow("{\"a\":\"\\u0061\"}")]
        public void OriginalJsonTextRejectsNonWhitespaceChanges(string source)
        {
            var model = new TextModel(1, source, true);
            const string displayedTerm = "\"a\": ";
            var line = model[1].ToString();
            var start = line.IndexOf(displayedTerm);
            Assert.IsTrue(start >= 0);

            Assert.IsFalse(model.TryGetOriginalText(1, start, line.TrimEnd().Length - start, false, out _));
        }

        [TestMethod]
        public void OriginalTextMappingRequiresJsonAndOptIn()
        {
            var disabled = new TextModel(1, Json);
            Assert.IsFalse(disabled.TryGetOriginalText(1, 2, 5, false, out _));

            var plain = new TextModel(1, "plain text", true);
            Assert.IsFalse(plain.TryGetOriginalText(0, 0, 5, false, out _));

            var xml = new TextModel(1, Xml, true);
            Assert.IsFalse(xml.TryGetOriginalText(0, 0, 5, false, out _));
        }

        [TestMethod]
        public void OriginalJsonHighlightProjectsRawWhitespace()
        {
            const string source = "{\"alpha\" : \"two  words\"}";
            var model = new TextModel(1, source, true);
            var regex = new Regex(Regex.Escape("\"alpha\" : \"two  words\""));
            var line = model[1].ToString();
            const string displayed = "\"alpha\": \"two  words\"";

            var range = model.GetOriginalHighlightRanges(1, false, regex).Single();

            Assert.AreEqual(line.IndexOf(displayed), range.start);
            Assert.AreEqual(displayed.Length, range.length);
        }

        [TestMethod]
        public void OriginalJsonHighlightProjectsMultilineAndCollapsedMatches()
        {
            const string source = "{\"alpha\":1,\"beta\":[2,3]}";
            var model = new TextModel(1, source, true);
            var regex = new Regex(Regex.Escape("\"alpha\":1,\"beta\":[2,3]"));

            var expandedCount = Enumerable.Range(0, model.Count)
                .Sum(i => model.GetOriginalHighlightRanges(i, false, regex).Count);
            Assert.IsTrue(expandedCount >= 3, $"Expanded ranges: {expandedCount}");

            var root = model.CollapsibleRanges!.OrderByDescending(r => r.length).First();
            var collapsedRanges = model.GetOriginalHighlightRanges(root.start, true, regex);
            Assert.IsTrue(collapsedRanges.Count >= 3, $"Collapsed ranges: {collapsedRanges.Count}");
            Assert.IsTrue(collapsedRanges.All(r => r.length > 0));
        }

        [TestMethod]
        public void OriginalJsonHighlightUsesCorrectInlineFragment()
        {
            var model = new TextModel(1, "before {\"alpha\":1} middle {\"beta\":2} after", true);
            var regex = new Regex(Regex.Escape("\"beta\":2"));

            var matches = Enumerable.Range(0, model.Count)
                .SelectMany(i => model.GetOriginalHighlightRanges(i, false, regex)
                    .Select(r => model[i].ToString().Substring(r.start, r.length)))
                .ToList();

            CollectionAssert.AreEqual(new[] { "\"beta\": 2" }, matches);
        }

        [TestMethod]
        public void OriginalJsonHighlightRejectsInvalidOrEmptyMappings()
        {
            foreach (var source in new[] { "{\"a\":1,}", "{\"a\":\"\\u0061\"}" })
            {
                var model = new TextModel(1, source, true);
                var regex = new Regex("1|a");
                Assert.IsTrue(Enumerable.Range(0, model.Count)
                    .All(i => model.GetOriginalHighlightRanges(i, false, regex).Count == 0));
            }

            var disabled = new TextModel(1, Json);
            Assert.AreEqual(0, disabled.GetOriginalHighlightRanges(1, false, new Regex("a")).Count);

            var whitespace = new TextModel(1, "{\"a\" : 1}", true);
            Assert.IsTrue(Enumerable.Range(0, whitespace.Count)
                .All(i => whitespace.GetOriginalHighlightRanges(i, false, new Regex(" +")).Count == 0));
            Assert.IsTrue(Enumerable.Range(0, whitespace.Count)
                .All(i => whitespace.GetOriginalHighlightRanges(i, false, new Regex("(?=1)")).Count == 0));
        }

        private static void AssertOriginalText(string source, string displayedTerm, string expected)
        {
            var model = new TextModel(1, source, true);
            for (var i = 0; i < model.Count; i++)
            {
                var line = model[i].ToString();
                var start = line.IndexOf(displayedTerm);
                if (start < 0) continue;

                Assert.IsTrue(model.TryGetOriginalText(i, start, displayedTerm.Length, false, out var original));
                Assert.AreEqual(expected, original);
                return;
            }

            Assert.Fail($"Displayed term not found: {displayedTerm}");
        }

        private const string Xml = "<root><item id=\"1\"><name>a</name></item><item id=\"2\" /></root>";

        [TestMethod]
        public void ExpandedXmlIsIndented()
        {
            var model = new TextModel(1, "request " + Xml);

            var displayed = model.GetDisplayedText(new HashSet<int>());

            StringAssert.StartsWith(displayed, "request <root>");
            StringAssert.Contains(displayed, "  <item id=\"1\">");
            StringAssert.Contains(displayed, "    <name>a</name>");
            StringAssert.Contains(displayed, "  <item id=\"2\" />");
        }

        [TestMethod]
        public void XmlHasCollapsibleRangesForElementsWithChildren()
        {
            var model = new TextModel(1, Xml);
            Assert.IsNotNull(model.CollapsibleRanges);

            CollectionAssert.AreEquivalent(
                new List<(int start, int length)> { (0, 6), (1, 3) },
                model.CollapsibleRanges);
        }

        [TestMethod]
        public void CollapsedOuterXmlIsInlined()
        {
            var model = new TextModel(1, Xml);
            Assert.IsNotNull(model.CollapsibleRanges);

            var root = model.CollapsibleRanges!.OrderByDescending(r => r.length).First();

            var displayed = model.GetDisplayedText(new HashSet<int> { root.start });

            Assert.AreEqual(Xml, displayed);
        }

        [TestMethod]
        public void JsonAndXmlInOneLineAreBothFormatted()
        {
            var model = new TextModel(1, "{\"a\":{\"b\":1}} " + Xml);
            Assert.IsNotNull(model.CollapsibleRanges);

            var displayed = model.GetDisplayedText(new HashSet<int>());

            StringAssert.Contains(displayed, "\"b\": 1");
            StringAssert.Contains(displayed, "    <name>a</name>");
        }

        [TestMethod]
        public void TextWithoutXmlStaysPlain()
        {
            var model = new TextModel(1, "List<int> a < b");

            Assert.IsNull(model.CollapsibleRanges);
            Assert.AreEqual("List<int> a < b", model.GetDisplayedText(null));
        }
    }
}
