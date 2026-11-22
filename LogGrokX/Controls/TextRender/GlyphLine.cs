using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using LogGrokX.Data;

namespace LogGrokX.Controls.TextRender;

public class GlyphLine : IDisposable
{
    private readonly List<GlyphRun> _runs = new();
    private readonly PooledList<double> _advanceWidthForChar = new();
    private readonly double _baselineOffset;

    private static readonly ConcurrentDictionary<(GlyphTypeface typeface, double fontSize, char ch), GlyphParameters>
        TypefaceCache = new();

    private static readonly Lazy<GlyphTypeface[]> FallbackTypefaces = new(CreateFallbackTypefaces);

    private readonly record struct GlyphParameters(
        double AdvanceWidth,
        double AdvanceHeight,
        ushort GlyphIndex,
        GlyphTypeface Typeface);

    public Size Size { get; }

    public double AdvanceHeight { get; private set; }

    public StringRange Text { get; }

    internal int RunCount => _runs.Count;

    public int GetNearestTextPosition(double relativeHorizontalPosition)
    {
        if (relativeHorizontalPosition <= 0)
        {
            return 0;
        }

        var currentOffset = 0.0;
        for (var i = 0; i < _advanceWidthForChar.Count; i++)
        {
            var currentCharWidth = _advanceWidthForChar[i];
            currentOffset += currentCharWidth;
            if (!(relativeHorizontalPosition <= currentOffset)) continue;
            if (relativeHorizontalPosition + currentCharWidth / 2.0 < currentOffset)
                return i;
            return i + 1;
        }
        return _advanceWidthForChar.Count;
    }

    public int GetCharacterIndexAtPosition(double relativeHorizontalPosition)
    {
        if (relativeHorizontalPosition < 0) return -1;

        var currentOffset = 0.0;
        for (var i = 0; i < _advanceWidthForChar.Count; i++)
        {
            currentOffset += _advanceWidthForChar[i];
            if (relativeHorizontalPosition < currentOffset) return i;
        }

        return -1;
    }

    public Rect GetTextBounds(Point startPoint, int firstTextSourceCharacterIndex, int textLength)
    {
        var height = Size.Height;
        var start = _advanceWidthForChar.Take(firstTextSourceCharacterIndex).Sum();
        var width = _advanceWidthForChar.Skip(firstTextSourceCharacterIndex).Take(textLength).Sum();

        return new Rect(start + startPoint.X, startPoint.Y, width, height);
    }

    private static GlyphTypeface[] CreateFallbackTypefaces()
    {
        var result = new List<GlyphTypeface>();
        foreach (var name in new[]
                 {
                     "Yu Gothic UI", "Microsoft YaHei", "Meiryo", "SimSun",
                     "Segoe UI Symbol", "Segoe UI Emoji", "Segoe UI"
                 })
        {
            if (new Typeface(name).TryGetGlyphTypeface(out var typeface))
                result.Add(typeface);
        }
        return result.ToArray();
    }

    private static GlyphParameters GetGlyphParametersForChar(
        (GlyphTypeface typeface, double fontSize, char ch) key)
    {
        var typeface = key.typeface;
        if (!typeface.CharacterToGlyphMap.TryGetValue(key.ch, out var glyphIndex)
            || glyphIndex == 0 && key.ch != '\0')
        {
            foreach (var fallback in FallbackTypefaces.Value)
            {
                if (!fallback.CharacterToGlyphMap.TryGetValue(key.ch, out glyphIndex) || glyphIndex == 0)
                    continue;
                typeface = fallback;
                break;
            }
        }
        return new GlyphParameters(typeface.AdvanceWidths[glyphIndex] * key.fontSize,
            typeface.AdvanceHeights[glyphIndex] * key.fontSize, glyphIndex, typeface);
    }

    public static double MeasureWidth(ReadOnlySpan<char> text, GlyphTypeface typeface, double fontSize)
    {
        if (text.IsEmpty)
            return 0;

        double totalWidth = 0;
        var indexOfGlyph = 0;
        for (var n = 0; n < text.Length; n++, indexOfGlyph++)
        {
            if (text[n] == '\t')
            {
                var spaceCount = indexOfGlyph % 8 == 0 ? 8 : 8 - indexOfGlyph % 8;
                var space = TypefaceCache.GetOrAdd((typeface, fontSize, ' '), GetGlyphParametersForChar);
                totalWidth += space.AdvanceWidth * spaceCount;
                indexOfGlyph += spaceCount;
                continue;
            }

            totalWidth += TypefaceCache.GetOrAdd((typeface, fontSize, text[n]), GetGlyphParametersForChar).AdvanceWidth;
        }

        return totalWidth;
    }

