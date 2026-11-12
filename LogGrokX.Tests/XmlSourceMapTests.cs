using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Tests;

[TestClass]
public class XmlSourceMapTests
{
    [TestMethod]
    public void TagsAttributesAndTextPreserveOriginalWhitespace()
    {
        const string source = "<root><item id = '1'><name> a  b </name><empty/></item></root>";
        const string formatted = "<root>\n  <item id=\"1\">\n    <name> a  b </name>\n    <empty />\n  </item>\n</root>";

        AssertMaps(source, formatted, "id=\"1\"", "id = '1'");
        AssertMaps(source, formatted, "<name> a  b </name>", "<name> a  b </name>");
        AssertMaps(source, formatted, "<empty />", "<empty/>");
    }

    [TestMethod]
    public void MixedContentKeepsMeaningfulSpaces()
    {
        const string source = "<root>Hello  <b>world</b> again </root>";
        AssertMaps(source, source, "Hello  <b>world</b> again ", "Hello  <b>world</b> again ");
    }

    [TestMethod]
    public void CommentsAndCdataAreComparedExactly()
    {
        const string source = "<root><!-- a  b --><a><![CDATA[x  y <raw>&]]></a></root>";
        const string formatted = "<root>\n  <!-- a  b -->\n  <a><![CDATA[x  y <raw>&]]></a>\n</root>";

        AssertMaps(source, formatted, "<!-- a  b -->", "<!-- a  b -->");
        AssertMaps(source, formatted, "<![CDATA[x  y <raw>&]]>", "<![CDATA[x  y <raw>&]]>");
    }

    [TestMethod]
    public void DeclarationAndProcessingInstructionAllowSyntaxWhitespaceChanges()
    {
        const string declarationSource = "<?xml version = '1.0' encoding = 'utf-8'?><root><a/></root>";
        const string declarationFormatted = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<root>\n  <a />\n</root>";
        AssertMaps(declarationSource, declarationFormatted, "version=\"1.0\"", "version = '1.0'");

        const string piSource = "<?work   alpha  beta?><root><a/></root>";
        const string piFormatted = "<?work alpha  beta?>\n<root>\n  <a />\n</root>";
        AssertMaps(piSource, piFormatted, "alpha  beta", "alpha  beta");
    }

    [TestMethod]
    public void LogicalNewlinesAreAligned()
    {
        const string source = "<root>\n  <a>x</a>\n</root>";
        const string formatted = "<root>\r\n  <a>x</a>\r\n</root>";
        AssertMaps(source, formatted, "<a>x</a>", "<a>x</a>");
    }

    [TestMethod]
    public void UnchangedEntityMapsButNormalizedEntityFailsClosed()
    {
        AssertMaps("<root><a>&amp;</a></root>", "<root>\n  <a>&amp;</a>\n</root>", "&amp;", "&amp;");

        Assert.IsFalse(XmlSourceMap.TryCreate("<root><a>&apos;</a></root>",
            "<root>\n  <a>'</a>\n</root>", out _));
        Assert.IsFalse(XmlSourceMap.TryCreate("<root><a>&#65;</a></root>",
            "<root>\n  <a>A</a>\n</root>", out _));
    }

    private static void AssertMaps(string source, string formatted, string formattedTerm, string expected)
    {
        Assert.IsTrue(XmlSourceMap.TryCreate(source, formatted, out var map));
        var start = formatted.IndexOf(formattedTerm, StringComparison.Ordinal);
        Assert.IsTrue(start >= 0, formattedTerm);
        Assert.IsTrue(map!.TryGetSubstring(start, formattedTerm.Length, out var original));
        Assert.AreEqual(expected, original);
    }
}
