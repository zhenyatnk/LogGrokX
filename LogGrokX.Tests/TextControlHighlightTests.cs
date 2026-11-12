using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Text.RegularExpressions;
using LogGrokX.Controls.TextRender;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Tests;

[TestClass]
public class TextControlHighlightTests
{
    [TestMethod]
    public void SourceMatchAppearsInFormattedJsonAndDeduplicatesDirectMatch()
    {
        RunOnSta(() =>
        {
            const string source = "{\"alpha\" : \"two  words\"}";
            const string displayed = "\"alpha\": \"two  words\"";
            var textView = CreateTextView(source);
            var lineIndex = FindLine(textView, displayed);
            var control = (TextControl)VisualTreeHelper.GetChild(textView, 0);
            var line = control.TextLines![lineIndex].glyphLine.Text.ToString();
            var expectedStart = line.IndexOf(displayed, StringComparison.Ordinal);

            var rawRanges = textView.GetHighlightRanges(lineIndex, line,
                new Regex(Regex.Escape("\"alpha\" : \"two  words\"")));
            Assert.AreEqual(1, rawRanges.Count);
            Assert.AreEqual((expectedStart, displayed.Length, false), rawRanges.Single());

            var formattedRanges = textView.GetHighlightRanges(lineIndex, line,
                new Regex(Regex.Escape(displayed)));
            Assert.AreEqual(1, formattedRanges.Count);
            Assert.AreEqual((expectedStart, displayed.Length, false), formattedRanges.Single());
        });
    }

    [TestMethod]
    public void WildcardPartsAreClassifiedInFormattedXml()
    {
        RunOnSta(() =>
        {
            var textView = CreateTextView("<root><item id = '1'><name>a</name></item></root>");
            var control = (TextControl)VisualTreeHelper.GetChild(textView, 0);
            var regex = new Regex("<item.*<name>a</name>");

            var ranges = control.TextLines!.SelectMany((textLine, i) =>
                    textView.GetHighlightRanges(i, textLine.glyphLine.Text.ToString(), regex)
                        .Select(r => (text: textLine.glyphLine.Text.ToString().Substring(r.start, r.length),
                            r.isWildcard)))
                .ToList();

            Assert.IsTrue(ranges.Any(r => !r.isWildcard && r.text.Contains("name")));
            Assert.IsTrue(ranges.Any(r => r.isWildcard));
        });
    }

    [TestMethod]
    public void WildcardPartsAreClassifiedInFormattedJson()
    {
        RunOnSta(() =>
        {
            var textView = CreateTextView("{\"alpha\":1,\"beta\":2}");
            var control = (TextControl)VisualTreeHelper.GetChild(textView, 0);
            var regex = new Regex("\"alpha\":1.*\"beta\":2");

            var ranges = control.TextLines!.SelectMany((textLine, i) =>
                    textView.GetHighlightRanges(i, textLine.glyphLine.Text.ToString(), regex)
                        .Select(r => (text: textLine.glyphLine.Text.ToString().Substring(r.start, r.length),
                            r.isWildcard)))
                .ToList();

            Assert.IsTrue(ranges.Any(r => !r.isWildcard && r.text.Contains("alpha")));
            Assert.IsTrue(ranges.Any(r => !r.isWildcard && r.text.Contains("beta")));
            Assert.IsTrue(ranges.Any(r => r.isWildcard));
        });
    }

    [TestMethod]
    public void MatchCrossingPlainTextAndJsonAppearsInFormattedLines()
    {
        RunOnSta(() =>
        {
            const string source = "prefix = demo_settings; data = {\"section\":{\"run\":true},\"target\":{\"feature\":{\"value\":true,\"mandatory\":true,\"locked\":false}}}";
            var textView = CreateTextView(source);
            var control = (TextControl)VisualTreeHelper.GetChild(textView, 0);
            var regex = new Regex("demo_settings.*\"target\":\\{\"feature\":\\{\"value\":true,\"mandatory\":true,\"locked\":false\\}");

            var matches = control.TextLines!.SelectMany((textLine, i) =>
                    textView.GetHighlightRanges(i, textLine.glyphLine.Text.ToString(), regex)
                        .Select(r => textLine.glyphLine.Text.ToString().Substring(r.start, r.length)))
                .ToList();

            Assert.IsTrue(matches.Any(m => m.Contains("demo_settings")), string.Join(" | ", matches));
            Assert.IsTrue(matches.Any(m => m.Contains("\"target\": {")), string.Join(" | ", matches));
            Assert.IsTrue(matches.Any(m => m.Contains("\"value\": true")), string.Join(" | ", matches));
        });
    }

    [TestMethod]
    public void TextLinesChangeInvalidatesGeometryWithSameFormattedText()
    {
        RunOnSta(() =>
        {
            var textView = CreateTextView("{\"alpha\":1}");
            var control = (TextControl)VisualTreeHelper.GetChild(textView, 0);
            var regex = new Regex(Regex.Escape("\"alpha\" : 1"));

            var initial = (GeometryGroup)control.GetHighlightGeometries(control.TextLines, regex);
            Assert.AreEqual(0, initial!.Children.Count);

            textView.TextModel = new TextModel(2, "{\"alpha\" : 1}", true);
            textView.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            var updated = (GeometryGroup)control.GetHighlightGeometries(control.TextLines, regex);
            Assert.IsTrue(updated!.Children.Count > 0);
        });
    }

    [TestMethod]
    public void PlainTextKeepsDirectRegexHighlight()
    {
        RunOnSta(() =>
        {
            var textView = new TextView { TextModel = new TextModel(1, "plain alpha", true) };

            var ranges = textView.GetHighlightRanges(0, "plain alpha", new Regex("alpha"));

            Assert.AreEqual(1, ranges.Count);
            Assert.AreEqual((6, 5, false), ranges.Single());
        });
    }

    private static TextView CreateTextView(string source)
    {
        var textView = new TextView { TextModel = new TextModel(1, source, true) };
        textView.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return textView;
    }

    private static int FindLine(TextView textView, string displayed)
    {
        var control = (TextControl)VisualTreeHelper.GetChild(textView, 0);
        for (var i = 0; i < control.TextLines!.Count; i++)
            if (control.TextLines[i].glyphLine.Text.ToString().Contains(displayed))
                return i;
        throw new InvalidOperationException($"Line not found: {displayed}");
    }

    private static void RunOnSta(Action action)
    {
        Exception error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                error = e;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null)
            throw error;
    }
}
