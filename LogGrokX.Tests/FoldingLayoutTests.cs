using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
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
    public void FixedGutterMeasuresPlainAndCollapsibleTextWithSameOffset()
    {
        RunOnSta(FoldingLayoutMode.FixedGutter, () =>
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
    public void FixedGutterAppliesOnlyToMessageTextView()
    {
        RunOnSta(FoldingLayoutMode.FixedGutter, () =>
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
    public void PlainMultilineFoldingHasNoStructuredRootLine()
    {
        var model = new TextModel(8, string.Join("\n", Enumerable.Range(0, 22)));

        Assert.IsNotNull(model.CollapsibleRanges);
        Assert.IsNull(model.RootFoldingLine);
    }

    [TestMethod]
    public void XmlRootFoldingLineSelectsOutermostElement()
    {
        var model = new TextModel(10, "<root><item><name>first</name></item><item><name>second</name></item></root>");
        var outermost = model.CollapsibleRanges!.OrderByDescending(range => range.length).First();

        Assert.IsTrue(model.CollapsibleRanges.Count >= 3);
        Assert.AreEqual(0, outermost.start);
        Assert.AreEqual(outermost.start, model.RootFoldingLine);
    }

    [TestMethod]
    public void RootFoldingLineSelectsOutermostRangeAcrossManyNestedSiblings()
    {
        const int siblingCount = 40;
        const int depth = 6;
        string CreateValue(int index)
        {
            var value = index.ToString();
            for (var level = 0; level < depth; level++)
                value = $"{{\"level{level}\":{value}}}";
            return value;
        }

        var source = "{" + string.Join(",", Enumerable.Range(0, siblingCount)
            .Select(index => $"\"item{index}\":{CreateValue(index)}")) + "}";
        var model = new TextModel(11, source);
        var outermost = model.CollapsibleRanges!.OrderByDescending(range => range.length).First();

        Assert.IsTrue(model.CollapsibleRanges.Count > siblingCount * depth);
        Assert.AreEqual(model.Count, outermost.length);
        Assert.AreEqual(outermost.start, model.RootFoldingLine);
    }

    [TestMethod]
    public void RootFoldingLineSelectsFirstOfMultipleOutermostJsonAndXmlFragments()
    {
        var model = new TextModel(12,
            "{\"first\":{\"nested\":1}} between <second><child>2</child></second> and {\"third\":[3]}");

        Assert.IsNotNull(model.CollapsibleRanges);
        Assert.AreEqual(model.CollapsibleRanges.Min(range => range.start), model.RootFoldingLine);
        Assert.IsTrue(model.CollapsibleRanges.Count >= 4);
    }

    [TestMethod]
    public void FixedGutterUsesSameHighlightAndHitTestOffsetForPlainAndCollapsibleText()
    {
        RunOnSta(FoldingLayoutMode.FixedGutter, () =>
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
    public void RootExpanderFollowsSharedRootStateChanges()
    {
        RunOnSta(FoldingLayoutMode.RootInPreviousColumn, () =>
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
    public void ExternalRootModeMarksOnlyRootLineAsNotRequiringInlineMargin()
    {
        RunOnSta(FoldingLayoutMode.RootInPreviousColumn, () =>
        {
            var state = new TextViewSharedFoldingState();
            state.ExpandAll();
            var view = new TextView();
            TextView.SetHasExternalRootExpander(view, true);
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
    public void NonExternalRootKeepsInlineMarginAndExpanderInPreviousColumnMode()
    {
        RunOnSta(FoldingLayoutMode.RootInPreviousColumn, () =>
        {
            var state = new TextViewSharedFoldingState();
            state.ExpandAll();
            var view = new TextView();
            TextView.SetSharedFoldingState(view, state);
            view.TextModel = new TextModel(8, "{\"a\":{\"b\":1}}");

            view.Measure(Infinite);
            var control = (TextControl)VisualTreeHelper.GetChild(view, 0);

            Assert.IsTrue(control.TextLines![0].isCollapsible);
            Assert.IsTrue(control.TextLines.Skip(1).Any(line => line.isCollapsible));

            view.Arrange(new Rect(view.DesiredSize));
            var outlineCount = Enumerable.Range(0, VisualTreeHelper.GetChildrenCount(view))
                .Select(index => VisualTreeHelper.GetChild(view, index))
                .OfType<OutlineExpander>()
                .Count();
            Assert.AreEqual(view.TextModel.CollapsibleRanges!.Count * 2, outlineCount);
        });
    }

    [TestMethod]
    public void HasExternalRootExpanderIsInheritedAndDefaultsToFalse()
    {
        RunOnSta(FoldingLayoutMode.RootInPreviousColumn, () =>
        {
            var parent = new Grid();
            var child = new TextView();
            parent.Children.Add(child);

            Assert.IsFalse(TextView.GetHasExternalRootExpander(child));
            TextView.SetHasExternalRootExpander(parent, true);

            Assert.IsTrue(TextView.GetHasExternalRootExpander(child));
        });
    }

    [TestMethod]
    public void LayoutSwitchInvalidatesMeasureAndHighlightGeometryAndHidesRootExpander()
    {
        RunOnSta(FoldingLayoutMode.RootInPreviousColumn, () =>
        {
            var state = new TextViewSharedFoldingState();
            state.ExpandAll();
            var model = new TextModel(9, "{\"a\":{\"b\":1}}");
            var view = new TextView { TextModel = model };
            TextView.SetHasExternalRootExpander(view, true);
            TextView.SetUseFixedFoldingGutter(view, true);
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

            Assert.IsFalse(viewModel.IsRootFoldingInPreviousColumn);
            viewModel.IsRootFoldingInPreviousColumn = true;

            Assert.AreEqual(FoldingLayoutMode.RootInPreviousColumn, FoldingLayoutOptions.Instance.Current);
            Assert.IsTrue(viewModel.IsRootFoldingInPreviousColumn);
            CollectionAssert.Contains(changed, nameof(MainWindowViewModel.IsRootFoldingInPreviousColumn));

            viewModel.IsRootFoldingInPreviousColumn = false;
            Assert.AreEqual(FoldingLayoutMode.FixedGutter, FoldingLayoutOptions.Instance.Current);
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
