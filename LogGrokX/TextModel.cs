using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using LogGrokX.Data;
using LogGrokX.Data.Index;
using Microsoft.Toolkit.HighPerformance;

namespace LogGrokX;

public class TextModel : IReadOnlyList<StringRange>
{
    private const int CollapseToLines = 20;
    
    private readonly List<StringRange>? _textLines;
    private readonly StringRange? _sourceText;
    // Reference to the original (not truncated by BigLineSize) text used for copying.
    // Only a reference is kept; lines are re-tokenized on demand to avoid extra memory.
    private readonly string? _plainSource;
    private readonly Dictionary<int, StringRange>? _substitutions;
    private readonly string? _originalStructuredSource;
    private string? _formattedStructuredText;
    private List<(int start, int length, StructuredTextKind kind)>? _originalStructuredRanges;
    private List<(int start, int length, StructuredTextKind kind)>? _formattedStructuredRanges;
    private Dictionary<int, (int start, int length)>? _indexedCollapsibleRanges;
    private Dictionary<int, StringRange>? _collapsedTextCache;
    private Dictionary<int, IReadOnlyList<(int collapsedStart, int formattedStart, int length)>>? _collapsedSegments;
    private Regex? _highlightRegex;
    private IReadOnlyList<(int start, int length, bool isWildcard)>? _originalHighlightRanges;

    public int UniqueId { get; }

    public List<(int start, int length)>? CollapsibleRanges { get; }

    public StringRange GetCollapsedTextSubstitution(int index)
    {
        if (_textLines is not { } textLines || CollapsibleRanges is not { } collapsibleRanges)
        {
            throw new InvalidOperationException();
        }

        if (_substitutions?.TryGetValue(index, out var result) ?? false)
        {
            return result;
        }

        if (_collapsedTextCache?.TryGetValue(index, out var cached) ?? false)
        {
            return cached;
        }

        _indexedCollapsibleRanges ??= collapsibleRanges.ToDictionary(
            static kv => kv.start, static kv => kv);

        var (start, length) = _indexedCollapsibleRanges[index];
        var end = Math.Min(start + length, textLines.Count);
        var builder = new StringBuilder();
        List<(int collapsedStart, int formattedStart, int length)>? segments =
            _originalStructuredSource == null ? null : new();
        for (var i = start; i < end; i++)
        {
            var lineRange = textLines[i];
            var line = lineRange.Span;
            var trimmed = i == start ? line.TrimEnd() : line.Trim();
            var leadingLength = i == start ? 0 : line.Length - line.TrimStart().Length;
            segments?.Add((builder.Length, lineRange.Start + leadingLength, trimmed.Length));
            builder.Append(trimmed);
        }

        var collapsedText = StringRange.FromString(builder.ToString());
        _collapsedTextCache ??= new Dictionary<int, StringRange>();
        _collapsedTextCache[index] = collapsedText;
        if (segments != null)
        {
            _collapsedSegments ??= new Dictionary<int, IReadOnlyList<(int collapsedStart, int formattedStart, int length)>>();
            _collapsedSegments[index] = segments;
        }
        return collapsedText;
    }

    internal bool TryGetOriginalText(int lineIndex, int start, int length, bool isCollapsed,
        out string? text)
    {
        text = null;
        if (_originalStructuredSource == null || _formattedStructuredText == null ||
            _originalStructuredRanges == null || _formattedStructuredRanges == null || _textLines == null ||
            start < 0 || length <= 0)
            return false;

        int formattedStart;
        int formattedEnd;
        if (isCollapsed)
        {
            if (GetCollapsedTextSubstitution(lineIndex).IsEmpty ||
                _collapsedSegments?.TryGetValue(lineIndex, out var segments) != true ||
                !TryGetCollapsedRange(segments!, start, length, out formattedStart, out formattedEnd))
                return false;
        }
        else
        {
            if ((uint)lineIndex >= (uint)_textLines.Count || start > _textLines[lineIndex].Length - length)
                return false;
            formattedStart = _textLines[lineIndex].Start + start;
            formattedEnd = formattedStart + length;
        }

        for (var i = 0; i < _formattedStructuredRanges.Count; i++)
        {
            var formattedRange = _formattedStructuredRanges[i];
            if (formattedStart < formattedRange.start || formattedEnd > formattedRange.start + formattedRange.length)
                continue;

            var originalRange = _originalStructuredRanges[i];
            var originalText = _originalStructuredSource.Substring(originalRange.start, originalRange.length);
            var formattedText = _formattedStructuredText.Substring(formattedRange.start, formattedRange.length);
            return StructuredSourceMap.TryCreate(originalRange.kind, originalText, formattedText, out var map) &&
                   map!.TryGetSubstring(formattedStart - formattedRange.start,
                       formattedEnd - formattedStart, out text);
        }

        return false;
    }

