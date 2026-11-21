using System;
using System.Threading;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LogGrokX.Controls.TextRender;
using LogGrokX.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Tests;

[TestClass]
public class FoldingLayoutTests
{
    private static readonly Size Infinite = new(double.PositiveInfinity, double.PositiveInfinity);

    [TestMethod]
    public void MessageGutterMeasuresPlainAndCollapsibleTextWithSameOffset()
    {
        RunOnSta(true, () =>
        {
            using var plain = CreateLine("same");
            using var collapsible = CreateLine("same");
            var plainControl = CreateControl(true, (plain, false));
            var collapsibleControl = CreateControl(true, (collapsible, true));

            plainControl.Measure(Infinite);
            collapsibleControl.Measure(Infinite);

            Assert.AreEqual(plain.Size.Width + TextView.ExpanderMargin,
                plainControl.DesiredSize.Width);
            Assert.AreEqual(plainControl.DesiredSize.Width,
                collapsibleControl.DesiredSize.Width);
        });
    }

    [TestMethod]
    public void MessageGutterDoesNotChangeOtherColumns()
    {
        RunOnSta(true, () =>
        {
            using var messagePlain = CreateLine("same");
            using var messageCollapsible = CreateLine("same");
            using var otherPlain = CreateLine("same");
            using var otherCollapsible = CreateLine("same");
            var messagePlainControl = CreateControl(true, (messagePlain, false));
            var messageCollapsibleControl = CreateControl(true, (messageCollapsible, true));
            var otherPlainControl = CreateControl(false, (otherPlain, false));
            var otherCollapsibleControl = CreateControl(false, (otherCollapsible, true));

            messagePlainControl.Measure(Infinite);
            messageCollapsibleControl.Measure(Infinite);
            otherPlainControl.Measure(Infinite);
            otherCollapsibleControl.Measure(Infinite);

            Assert.AreEqual(messagePlain.Size.Width + TextView.ExpanderMargin,
                messagePlainControl.DesiredSize.Width);
            Assert.AreEqual(messagePlainControl.DesiredSize.Width,
                messageCollapsibleControl.DesiredSize.Width);
            Assert.AreEqual(otherPlain.Size.Width, otherPlainControl.DesiredSize.Width);
            Assert.AreEqual(otherCollapsible.Size.Width + TextView.ExpanderMargin,
                otherCollapsibleControl.DesiredSize.Width);
        });
    }

    [TestMethod]
    public void DisabledMessageGutterRestoresLegacyOffsets()
    {
        RunOnSta(false, () =>
        {
            using var plain = CreateLine("same");
            using var collapsible = CreateLine("same");
            var plainControl = CreateControl(true, (plain, false));
            var collapsibleControl = CreateControl(true, (collapsible, true));

            plainControl.Measure(Infinite);
            collapsibleControl.Measure(Infinite);

            Assert.AreEqual(plain.Size.Width, plainControl.DesiredSize.Width);
            Assert.AreEqual(collapsible.Size.Width + TextView.ExpanderMargin,
                collapsibleControl.DesiredSize.Width);
        });
    }

    [TestMethod]
    public void MessageGutterUsesSameHighlightAndHitTestOffsetForPlainAndCollapsibleText()
    {
        RunOnSta(true, () =>
        {
            using var plain = CreateLine("same");
            using var collapsible = CreateLine("same");
            var plainControl = CreateControl(true, (plain, false));
            var collapsibleControl = CreateControl(true, (collapsible, true));
            var regex = new Regex("same");

            var plainBounds = plainControl.GetHighlightGeometries(plainControl.TextLines, regex)!.Bounds;
            var collapsibleBounds = collapsibleControl.GetHighlightGeometries(
                collapsibleControl.TextLines, regex)!.Bounds;

            Assert.AreEqual(collapsibleBounds, plainBounds);
            Assert.AreEqual("same", plainControl.GetTermTextAtPoint(
                new Point(TextView.ExpanderMargin + 1, 1)));
        });
    }

    [TestMethod]
    public void UseFixedFoldingGutterIsInheritedAndDefaultsToFalse()
    {
        RunOnSta(true, () =>
        {
            var parent = new Grid();
            var child = new TextView();
            parent.Children.Add(child);

            Assert.IsFalse(TextView.GetUseFixedFoldingGutter(child));
            TextView.SetUseFixedFoldingGutter(parent, true);

            Assert.IsTrue(TextView.GetUseFixedFoldingGutter(child));
        });
    }

    [TestMethod]
    public void SettingChangeInvalidatesMeasureAndHighlightGeometry()
    {
        RunOnSta(false, () =>
        {
            var view = new TextView { TextModel = new TextModel(9, "plain") };
            TextView.SetUseFixedFoldingGutter(view, true);
            view.Measure(Infinite);
            Assert.IsTrue(view.IsMeasureValid);
            view.Arrange(new Rect(view.DesiredSize));
            var control = (TextControl)VisualTreeHelper.GetChild(view, 0);
            var regex = new Regex("plain");
            var initialBounds = control.GetHighlightGeometries(control.TextLines, regex)!.Bounds;

            FoldingLayoutOptions.Instance.UseMessageGutter = true;

            var isMeasureValid = view.IsMeasureValid;
            var updatedBounds = control.GetHighlightGeometries(control.TextLines, regex)!.Bounds;
            Assert.IsFalse(isMeasureValid);
            Assert.AreEqual(initialBounds.X + TextView.ExpanderMargin, updatedBounds.X, 0.001);
        });
    }

    private static TextControl CreateControl(
        params (GlyphLine glyphLine, bool isCollapsible)[] textLines) =>
        CreateControl(false, textLines);

    private static TextControl CreateControl(bool useFixedGutter,
        params (GlyphLine glyphLine, bool isCollapsible)[] textLines)
    {
        var textView = new TextView();
        TextView.SetUseFixedFoldingGutter(textView, useFixedGutter);
        return new TextControl(textView) { TextLines = textLines };
    }

    private static GlyphLine CreateLine(string text)
    {
        var typeface = new Typeface("Segoe UI");
        if (!typeface.TryGetGlyphTypeface(out var glyphTypeface))
            throw new InvalidOperationException("Segoe UI glyph typeface is not available.");
        return new GlyphLine(StringRange.FromString(text), glyphTypeface, 12, 1f, double.PositiveInfinity);
    }

    private static void RunOnSta(bool useMessageGutter, Action action)
    {
        ApplicationSettings.Instance();
        Exception error = null;
        var thread = new Thread(() =>
        {
            var previous = FoldingLayoutOptions.Instance.UseMessageGutter;
            try
            {
                FoldingLayoutOptions.Instance.UseMessageGutter = useMessageGutter;
                action();
            }
            catch (Exception e)
            {
                error = e;
            }
            finally
            {
                FoldingLayoutOptions.Instance.UseMessageGutter = previous;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null)
            throw error;
    }
}
