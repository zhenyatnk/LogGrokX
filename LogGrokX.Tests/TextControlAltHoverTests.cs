using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using LogGrokX.Controls.TextRender;
using LogGrokX.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Tests;

[TestClass]
public class TextControlAltHoverTests
{
    [TestMethod]
    public void AltHoverHighlightsPairFromKeyAndSeparatorButOnlyValueFromValue()
    {
        RunOnSta(() =>
        {
            const string text = "prefix key =\tvalue suffix";
            const int start = 7;
            const int length = 11;
            using var line = CreateLine(text);
            var control = CreateControl(false, line);
            var expected = line.GetTextBounds(new Point(), start, length);

            foreach (var position in new[] { start, text.IndexOf('=') })
            {
                control.UpdateAltHover(GetCharacterPoint(line, position), ModifierKeys.Alt);
                Assert.AreEqual(expected, control.AltHoverBounds!.Value);
            }

            var valueStart = text.IndexOf("value", StringComparison.Ordinal);
            control.UpdateAltHover(GetCharacterPoint(line, valueStart), ModifierKeys.Alt);
            Assert.AreEqual(line.GetTextBounds(new Point(), valueStart, "value".Length),
                control.AltHoverBounds!.Value);
        });
    }

    [TestMethod]
    public void AltHoverHighlightsOnlyQuotedValueAndWindowsPath()
    {
        RunOnSta(() =>
        {
            foreach (var (text, clicked, expected) in new[]
                     {
                         ("artifact = 'payload_42.bin'", "payload_42", "'payload_42.bin'"),
                         (@"volumePath: \\?\Volume{12345678-abcd-4321-9876-0123456789ab}\", "Volume",
                             @"\\?\Volume{12345678-abcd-4321-9876-0123456789ab}\"),
                         (@"deviceName: \Device\SyntheticVolume7", "SyntheticVolume7", @"\Device\SyntheticVolume7")
                     })
            {
                using var line = CreateLine(text);
                var control = CreateControl(false, line);
                var position = text.IndexOf(clicked, StringComparison.Ordinal);

                control.UpdateAltHover(GetCharacterPoint(line, position), ModifierKeys.Alt);

                Assert.AreEqual(line.GetTextBounds(new Point(), text.IndexOf(expected, StringComparison.Ordinal),
                        expected.Length), control.AltHoverBounds!.Value);
            }
        });
    }

    [TestMethod]
    public void AltHoverHighlightsBracketContents()
    {
        RunOnSta(() =>
        {
            const string content = "alpha::beta::gamma::delta::GetTargetPath";
            foreach (var (text, expected) in new[]
                     {
                         ("[" + content + "] Processing item", content),
                         ("<alpha::beta>", "alpha::beta"),
                         ("(alpha beta)", "alpha beta")
                     })
            {
                using var line = CreateLine(text);
                var control = CreateControl(false, line);

                control.UpdateAltHover(GetCharacterPoint(line, 0), ModifierKeys.Alt);

                Assert.AreEqual(line.GetTextBounds(new Point(), 1, expected.Length),
                    control.AltHoverBounds!.Value);
            }
        });
    }

    [TestMethod]
    public void AltHoverHighlightsFactoryNameAndHexProviderKey()
    {
        RunOnSta(() =>
        {
            const string text = "Building provider factory='acme_demo.inventory.SampleProvider', providerKey=0x1234abcd";
            const string nameValue = "'acme_demo.inventory.SampleProvider'";
            using var line = CreateLine(text);
            var control = CreateControl(false, line);

            control.UpdateAltHover(GetCharacterPoint(line, 0), ModifierKeys.Alt);
            Assert.AreEqual(line.GetTextBounds(new Point(), 0, "Building".Length),
                control.AltHoverBounds!.Value);

            var nameStart = text.IndexOf(nameValue, StringComparison.Ordinal);
            control.UpdateAltHover(GetCharacterPoint(line,
                text.IndexOf("SampleProvider", StringComparison.Ordinal)), ModifierKeys.Alt);
            Assert.AreEqual(line.GetTextBounds(new Point(), nameStart, nameValue.Length),
                control.AltHoverBounds!.Value);

            var keyStart = text.IndexOf("0x1234abcd", StringComparison.Ordinal);
            control.UpdateAltHover(GetCharacterPoint(line, keyStart), ModifierKeys.Alt);
            Assert.AreEqual(line.GetTextBounds(new Point(), keyStart, "0x1234abcd".Length),
                control.AltHoverBounds!.Value);
        });
    }

    [TestMethod]
    public void AltHoverExtractsTermsFromSyntheticFailureLine()
    {
        RunOnSta(() =>
        {
            const string text = "10:11:12.013\t0x7f\tDBG\tdemo\tCan't dispatch request iface=0x1234abcd, providerKey=0x00000000, hostId=0x00000000, accessPointId=0x00000000, caller={placeholder}. 0xdeadbeef (Can't locate provider)";
            using var line = CreateLine(text);
            var control = CreateControl(false, line);
            var start = text.IndexOf("Can't", StringComparison.Ordinal);
            var expected = line.GetTextBounds(new Point(), start, "Can't".Length);

            control.UpdateAltHover(GetCharacterPoint(line, start + 1), ModifierKeys.Alt);
            Assert.AreEqual(expected, control.AltHoverBounds!.Value);
            control.UpdateAltHover(GetCharacterPoint(line, start + 3), ModifierKeys.Alt);
            Assert.AreEqual(expected, control.AltHoverBounds!.Value);

            var callerStart = text.IndexOf("{placeholder}", StringComparison.Ordinal);
            control.UpdateAltHover(GetCharacterPoint(line,
                text.IndexOf("placeholder", StringComparison.Ordinal)), ModifierKeys.Alt);
            Assert.AreEqual(line.GetTextBounds(new Point(), callerStart, "{placeholder}".Length),
                control.AltHoverBounds!.Value);
        });
    }

    [TestMethod]
    public void AltClickUsesOriginalJsonTextWhileHoverUsesFormattedBounds()
    {
        RunOnSta(() =>
        {
            const string source = "{\"key\" : \"a  b\"}";
            const string displayedTerm = "\"key\": \"a  b\"";
            var textView = new TextView { TextModel = new TextModel(1, source, true) };
            textView.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var control = (TextControl)VisualTreeHelper.GetChild(textView, 0);
            var lineIndex = 0;
            while (!control.TextLines![lineIndex].glyphLine.Text.ToString().Contains(displayedTerm))
                lineIndex++;
            var line = control.TextLines[lineIndex].glyphLine;
            var start = line.Text.ToString().IndexOf(displayedTerm, StringComparison.Ordinal);
            var verticalOffset = 0.0;
            for (var i = 0; i < lineIndex; i++)
                verticalOffset += control.TextLines[i].glyphLine.AdvanceHeight;
            var horizontalOffset = control.TextLines[lineIndex].isCollapsible ? TextView.ExpanderMargin : 0;
            var point = GetCharacterPoint(line, start + 1, horizontalOffset, verticalOffset);
            Assert.AreEqual(displayedTerm, SearchTermExtractor.GetTerm(line.Text.ToString(), start + 1));

            control.UpdateAltHover(point, ModifierKeys.Alt);

            Assert.AreEqual(line.GetTextBounds(new Point(horizontalOffset, verticalOffset), start, displayedTerm.Length),
                control.AltHoverBounds!.Value);
            Assert.AreEqual("\"key\" : \"a  b\"", control.GetTermTextAtPoint(point));

            const string valueTerm = "\"a  b\"";
            var valueStart = line.Text.ToString().IndexOf(valueTerm, StringComparison.Ordinal);
            var valuePoint = GetCharacterPoint(line, valueStart + 1, horizontalOffset, verticalOffset);
            control.UpdateAltHover(valuePoint, ModifierKeys.Alt);
            Assert.AreEqual(line.GetTextBounds(new Point(horizontalOffset, verticalOffset), valueStart, valueTerm.Length),
                control.AltHoverBounds!.Value);
            Assert.AreEqual(valueTerm, control.GetTermTextAtPoint(valuePoint));

            using var plainLine = CreateLine("plain");
            var plainControl = CreateControl(false, plainLine);
            Assert.AreEqual("plain", plainControl.GetTermTextAtPoint(GetCharacterPoint(plainLine, 0)));
        });
    }

    [TestMethod]
    public void AltHoverTracksLinesAndCollapsibleOffset()
    {
        RunOnSta(() =>
        {
            using var first = CreateLine("first");
            using var second = CreateLine("second");
            var control = CreateControl(true, first, second);

            control.UpdateAltHover(GetCharacterPoint(second, 1, TextView.ExpanderMargin, first.AdvanceHeight),
                ModifierKeys.Alt);
            var secondExpected = second.GetTextBounds(
                new Point(TextView.ExpanderMargin, first.AdvanceHeight), 0, second.Text.Length);
            Assert.AreEqual(secondExpected, control.AltHoverBounds!.Value);

            control.UpdateAltHover(GetCharacterPoint(first, 2, TextView.ExpanderMargin), ModifierKeys.Alt);
            var firstExpected = first.GetTextBounds(
                new Point(TextView.ExpanderMargin, 0), 0, first.Text.Length);
            Assert.AreEqual(firstExpected, control.AltHoverBounds!.Value);
        });
    }

    [TestMethod]
    public void AltHoverRequiresOnlyAltAndTextUnderPointer()
    {
        RunOnSta(() =>
        {
            using var line = CreateLine("one  two");
            var control = CreateControl(false, line);
            var point = GetCharacterPoint(line, 1);

            control.UpdateAltHover(point, ModifierKeys.None);
            Assert.IsNull(control.AltHoverBounds);

            control.UpdateAltHover(point, ModifierKeys.Alt);
            Assert.IsNotNull(control.AltHoverBounds);
            control.UpdateAltHover(point, ModifierKeys.Alt | ModifierKeys.Control);
            Assert.IsNull(control.AltHoverBounds);

            control.UpdateAltHover(point, ModifierKeys.Alt);
            control.UpdateAltHover(point, ModifierKeys.Alt | ModifierKeys.Shift);
            Assert.IsNull(control.AltHoverBounds);

            control.UpdateAltHover(GetCharacterPoint(line, 4), ModifierKeys.Alt);
            Assert.IsNull(control.AltHoverBounds);
            control.UpdateAltHover(new Point(-1, 1), ModifierKeys.Alt);
            Assert.IsNull(control.AltHoverBounds);
            control.UpdateAltHover(new Point(line.Size.Width + 1, 1), ModifierKeys.Alt);
            Assert.IsNull(control.AltHoverBounds);
        });
    }

    [TestMethod]
    public void ReplacingTextLinesClearsAltHoverWithoutChangingSelection()
    {
        RunOnSta(() =>
        {
            using var line = CreateLine("first");
            using var replacement = CreateLine("second");
            var control = CreateControl(false, line);
            line.SetSelection(1, 2);
            control.UpdateAltHover(GetCharacterPoint(line, 0), ModifierKeys.Alt);
            Assert.IsNotNull(control.AltHoverBounds);

            control.TextLines = new List<(GlyphLine glyphLine, bool isCollapsible)> { (replacement, false) };

            Assert.IsNull(control.AltHoverBounds);
            Assert.AreEqual((1, 2), line.Selection!.Value);
        });
    }

    private static TextControl CreateControl(bool isCollapsible, params GlyphLine[] lines)
    {
        var textLines = new List<(GlyphLine glyphLine, bool isCollapsible)>();
        foreach (var line in lines)
            textLines.Add((line, isCollapsible));
        return new TextControl(new TextView()) { TextLines = textLines };
    }

    private static GlyphLine CreateLine(string text)
    {
        var typeface = new Typeface("Segoe UI");
        if (!typeface.TryGetGlyphTypeface(out var glyphTypeface))
            throw new InvalidOperationException("Segoe UI glyph typeface is not available.");
        return new GlyphLine(StringRange.FromString(text), glyphTypeface, 12, 1f, double.PositiveInfinity);
    }

    private static Point GetCharacterPoint(GlyphLine line, int position,
        double horizontalOffset = 0, double verticalOffset = 0)
    {
        var bounds = line.GetTextBounds(new Point(horizontalOffset, verticalOffset), position, 1);
        return new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
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