    private static bool TryGetCollapsedRange(
        IReadOnlyList<(int collapsedStart, int formattedStart, int length)> segments,
        int start, int length, out int formattedStart, out int formattedEnd)
    {
        formattedStart = 0;
        formattedEnd = 0;
        var haveStart = false;
        var haveEnd = false;
        var end = start + length - 1;
        foreach (var (collapsedStart, segmentFormattedStart, segmentLength) in segments)
        {
            var collapsedEnd = collapsedStart + segmentLength;
            if (!haveStart && start >= collapsedStart && start < collapsedEnd)
            {
                formattedStart = segmentFormattedStart + start - collapsedStart;
                haveStart = true;
            }

            if (end >= collapsedStart && end < collapsedEnd)
            {
                formattedEnd = segmentFormattedStart + end - collapsedStart + 1;
                haveEnd = true;
            }
        }

        return haveStart && haveEnd;
    }

    internal IReadOnlyList<(int start, int length, bool isWildcard)> GetOriginalHighlightRanges(int lineIndex,
        bool isCollapsed, Regex regex)
    {
        if (_originalStructuredSource == null || _formattedStructuredText == null ||
            _originalStructuredRanges == null || _formattedStructuredRanges == null || _textLines == null ||
            (uint)lineIndex >= (uint)_textLines.Count)
            return Array.Empty<(int start, int length, bool isWildcard)>();

        if (!ReferenceEquals(regex, _highlightRegex))
        {
            _highlightRegex = regex;
            _originalHighlightRanges = BuildOriginalHighlightRanges(regex);
        }

        var result = new List<(int start, int length, bool isWildcard)>();
        if (isCollapsed)
        {
            if (GetCollapsedTextSubstitution(lineIndex).IsEmpty ||
                _collapsedSegments?.TryGetValue(lineIndex, out var segments) != true)
                return result;

            foreach (var range in _originalHighlightRanges!)
            foreach (var (collapsedStart, formattedStart, segmentLength) in segments!)
            {
                var start = Math.Max(range.start, formattedStart);
                var end = Math.Min(range.start + range.length, formattedStart + segmentLength);
                if (end > start)
                    result.Add((collapsedStart + start - formattedStart, end - start, range.isWildcard));
            }
        }
        else
        {
            var line = _textLines[lineIndex];
            foreach (var range in _originalHighlightRanges!)
            {
                var start = Math.Max(range.start, line.Start);
                var end = Math.Min(range.start + range.length, line.End);
                if (end > start)
                    result.Add((start - line.Start, end - start, range.isWildcard));
            }
        }

        return result;
    }

    private IReadOnlyList<(int start, int length, bool isWildcard)> BuildOriginalHighlightRanges(Regex regex)
    {
        var result = new List<(int start, int length, bool isWildcard)>();
        var maps = new StructuredSourceMap?[_originalStructuredRanges!.Count];
        for (var i = 0; i < maps.Length; i++)
        {
            var originalRange = _originalStructuredRanges[i];
            var formattedRange = _formattedStructuredRanges![i];
            StructuredSourceMap.TryCreate(originalRange.kind,
                _originalStructuredSource!.Substring(originalRange.start, originalRange.length),
                _formattedStructuredText!.Substring(formattedRange.start, formattedRange.length),
                out maps[i]);
        }

        var highlightPattern = new SearchHighlightPattern(regex);
        foreach (Match match in regex.Matches(_originalStructuredSource!))
        {
            if (match.Length == 0 || MapSourceRange(maps, match.Index, match.Length) is not { } wholeRange)
                continue;

            var wildcardRanges = new List<(int start, int length)>();
            if (highlightPattern.GetWildcardRanges(match) is { } sourceWildcards)
                foreach (var wildcard in sourceWildcards)
                    if (MapSourceRange(maps, match.Index + wildcard.start, wildcard.length) is { } formattedWildcard)
                        wildcardRanges.Add(formattedWildcard);

            foreach (var range in SearchHighlightPattern.Subtract(wholeRange, wildcardRanges))
                result.Add((range.start, range.length, false));
            foreach (var range in wildcardRanges)
                result.Add((range.start, range.length, true));
        }

        return result;
    }

