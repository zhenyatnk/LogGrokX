using System;
using System.Windows.Media;
using LogGrokX.Controls.TextRender;
using LogGrokX.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Tests;

[TestClass]
public sealed class GlyphLineAllocationTests
{
    [STATestMethod]
    public void PlainAsciiLineStaysWithinAllocationBudget()
    {
        Assert.IsTrue(new Typeface("Consolas").TryGetGlyphTypeface(out var typeface));
        var text = StringRange.FromString(new string('A', 160));
        const int count = 256;
        const long budgetPerLine = 3072;
        MeasureAllocations(text, typeface, count);

        var bytesPerLine = MeasureAllocations(text, typeface, count) / count;
        Assert.IsLessThan(budgetPerLine, bytesPerLine, $"Allocated {bytesPerLine} bytes per line.");
    }

    private static long MeasureAllocations(StringRange text, GlyphTypeface typeface, int count)
    {
        var start = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < count; i++)
        {
            using var line = new GlyphLine(text, typeface, 15, 1f, double.PositiveInfinity);
        }
        return GC.GetAllocatedBytesForCurrentThread() - start;
    }
}
