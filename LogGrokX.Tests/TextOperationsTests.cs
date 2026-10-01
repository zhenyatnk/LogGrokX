using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Tests
{
    [TestClass]
    public class TextOperationsTests
    {
        private const string ThreeLines = "Hello\r\nworld\r\n!";
        private const string OneLine = "Hello";
        
        [TestMethod]
        public void TestCountLinesEmpty()
        {
            Assert.AreEqual(0, TextOperations.CountLines(""));
        }

        [TestMethod]
        public void TestCountLinesOne()
        {
            Assert.AreEqual(1, TextOperations.CountLines(OneLine));
        }

        [TestMethod]
        public void TestCountLinesThree()
        {
            Assert.AreEqual(3, TextOperations.CountLines(ThreeLines));
        }

        [TestMethod]
        public void TestTrimLinesEmpty()
        {
            var (resultString, linesTrimmed) = TextOperations.TrimLines("", 3);
            Assert.AreEqual("", resultString);
            Assert.AreEqual(0, linesTrimmed);
        }
        
        [TestMethod]
        public void TestDontTrimLines()
        {
            var (resultString, linesTrimmed) = TextOperations.TrimLines(ThreeLines, 3);
            Assert.AreEqual(ThreeLines, resultString);
            Assert.AreEqual(0, linesTrimmed);
        }
        
        [TestMethod]
        public void TestTrimLinesZero()
        {
            var (resultString, linesTrimmed) = TextOperations.TrimLines(ThreeLines, 0);
            Assert.AreEqual("", resultString);
            Assert.AreEqual(3, linesTrimmed);
        }

        [TestMethod]
        public void TestTrimSomeLines()
        {
            var (resultString, linesTrimmed) = TextOperations.TrimLines(ThreeLines, 1);
            Assert.AreEqual("Hello\r\n", resultString);
            Assert.AreEqual(2, linesTrimmed);
        }
        
        [TestMethod]
        public void TestTrimSomeLines2()
        {
            var (resultString, linesTrimmed) = TextOperations.TrimLines(ThreeLines, 2);
            Assert.AreEqual("Hello\r\nworld\r\n", resultString);
            Assert.AreEqual(1, linesTrimmed);
        }

        [TestMethod]
        public void FormatInlineJsonAlignsJson()
        {
            var result = TextOperations.FormatInlineJson("prefix {\"a\":1,\"b\":[1,2]} suffix");

            StringAssert.Contains(result, "\"a\": 1");
            StringAssert.Contains(result, "\"b\": [");
        }

        [TestMethod]
        public void FormatInlineJsonLeavesNonJsonUntouched()
        {
            const string source = "no json here";

            Assert.AreEqual(source, TextOperations.FormatInlineJson(source));
        }
        [TestMethod]
        public void GetXmlRangesFindsInlineXml()
        {
            const string xml = "<root><item id=\"1\">a</item><item id=\"2\"/></root>";
            const string source = "prefix " + xml + " suffix";

            var ranges = TextOperations.GetXmlRanges(source).ToList();

            Assert.AreEqual(1, ranges.Count);
            Assert.AreEqual(xml, source.Substring(ranges[0].start, ranges[0].length));
        }

        [TestMethod]
        public void GetXmlRangesIncludesDeclaration()
        {
            const string xml = "<?xml version=\"1.0\" encoding=\"utf-8\"?><a><b>1</b></a>";

            var ranges = TextOperations.GetXmlRanges("request: " + xml).ToList();

            Assert.AreEqual(1, ranges.Count);
            Assert.AreEqual(9, ranges[0].start);
            Assert.AreEqual(xml.Length, ranges[0].length);
        }

        [TestMethod]
        [DataRow("List<int> values")]
        [DataRow("a < b and c > d")]
        [DataRow("<br/>")]
        [DataRow("<a>text</a>")]
        [DataRow("<a><b>unclosed</a>")]
        [DataRow("<a><b>1</b>")]
        public void GetXmlRangesIgnoresNonXml(string source)
        {
            Assert.IsFalse(TextOperations.GetXmlRanges(source).Any());
        }

        [TestMethod]
        public void FormatInlineXmlIndentsXml()
        {
            var result = TextOperations.FormatInlineXml("prefix <a><b x=\"1\">text</b><c/></a> suffix");

            StringAssert.StartsWith(result, "prefix <a>");
            StringAssert.Contains(result, Environment.NewLine + "  <b x=\"1\">text</b>");
            StringAssert.Contains(result, Environment.NewLine + "  <c />");
            StringAssert.EndsWith(result, "</a> suffix");
        }

        [TestMethod]
        public void FormatInlineXmlKeepsDeclarationAndCdata()
        {
            var result = TextOperations.FormatInlineXml(
                "<?xml version=\"1.0\"?><a><!-- note --><b><![CDATA[<raw>]]></b><c>1</c></a>");

            StringAssert.StartsWith(result, "<?xml version=\"1.0\"?>");
            StringAssert.Contains(result, "<![CDATA[<raw>]]>");
            StringAssert.Contains(result, "<!-- note -->");
            StringAssert.Contains(result, "  <c>1</c>");
        }

        [TestMethod]
        public void FormatInlineXmlLeavesNonXmlUntouched()
        {
            const string source = "no <xml> here";

            Assert.AreEqual(source, TextOperations.FormatInlineXml(source));
        }

        [TestMethod]
        public void GetStructuredRangesPrefersOuterRange()
        {
            const string source = "<a><b>{\"x\":1}</b></a> {\"y\":\"<c><d/></c>\"}";

            var ranges = TextOperations.GetStructuredRanges(source);

            Assert.AreEqual(2, ranges.Count);
            Assert.AreEqual(StructuredTextKind.Xml, ranges[0].kind);
            Assert.AreEqual(0, ranges[0].start);
            Assert.AreEqual(StructuredTextKind.Json, ranges[1].kind);
        }

        [TestMethod]
        public void GetXmlElementRangesReturnsNestedElements()
        {
            const string xml = "<a>\n  <b>\n    <c />\n  </b>\n</a>";

            var elements = TextOperations.GetXmlElementRanges(xml.AsSpan());

            Assert.AreEqual(2, elements.Count);
            CollectionAssert.Contains(elements, (0, xml.LastIndexOf("</a>", StringComparison.Ordinal)));
            CollectionAssert.Contains(elements, (xml.IndexOf("<b>", StringComparison.Ordinal),
                xml.IndexOf("</b>", StringComparison.Ordinal)));
        }
        [TestMethod]
        public void GetXmlRangesFindsXmlInsideUnclosedTags()
        {
            const string xml = "<x a=\"1>2\"><y>1</y></x>";
            const string source = "<html><body><p>text<br>" + xml;

            var ranges = TextOperations.GetXmlRanges(source).ToList();

            Assert.AreEqual(1, ranges.Count);
            Assert.AreEqual(xml, source.Substring(ranges[0].start, ranges[0].length));
        }

        [TestMethod]
        public void GetXmlRangesIgnoresManyUnclosedTags()
        {
            var source = string.Concat(Enumerable.Repeat("<a>", 5000)) + "</b>";

            Assert.IsFalse(TextOperations.GetXmlRanges(source).Any());
        }
    }
}