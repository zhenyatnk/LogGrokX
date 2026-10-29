using System.Collections.Generic;
using System.Linq;
using LogGrokX.Data.Symbols;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Data.Tests.Symbols;

[TestClass]
public class CppSyntaxTests
{
    private static List<(string Text, SourceTokenKind Kind)> Highlighted(params string[] lines)
    {
        var tokens = CppSyntax.Tokenize(lines);
        return lines.SelectMany((line, index) => tokens[index]
                .Where(token => token.Kind != SourceTokenKind.Text)
                .Select(token => (line.Substring(token.Start, token.Length), token.Kind)))
            .ToList();
    }

    [TestMethod]
    public void HighlightsKeywordsTypesNumbersAndStrings()
    {
        var tokens = Highlighted("static const int count = 0x1F + 1'000 + 1.5e-3f; auto s = L\"a\\\"b\"; char c = '\\'';");

        CollectionAssert.AreEqual(new (string, SourceTokenKind)[]
        {
            ("static", SourceTokenKind.Keyword), ("const", SourceTokenKind.Keyword), ("int", SourceTokenKind.Type),
            ("0x1F", SourceTokenKind.Number), ("1'000", SourceTokenKind.Number), ("1.5e-3f", SourceTokenKind.Number),
            ("auto", SourceTokenKind.Type), ("L\"a\\\"b\"", SourceTokenKind.String), ("char", SourceTokenKind.Type),
            ("'\\''", SourceTokenKind.String)
        }, tokens);
    }

    [TestMethod]
    public void HighlightsPreprocessorAndIncludes()
    {
        var tokens = Highlighted("#include <windows.h>", "  # define MAX 10", "#include \"local.h\"");

        CollectionAssert.AreEqual(new (string, SourceTokenKind)[]
        {
            ("#include", SourceTokenKind.Preprocessor), ("<windows.h>", SourceTokenKind.String),
            ("# define", SourceTokenKind.Preprocessor), ("10", SourceTokenKind.Number),
            ("#include", SourceTokenKind.Preprocessor), ("\"local.h\"", SourceTokenKind.String)
        }, tokens);
    }

    [TestMethod]
    public void CommentsContinueAcrossLines()
    {
        var tokens = Highlighted("int a; /* start", "still comment", "end */ return a; // tail");

        CollectionAssert.AreEqual(new (string, SourceTokenKind)[]
        {
            ("int", SourceTokenKind.Type), ("/* start", SourceTokenKind.Comment),
            ("still comment", SourceTokenKind.Comment),
            ("end */", SourceTokenKind.Comment), ("return", SourceTokenKind.Keyword), ("// tail", SourceTokenKind.Comment)
        }, tokens);
    }

    [TestMethod]
    public void RawStringsContinueAcrossLines()
    {
        var tokens = Highlighted("auto s = R\"x(first \")", "second)x\"; return 0;");

        CollectionAssert.AreEqual(new (string, SourceTokenKind)[]
        {
            ("auto", SourceTokenKind.Type), ("R\"x(first \")", SourceTokenKind.String),
            ("second)x\"", SourceTokenKind.String), ("return", SourceTokenKind.Keyword), ("0", SourceTokenKind.Number)
        }, tokens);
    }

    [TestMethod]
    public void TokensCoverWholeLine()
    {
        const string line = "for (int i = 0; i < n; ++i) { s += \"x\"; } // done";

        var tokens = CppSyntax.Tokenize([line])[0];

        Assert.AreEqual(line, string.Concat(tokens.Select(token => line.Substring(token.Start, token.Length))));
    }

    [TestMethod]
    [DataRow(@"C:\src\main.cpp", true)]
    [DataRow("lib/header.HPP", true)]
    [DataRow("file.h", true)]
    [DataRow("readme.txt", false)]
    [DataRow(null, false)]
    public void DetectsCppFiles(string? path, bool expected)
    {
        Assert.AreEqual(expected, CppSyntax.IsCppFile(path));
    }
}