    private (int start, int length)? MapSourceRange(StructuredSourceMap?[] maps, int sourceStart, int sourceLength)
    {
        var sourceEnd = sourceStart + sourceLength;
        var first = -1;
        var last = -1;
        for (var i = 0; i < _originalStructuredRanges!.Count; i++)
        {
            var range = _originalStructuredRanges[i];
            if (sourceStart >= range.start + range.length || sourceEnd <= range.start)
                continue;
            if (first < 0) first = i;
            last = i;
        }

        if (first < 0) return null;
        for (var i = first; i <= last; i++)
            if (maps[i] == null)
                return null;

        var formattedStart = 0;
        var formattedEnd = 0;
        var firstRange = _originalStructuredRanges[first];
        if (sourceStart < firstRange.start)
            formattedStart = MapPlainSourcePosition(sourceStart);
        else if (!TryGetFormattedStructuredRange(maps, first, sourceStart - firstRange.start,
                     Math.Min(sourceEnd, firstRange.start + firstRange.length) - sourceStart,
                     out formattedStart, out _))
            return null;

        var lastRange = _originalStructuredRanges[last];
        if (sourceEnd > lastRange.start + lastRange.length)
            formattedEnd = MapPlainSourcePosition(sourceEnd);
        else
        {
            var start = Math.Max(sourceStart, lastRange.start);
            if (!TryGetFormattedStructuredRange(maps, last, start - lastRange.start, sourceEnd - start,
                    out _, out formattedEnd))
                return null;
        }

        return formattedEnd > formattedStart ? (formattedStart, formattedEnd - formattedStart) : null;
    }

    private bool TryGetFormattedStructuredRange(StructuredSourceMap?[] maps, int index, int sourceStart, int sourceLength,
        out int formattedStart, out int formattedEnd)
    {
        formattedStart = 0;
        formattedEnd = 0;
        if (maps[index]?.TryGetFormattedRange(sourceStart, sourceLength, out var start, out var length) != true)
            return false;
        var formattedRange = _formattedStructuredRanges![index];
        formattedStart = formattedRange.start + start;
        formattedEnd = formattedStart + length;
        return true;
    }

    private int MapPlainSourcePosition(int sourcePosition)
    {
        var offset = 0;
        for (var i = 0; i < _originalStructuredRanges!.Count; i++)
        {
            var originalRange = _originalStructuredRanges[i];
            if (sourcePosition < originalRange.start)
                return sourcePosition + offset;
            var formattedRange = _formattedStructuredRanges![i];
            offset = formattedRange.start + formattedRange.length - originalRange.start - originalRange.length;
        }

        return sourcePosition + offset;
    }