    public GlyphLine(StringRange text, GlyphTypeface typeface, double fontSize, float pixelsPerDip,
        double constraintWidth)
    {
        Text = text;
        var span = text.Span;
        if (text.Length == 0)
            return;

        double totalWidth = 0;
        var indexOfGlyph = 0;
        GlyphTypeface? runTypeface = null;
        using var glyphIndicesBuffer = new PooledList<ushort>();
        using var advanceWidthsBuffer = new PooledList<double>();
        double runStart = 0;
        var baseline = Math.Round(typeface.Baseline * fontSize);
        var height = typeface.Height * fontSize;
        var ascent = baseline;
        var descent = height - baseline;
        var usesFallback = false;

        void FlushRun()
        {
            if (runTypeface == null)
                return;
            var glyphIndices = glyphIndicesBuffer.ToArray();
            var advanceWidths = advanceWidthsBuffer.ToArray();
            _runs.Add(new GlyphRun(runTypeface,
                bidiLevel: 0,
                isSideways: false,
                renderingEmSize: fontSize,
                glyphIndices: glyphIndices,
                baselineOrigin: new Point(runStart, baseline),
                advanceWidths: advanceWidths,
                glyphOffsets: null,
                characters: null,
                deviceFontName: null,
                clusterMap: null,
                caretStops: null,
                language: null,
                pixelsPerDip: pixelsPerDip));
            glyphIndicesBuffer.Clear();
            advanceWidthsBuffer.Clear();
        }

        void AddGlyph(GlyphParameters parameters)
        {
            if (runTypeface != parameters.Typeface)
            {
                FlushRun();
                runTypeface = parameters.Typeface;
                runStart = totalWidth;
                if (runTypeface != typeface)
                {
                    var runAscent = Math.Round(runTypeface.Baseline * fontSize);
                    ascent = Math.Max(ascent, runAscent);
                    descent = Math.Max(descent, runTypeface.Height * fontSize - runAscent);
                    usesFallback = true;
                }
            }
            glyphIndicesBuffer.Add(parameters.GlyphIndex);
            advanceWidthsBuffer.Add(parameters.AdvanceWidth);
            AdvanceHeight = Math.Max(AdvanceHeight, parameters.AdvanceHeight);
            totalWidth += parameters.AdvanceWidth;
        }

        for (var n = 0; n < text.Length && totalWidth <= constraintWidth; n++, indexOfGlyph++)
        {
            if (span[n] == '\t')
            {
                var advanceWidthForChar = 0.0;
                var spaceCount = indexOfGlyph % 8 == 0 ? 8 : 8 - indexOfGlyph % 8;
                var space = TypefaceCache.GetOrAdd((typeface, fontSize, ' '), GetGlyphParametersForChar);
                while (spaceCount > 0)
                {
                    AddGlyph(space);
                    advanceWidthForChar += space.AdvanceWidth;
                    indexOfGlyph++;
                    spaceCount--;
                }

                _advanceWidthForChar.Add(advanceWidthForChar);
                continue;
            }

            var parameters = TypefaceCache.GetOrAdd((typeface, fontSize, span[n]), GetGlyphParametersForChar);
            AddGlyph(parameters);
            _advanceWidthForChar.Add(parameters.AdvanceWidth);
        }

        FlushRun();
        if (usesFallback)
        {
            _baselineOffset = ascent - baseline;
            height = Math.Max(AdvanceHeight, ascent + descent);
            AdvanceHeight = height;
        }
        Size = new Size(totalWidth, height);
    }

    public void Render(Point position, Brush brush, DrawingContext drawingContext)
    {
        drawingContext.PushTransform(new TranslateTransform(position.X, position.Y + _baselineOffset));
        foreach (var run in _runs)
            drawingContext.DrawGlyphRun(brush, run);
        drawingContext.Pop();
    }

    public void Dispose() => _advanceWidthForChar.Dispose();

    public (int start, int length)? Selection { get; private set; }

    public bool SetSelection(int start, int length)
    {
        if (Selection == (start, length)) return false;
        Selection = (start, length);
        return true;
    }

    public bool ResetSelection()
    {
        var changed = Selection != null;
        Selection = null;
        return changed;
    }
}
