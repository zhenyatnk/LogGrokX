using System;
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
}