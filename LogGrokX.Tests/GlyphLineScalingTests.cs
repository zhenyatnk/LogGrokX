using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using LogGrokX.Controls.TextRender;
using LogGrokX.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Tests
{
    [TestClass]
    public class GlyphLineScalingTests
    {
        [TestMethod]
        public void TextViewRestoresSizeWhenFontSizeReset()
        {
            RunOnSta(() =>
            {
                var textView = new TextView { TextModel = new TextModel(1, "Hello world") };
                var constraint = new Size(double.PositiveInfinity, double.PositiveInfinity);

                textView.FontSize = 24;
                textView.Measure(constraint);
                var big = textView.DesiredSize;

                textView.FontSize = 12;
                textView.Measure(constraint);
                var small = textView.DesiredSize;

                Assert.AreEqual(big.Width / 2, small.Width, 0.01);
                Assert.AreEqual(big.Height / 2, small.Height, 0.01);
            });
        }

        [TestMethod]
        public void AdvanceWidthScalesWithFontSizeRegardlessOfCacheOrder()
        {
            RunOnSta(() =>
            {
                var typeface = new Typeface("Segoe UI");
                if (!typeface.TryGetGlyphTypeface(out var glyphTypeface))
                    throw new InvalidOperationException("Segoe UI glyph typeface is not available.");

                using var small = new GlyphLine(StringRange.FromString("Wg1"), glyphTypeface, 12, 1f, double.PositiveInfinity);
                using var large = new GlyphLine(StringRange.FromString("Wg1"), glyphTypeface, 24, 1f, double.PositiveInfinity);

                Assert.AreEqual(small.Size.Width * 2, large.Size.Width, 0.001);
                Assert.AreEqual(small.Size.Height * 2, large.Size.Height, 0.001);
            });
        }

        [TestMethod]
        public void MeasureWidthMatchesGlyphLineAndScalesWithFontSize()
        {
            RunOnSta(() =>
            {
                var typeface = new Typeface("Segoe UI");
                if (!typeface.TryGetGlyphTypeface(out var glyphTypeface))
                    throw new InvalidOperationException("Segoe UI glyph typeface is not available.");

                const string text = "Wg1j";

                using var line = new GlyphLine(StringRange.FromString(text), glyphTypeface, 12, 1f, double.PositiveInfinity);
                Assert.AreEqual(line.Size.Width, GlyphLine.MeasureWidth(text.AsSpan(), glyphTypeface, 12), 0.001);
                Assert.AreEqual(GlyphLine.MeasureWidth(text.AsSpan(), glyphTypeface, 12) * 2,
                    GlyphLine.MeasureWidth(text.AsSpan(), glyphTypeface, 24), 0.001);
            });
        }

        [TestMethod]
        public void GlyphRunsAreMaterializedOnDemand()
        {
            RunOnSta(() =>
            {
                var typeface = new Typeface("Segoe UI");
                Assert.IsTrue(typeface.TryGetGlyphTypeface(out var glyphTypeface));
                using var line = new GlyphLine(StringRange.FromString("A日本語B"), glyphTypeface, 12, 1f,
                    double.PositiveInfinity);

                var size = line.Size;
                var advanceHeight = line.AdvanceHeight;
                Assert.AreEqual(0, line.MaterializedRunCount);
                Assert.IsFalse(line.HasWidthStorage);
                Assert.IsGreaterThan(0, size.Width);
                Assert.IsGreaterThan(0, line.RunCount);
                Assert.IsGreaterThan(0, line.MaterializedRunCount);
                Assert.IsTrue(line.HasWidthStorage);
                Assert.AreEqual(size, line.Size);
                Assert.AreEqual(advanceHeight, line.AdvanceHeight);
            });
        }

        [TestMethod]
        public void MaterializationPreservesMeasurementForTabsAndFiniteWidth()
        {
            RunOnSta(() =>
            {
                Assert.IsTrue(new Typeface("Consolas").TryGetGlyphTypeface(out var glyphTypeface));
                using var line = new GlyphLine(StringRange.FromString("A\t日B"), glyphTypeface, 12, 1.5f, 40);
                var size = line.Size;
                var advanceHeight = line.AdvanceHeight;

                Assert.IsFalse(line.HasWidthStorage);
                Assert.IsGreaterThan(0, line.GetNearestTextPosition(20));
                Assert.IsTrue(line.HasWidthStorage);
                Assert.AreEqual(size, line.Size);
                Assert.AreEqual(advanceHeight, line.AdvanceHeight);
            });
        }

        [TestMethod]
        public void MissingGlyphsUseFallbackTypeface()
        {
            RunOnSta(() =>
            {
                var typeface = new Typeface("Segoe UI");
                Assert.IsTrue(typeface.TryGetGlyphTypeface(out var glyphTypeface));
                Assert.IsFalse(glyphTypeface.CharacterToGlyphMap.ContainsKey('日'));
                const string text = "A日本語B";

                using var line = new GlyphLine(StringRange.FromString(text), glyphTypeface, 12, 1f, double.PositiveInfinity);

                Assert.IsGreaterThan(1, line.RunCount);
                Assert.AreEqual(line.Size.Width, GlyphLine.MeasureWidth(text.AsSpan(), glyphTypeface, 12), 0.001);
            });
        }

        [TestMethod]
        public void MixedFontRunsShareOneBaselineAndFitTheLineBox()
        {
            RunOnSta(() =>
            {
                Assert.IsTrue(new Typeface("Consolas").TryGetGlyphTypeface(out var typeface));
                const double size = 36;
                using var line = new GlyphLine(StringRange.FromString("A日本語B"), typeface, size, 1.5f, double.PositiveInfinity);
                var drawing = new DrawingGroup();
                using (var context = drawing.Open())
                    line.Render(new Point(), Brushes.Black, context);

                var runs = GlyphRuns(drawing).ToArray();
                Assert.IsGreaterThan(1, runs.Select(run => run.GlyphTypeface).Distinct().Count());
                Assert.IsFalse(runs.SelectMany(run => run.GlyphIndices).Contains((ushort)0));
                Assert.HasCount(1, runs.Select(run => run.BaselineOrigin.Y).Distinct());

                var ascent = runs.Max(run => Math.Round(run.GlyphTypeface.Baseline * size));
                var descent = runs.Max(run => run.GlyphTypeface.Height * size - Math.Round(run.GlyphTypeface.Baseline * size));
                Assert.IsGreaterThanOrEqualTo(ascent + descent - 0.001, line.Size.Height);
                Assert.IsGreaterThanOrEqualTo(line.Size.Height, line.AdvanceHeight);
            });
        }

        private static IEnumerable<GlyphRun> GlyphRuns(Drawing drawing) => drawing switch
        {
            GlyphRunDrawing run => [run.GlyphRun],
            DrawingGroup group => group.Children.SelectMany(GlyphRuns),
            _ => []
        };

        private static void RunOnSta(Action action)
        {
            Exception error = null;
            var thread = new Thread(() =>
            {
                var previous = FoldingLayoutOptions.Instance.UseMessageGutter;
                try
                {
                    FoldingLayoutOptions.Instance.UseMessageGutter = false;
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
}