using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
    private Dictionary<int, (int start, int length)>? _indexedCollapsibleRanges;
    private Dictionary<int, StringRange>? _collapsedTextCache;

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
        for (var i = start; i < end; i++)
        {
            var line = textLines[i].Span;
            builder.Append(i == start ? line.TrimEnd() : line.Trim());
        }

        var collapsedText = StringRange.FromString(builder.ToString());
        _collapsedTextCache ??= new Dictionary<int, StringRange>();
        _collapsedTextCache[index] = collapsedText;
        return collapsedText;
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

    public TextModel(int uniqueId, string source)
    {
        UniqueId = uniqueId;
        var structuredRanges = TextOperations.GetStructuredRanges(source);
        var viewSettings = ApplicationSettings.Instance().ViewSettings;
        if (structuredRanges.Count != 0)
        {
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
                    { CollapseToLines, StringRange.FromString($"More {linesToCollapse} lines >>>") }
                };

            }
            
            _sourceText = StringRange.FromString(TextOperations.Normalize(source,
                ApplicationSettings.Instance().ViewSettings));
            _plainSource = source;
        }
    }

    private static (List<StringRange> textLines, List<(int start, int length)> collapsibleRanges)
        GetStructuredProperties(string source, List<(int start, int length, StructuredTextKind kind)> structuredRanges)
    {
        var (text, formattedRanges) = TextOperations.FormatInlineStructured(source, structuredRanges);
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