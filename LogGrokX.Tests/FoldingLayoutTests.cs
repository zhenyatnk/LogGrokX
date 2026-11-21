using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
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

    [TestMethod]
    public void PinColumnModeMarksOnlyRootLineAsNotRequiringInlineMargin()
    {
        RunOnSta(FoldingLayoutMode.RootInPinColumn, () =>
        {
            var state = new TextViewSharedFoldingState();
            state.ExpandAll();
            var view = new TextView();
            TextView.SetSharedFoldingState(view, state);
            view.TextModel = new TextModel(7, "{\"a\":{\"b\":1}}");

            view.Measure(Infinite);
            var control = (TextControl)VisualTreeHelper.GetChild(view, 0);

            Assert.IsFalse(control.TextLines![0].isCollapsible);
            Assert.IsTrue(control.TextLines.Skip(1).Any(line => line.isCollapsible));

            view.Arrange(new Rect(view.DesiredSize));
            var outlineCount = Enumerable.Range(0, VisualTreeHelper.GetChildrenCount(view))
                .Select(index => VisualTreeHelper.GetChild(view, index))
                .OfType<OutlineExpander>()
                .Count();
            Assert.AreEqual(view.TextModel.CollapsibleRanges!.Count * 2 - 1, outlineCount);
        });
    }

    [TestMethod]
    public void LayoutSwitchInvalidatesMeasureAndHighlightGeometryAndHidesRootExpander()
    {
        RunOnSta(FoldingLayoutMode.RootInPinColumn, () =>
        {
            var state = new TextViewSharedFoldingState();
            state.ExpandAll();
            var model = new TextModel(9, "{\"a\":{\"b\":1}}");
            var view = new TextView { TextModel = model };
            TextView.SetSharedFoldingState(view, state);
            var expander = new RootFoldingExpander { TextModel = model, SharedFoldingState = state };
            view.Measure(Infinite);
            var control = (TextControl)VisualTreeHelper.GetChild(view, 0);
            var regex = new Regex("\\{");
            var initialBounds = control.GetHighlightGeometries(control.TextLines, regex)!.Bounds;
            Assert.IsTrue(view.IsMeasureValid);
            Assert.AreEqual(Visibility.Visible, expander.Visibility);

            FoldingLayoutOptions.Instance.Current = FoldingLayoutMode.FixedGutter;

            var isMeasureValid = view.IsMeasureValid;
            var updatedBounds = control.GetHighlightGeometries(control.TextLines, regex)!.Bounds;
            Assert.IsFalse(isMeasureValid);
            Assert.AreEqual(Visibility.Collapsed, expander.Visibility);
            Assert.AreEqual(initialBounds.X + TextView.ExpanderMargin, updatedBounds.X, 0.001);
        });
    }

    [TestMethod]
    public void MainWindowViewModelSwitchesRootLayoutAndNotifies()
    {
        RunOnSta(FoldingLayoutMode.FixedGutter, () =>
        {
            using var viewModel = TestServices.CreateMain(ApplicationSettings.Instance());
            var changed = new List<string>();
            ((INotifyPropertyChanged)viewModel).PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? string.Empty);

            Assert.IsFalse(viewModel.IsRootFoldingInPinColumn);
            viewModel.IsRootFoldingInPinColumn = true;

            Assert.AreEqual(FoldingLayoutMode.RootInPinColumn, FoldingLayoutOptions.Instance.Current);
            Assert.IsTrue(viewModel.IsRootFoldingInPinColumn);
            CollectionAssert.Contains(changed, nameof(MainWindowViewModel.IsRootFoldingInPinColumn));

            viewModel.IsRootFoldingInPinColumn = false;
            Assert.AreEqual(FoldingLayoutMode.FixedGutter, FoldingLayoutOptions.Instance.Current);
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