    public IEnumerator<StringRange> GetEnumerator()
    {
        for (var i = 0; i < Count; i++)
        {
            yield return this[i];
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public int Count => _textLines?.Count ?? 1;

    public StringRange this[int index]
    {
        get
        {
            var textLines = _textLines;

            if (textLines == null)
            {
                return (index, _sourceText) switch
                {
                    (0, { } value) => value,
                    _ => throw new InvalidOperationException(),
                };
            }

            return textLines[index];
        }
    }

    public TextModel(int uniqueId, string source) : this(uniqueId, source, false) { }

    internal TextModel(int uniqueId, string source, bool enableOriginalTextSearch)
    {
        UniqueId = uniqueId;
        var structuredRanges = TextOperations.GetStructuredRanges(source);
        var viewSettings = ApplicationSettings.Instance().ViewSettings;
        if (structuredRanges.Count != 0)
        {
            if (enableOriginalTextSearch)
                _originalStructuredSource = source;
            (_textLines, CollapsibleRanges) = GetStructuredProperties(source, structuredRanges);
        }
        else
        {
            var textLines = source.Tokenize().ToList();
            if (textLines.Count > 1)
            {
                _textLines = textLines.Select(c => TextOperations.Normalize(c, viewSettings)).ToList();
            }

            if (textLines.Count > CollapseToLines + 1)
            {
                var linesToCollapse = textLines.Count - CollapseToLines;
                CollapsibleRanges = new List<(int start, int length)>
                {
                    (CollapseToLines, linesToCollapse)
                };

                _substitutions = new Dictionary<int, StringRange>()
                {
                    { CollapseToLines, StringRange.FromString(Localization.Strings.Format("MoreLines", linesToCollapse)) }
                };

            }
            
            _sourceText = StringRange.FromString(TextOperations.Normalize(source,
                ApplicationSettings.Instance().ViewSettings));
            _plainSource = source;
        }
    }

    private (List<StringRange> textLines, List<(int start, int length)> collapsibleRanges)
        GetStructuredProperties(string source, List<(int start, int length, StructuredTextKind kind)> structuredRanges)
    {
        var (text, formattedRanges) = TextOperations.FormatInlineStructured(source, structuredRanges);
        if (_originalStructuredSource != null)
        {
            _formattedStructuredText = text;
            _originalStructuredRanges = structuredRanges;
            _formattedStructuredRanges = formattedRanges;
        }
        return GetCollapsibleRanges(text, formattedRanges);
    }

    private static (List<StringRange> textLines, List<(int start, int length)> ranges) GetCollapsibleRanges(
        string source, List<(int start, int length, StructuredTextKind kind)> structuredRanges)
    {
        var lines = source.Tokenize().ToList();
        var result = new List<(int start, int length)>();

        int GetLineNumber(int position)
        {
            var found = lines.BinarySearch(position, (s, p) => s.Start.CompareTo(p));
            return found >= 0 ? found : ~found - 1;
        }

        void AddInterval(int start, int length)
        {
            var startLine = GetLineNumber(start);
            var endLine = GetLineNumber(start + length);
            var lengthLines = endLine - startLine + 1;
            if (lengthLines > 1)
                result.Add((startLine, lengthLines));
        }

        var bracesStack = new Stack<(char brace, int position)>();

        int PopChar(char ch)
        {
            if (!bracesStack.TryPeek(out var prev) || prev.brace != ch)
                throw new InvalidOperationException();

            bracesStack.Pop();
            return prev.position;
        }

        foreach (var (offset, length, kind) in structuredRanges)
        {
            var span = source.AsSpan(offset, length);

            if (kind == StructuredTextKind.Xml)
            {
                foreach (var (open, close) in TextOperations.GetXmlElementRanges(span))
                    AddInterval(open + offset, close - open);
                continue;
            }

            var position = 0;
            while (position < length)
            {
                position = span[position..].IndexOfAny("{}[]") + position;
                if (position < 0)
                    break;
                switch (span[position])
                {
                    case '{':
                    case '[':
                        bracesStack.Push((span[position], position));
                        break;
                    case '}':
                    case ']':
                        var start = PopChar(span[position] == ']' ? '[' : '{');
                        AddInterval(start + offset, position - start);
                        break;
                }

                position++;
            }
        }

        return (lines, result);
    }

    public string GetDisplayedText(IReadOnlySet<int>? collapsedLines)
    {
        if (_textLines == null)
            return _plainSource != null
                ? TrimLine(_plainSource)
                : (_sourceText?.ToString() ?? string.Empty).TrimEnd();

        // Plain multi-line text: the only collapsible range is the "More N lines >>>" tail,
        // which must not affect copying, so the whole original text is returned.
        if (_plainSource != null)
            return string.Join(Environment.NewLine,
                _plainSource.Tokenize().Select(l => TrimLine(l.ToString()))).TrimEnd();

        var collapsedRanges = CollapsibleRanges == null || collapsedLines == null
            ? null
            : CollapsibleRanges.Where(r => collapsedLines.Contains(r.start))
                .ToDictionary(r => r.start, r => r.length);

        var builder = new StringBuilder();
        for (var i = 0; i < _textLines.Count; i++)
        {
            if (collapsedRanges != null && collapsedRanges.TryGetValue(i, out var length))
            {
                var substitution = GetCollapsedTextSubstitution(i);
                builder.Append(substitution.IsEmpty
                    ? _textLines[i].ToString().TrimEnd().TrimEnd('{') + "{...}"
                    : substitution.ToString());
                i += length - 1;
            }
            else
            {
                builder.Append(_textLines[i].ToString());
            }

            if (i < _textLines.Count - 1)
                builder.Append(Environment.NewLine);
        }

        return builder.ToString().TrimEnd();
    }

    private static string TrimLine(string line) => line.TrimEnd('\0').TrimEnd();

    public override string ToString()
    {
        return _sourceText?.ToString() ?? "<null>" ;
    }
}