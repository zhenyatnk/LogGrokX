using System;
using System.Linq;
using System.Threading;
using System.Windows;
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
    public void FixedGutterMeasuresPlainAndCollapsibleTextWithSameOffset()
    {
        RunOnSta(FoldingLayoutMode.FixedGutter, () =>
        {
            using var plain = CreateLine("same");
            using var collapsible = CreateLine("same");
            var plainControl = CreateControl((plain, false));
            var collapsibleControl = CreateControl((collapsible, true));

            plainControl.Measure(Infinite);
            collapsibleControl.Measure(Infinite);

            Assert.AreEqual(plain.Size.Width + TextView.ExpanderMargin,
                plainControl.DesiredSize.Width);
            Assert.AreEqual(plainControl.DesiredSize.Width,
                collapsibleControl.DesiredSize.Width);
        });
    }

    [TestMethod]
    public void PlainMultilineFoldingHasNoStructuredRootLine()
    {
        var model = new TextModel(8, string.Join("\n", Enumerable.Range(0, 22)));

        Assert.IsNotNull(model.CollapsibleRanges);
        Assert.IsNull(model.RootFoldingLine);
    }

    [TestMethod]
    public void RootExpanderFollowsSharedRootStateChanges()
    {
        RunOnSta(FoldingLayoutMode.RootInPinColumn, () =>
        {
            var model = new TextModel(42, "{\"a\":{\"b\":1,\"c\":2}}");
            var state = new TextViewSharedFoldingState();
            var expander = new RootFoldingExpander
            {
                TextModel = model,
                SharedFoldingState = state
            };
            var changedCount = 0;
            state.Changed += (_, _) => changedCount++;

            Assert.AreEqual(OutlineExpanderState.Collapsed, expander.State);

            state.ToggleRoot(model);

            Assert.AreEqual(OutlineExpanderState.ExpandedUpper, expander.State);
            Assert.AreEqual(1, changedCount);
        });
    }

    private static TextControl CreateControl(
        params (GlyphLine glyphLine, bool isCollapsible)[] textLines)
    {
        return new TextControl(new TextView()) { TextLines = textLines };
    }

    private static GlyphLine CreateLine(string text)
    {
        var typeface = new Typeface("Segoe UI");
        if (!typeface.TryGetGlyphTypeface(out var glyphTypeface))
            throw new InvalidOperationException("Segoe UI glyph typeface is not available.");
        return new GlyphLine(StringRange.FromString(text), glyphTypeface, 12, 1f, double.PositiveInfinity);
    }

    private static void RunOnSta(FoldingLayoutMode mode, Action action)
    {
        Exception error = null;
        var thread = new Thread(() =>
        {
            var previous = FoldingLayoutOptions.Instance.Current;
            try
            {
                FoldingLayoutOptions.Instance.Current = mode;
                action();
            }
            catch (Exception e)
            {
                error = e;
            }
            finally
            {
                FoldingLayoutOptions.Instance.Current = previous;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null)
            throw error;
    }
}
