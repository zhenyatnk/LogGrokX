using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using LogGrokX.Data;

namespace LogGrokX.Controls.TextRender;

public class GlyphLine : IDisposable
{
    private readonly GlyphRun? _run;
    private readonly PooledList<ushort> _glyphIndices = new();
    private readonly PooledList<double> _advanceWidthsForGlyph = new();
    private readonly PooledList<double> _advanceWidthForChar = new();
        
    private static readonly ConcurrentDictionary<(GlyphTypeface typeface, double fontSize, char ch), (double advanceWidth, double advanceHeight, ushort glyphIndex)> TypefaceCache = new();

    public Size Size { get; }
        
    public double AdvanceHeight { get; }

    public StringRange Text { get; }
       
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

    public Rect GetTextBounds(Point startPoint, int firstTextSourceCharacterIndex, int textLength)
    {
        var height = Size.Height;
        var start = _advanceWidthForChar.Take(firstTextSourceCharacterIndex).Sum();
        var width = _advanceWidthForChar.Skip(firstTextSourceCharacterIndex).Take(textLength).Sum();

        return new Rect(start + startPoint.X, startPoint.Y, width, height);
    }

    private static (double advanceWidth, double advanceHeight, ushort glyphIndex) GetGlyphParametersForChar(
        (GlyphTypeface typeface, double fontSize, char ch) key)
    {
        key.typeface.CharacterToGlyphMap.TryGetValue(key.ch, out var glyphIndex);
        return (key.typeface.AdvanceWidths[glyphIndex] * key.fontSize,
            key.typeface.AdvanceHeights[glyphIndex] * key.fontSize,
            glyphIndex);
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
                var (spaceWidth, _, _) = TypefaceCache.GetOrAdd((typeface, fontSize, ' '), GetGlyphParametersForChar);
                totalWidth += spaceWidth * spaceCount;
                indexOfGlyph += spaceCount;
                continue;
            }

            var (width, _, _) = TypefaceCache.GetOrAdd((typeface, fontSize, text[n]), GetGlyphParametersForChar);
            totalWidth += width;
        }

        return totalWidth;
    }

    public GlyphLine(StringRange text, GlyphTypeface typeface, double fontSize, float pixelsPerDip,
        double constraintWidth)
    {
        Text = text;
        var span = text.Span;
        if (text.Length == 0)
        {
            _run = null;
            return;
        }
            
        _glyphIndices = new PooledList<ushort>(text.Length);
        _advanceWidthsForGlyph = new PooledList<double>(text.Length);
            
        double totalWidth = 0;

        var indexOfGlyph = 0;

        for (var n = 0; n < text.Length && totalWidth <= constraintWidth; n++, indexOfGlyph++) 
        {
            ushort glyphIndex;
            double width;
            if (span[n] == '\t')
            {
                var advanceWidthForChar = 0.0;

                var spaceCount = indexOfGlyph % 8 == 0 ? 8 : (8 - indexOfGlyph % 8); 
                (width, _, glyphIndex) = TypefaceCache.GetOrAdd((typeface, fontSize, ' '), GetGlyphParametersForChar);
                while (spaceCount > 0)
                {
                    _glyphIndices.Add(glyphIndex);
                    _advanceWidthsForGlyph.Add(width);
                    advanceWidthForChar += width;
                    totalWidth += width;
                    indexOfGlyph++;
                    spaceCount--;
                }

                _advanceWidthForChar.Add(advanceWidthForChar);
                continue;
            }

            double advHeight;
            (width, advHeight, glyphIndex) = TypefaceCache.GetOrAdd((typeface, fontSize, span[n]), GetGlyphParametersForChar);
            _glyphIndices.Add(glyphIndex);
            AdvanceHeight = Math.Max(AdvanceHeight, advHeight);
            _advanceWidthsForGlyph.Add(width);
            _advanceWidthForChar.Add(width);
            totalWidth += width;
        }
            
        var height = typeface.Height* fontSize;
            
        _run = new GlyphRun(typeface,
            bidiLevel: 0,
            isSideways: false,
            renderingEmSize: fontSize,
            glyphIndices: _glyphIndices,
            baselineOrigin: new Point(0, Math.Round(typeface.Baseline * fontSize)),
            advanceWidths: _advanceWidthsForGlyph,
            glyphOffsets: null,
            characters: null,
            deviceFontName: null,
            clusterMap: null,
            caretStops: null,
            language: null, pixelsPerDip:pixelsPerDip);

        Size = new Size(totalWidth, height);
    }

    public void Render(Point position, Brush brush, DrawingContext drawingContext)
    {
        if (_run == null) return; 
        drawingContext.PushTransform(new TranslateTransform(position.X, position.Y));
        drawingContext.DrawGlyphRun(brush, _run);
        drawingContext.Pop();
    }

    public void Dispose()
    {
        _advanceWidthsForGlyph.Dispose();
        _advanceWidthForChar.Dispose();
        _glyphIndices.Dispose();
    }

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