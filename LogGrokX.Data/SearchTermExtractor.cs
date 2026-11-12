using System;
using System.Text.RegularExpressions;

namespace LogGrokX.Data;

public static class SearchTermExtractor
{
    public static (int start, int length)? GetTermRange(ReadOnlySpan<char> text, int position)
    {
        if (text.IsEmpty) return null;
        position = Math.Clamp(position, 0, text.Length - 1);

        if (GetBracketedRange(text, position) is { } bracketedRange)
            return bracketedRange;

        if (IsSeparator(text[position]))
            return GetPairAroundSeparator(text, position);

        if (TryGetQuotedRange(text, position, '\'', out var range) ||
            TryGetQuotedRange(text, position, '"', out range) ||
            TryGetWindowsPathRange(text, position, out range) ||
            TryGetCurlyBracedValueRange(text, position, out range))
            return GetTermAroundRange(text, range.start, range.end);

        if (!IsTokenPartAt(text, position))
        {
            if (position == 0 || !IsTokenPartAt(text, position - 1)) return null;
            position--;
        }

        var (start, end) = GetToken(text, position);
        return GetTermAroundRange(text, start, end);
    }

    private static (int start, int length)? GetBracketedRange(ReadOnlySpan<char> text, int position)
    {
        var closing = text[position] switch
        {
            '[' => ']',
            '<' => '>',
            '(' => ')',
            _ => '\0'
        };
        if (closing != '\0')
        {
            var depth = 0;
            for (var i = position + 1; i < text.Length; i++)
            {
                if (text[i] == text[position]) depth++;
                else if (text[i] == closing)
                {
                    if (depth > 0)
                    {
                        depth--;
                        continue;
                    }

                    return GetInnerBracketRange(text, position + 1, i);
                }
            }

            return null;
        }

        var opening = text[position] switch
        {
            ']' => '[',
            '>' => '<',
            ')' => '(',
            _ => '\0'
        };
        if (opening == '\0') return null;

        var nested = 0;
        for (var i = position - 1; i >= 0; i--)
        {
            if (text[i] == text[position]) nested++;
            else if (text[i] == opening)
            {
                if (nested > 0)
                {
                    nested--;
                    continue;
                }

                return GetInnerBracketRange(text, i + 1, position);
            }
        }

        return null;
    }

    private static (int start, int length)? GetInnerBracketRange(ReadOnlySpan<char> text, int start, int end)
    {
        var length = end - start;
        return length > 0 && !text.Slice(start, length).IsWhiteSpace() ? (start, length) : null;
    }

    private static (int start, int length) GetTermAroundRange(ReadOnlySpan<char> text, int start, int end)
    {
        var separator = SkipSpacesForward(text, end);
        if (separator < text.Length && IsSeparator(text[separator]))
        {
            var valueStart = SkipSpacesForward(text, separator + 1);
            if (GetValueEnd(text, valueStart) is { } valueEnd)
                return (start, valueEnd - start);
        }

        return (start, end - start);
    }

    public static string? GetTerm(string text, int position) =>
        GetTermRange(text, position) is var (start, length) ? text.Substring(start, length) : null;

    public static string AppendToPattern(string pattern, bool useRegex, string term)
    {
        var escapedTerm = Escape(term);
        if (string.IsNullOrEmpty(pattern)) return escapedTerm;
        var existing = useRegex ? pattern : Escape(pattern);
        return existing + ".*" + escapedTerm;
    }

    public static string Escape(string text) => Regex.Escape(text).Replace("\\ ", " ");

    private static (int start, int length)? GetPairAroundSeparator(ReadOnlySpan<char> text, int separator)
    {
        var keyEnd = SkipSpacesBackward(text, separator);
        if (keyEnd == 0 || !IsTokenPart(text[keyEnd - 1])) return null;

        var valueStart = SkipSpacesForward(text, separator + 1);
        if (GetValueEnd(text, valueStart) is not { } valueEnd) return null;

        var start = GetToken(text, keyEnd - 1).start;
        return (start, valueEnd - start);
    }

    private static int? GetValueEnd(ReadOnlySpan<char> text, int valueStart)
    {
        if (valueStart >= text.Length) return null;
        if (text[valueStart] is '\'' or '"')
        {
            var closingQuote = FindClosingQuote(text, valueStart, text[valueStart]);
            return closingQuote < 0 ? null : closingQuote + 1;
        }

        if (text[valueStart] == '{')
        {
            var closingBrace = text.Slice(valueStart + 1).IndexOf('}');
            return closingBrace < 0 ? null : valueStart + closingBrace + 2;
        }

        if (IsWindowsPathStart(text, valueStart))
        {
            var pathEnd = GetWindowsPathEnd(text, valueStart);
            return pathEnd > valueStart + 1 ? pathEnd : null;
        }

        return IsTokenPart(text[valueStart]) ? GetToken(text, valueStart).end : null;
    }

    private static bool TryGetCurlyBracedValueRange(ReadOnlySpan<char> text, int position,
        out (int start, int end) range)
    {
        range = default;
        if (text[position] == '{')
        {
            var closingBrace = text.Slice(position + 1).IndexOf('}');
            if (closingBrace < 0) return false;
            range = (position, position + closingBrace + 2);
            return true;
        }

        if (text[position] == '}')
        {
            var openingBrace = text.Slice(0, position).LastIndexOf('{');
            if (openingBrace < 0) return false;
            range = (openingBrace, position + 1);
            return true;
        }

        if (!IsTokenPartAt(text, position)) return false;
        var (start, end) = GetToken(text, position);
        if (start == 0 || end >= text.Length || text[start - 1] != '{' || text[end] != '}')
            return false;
        range = (start - 1, end + 1);
        return true;
    }

    private static bool TryGetQuotedRange(ReadOnlySpan<char> text, int position, char quote,
        out (int start, int end) range)
    {
        range = default;
        var searchStart = 0;
        while (searchStart <= position)
        {
            var openingQuote = text.Slice(searchStart).IndexOf(quote);
            if (openingQuote < 0) return false;
            openingQuote += searchStart;

            if (position < openingQuote) return false;
            if (!IsQuoteOpening(text, openingQuote))
            {
                searchStart = openingQuote + 1;
                continue;
            }

            var closingQuote = FindClosingQuote(text, openingQuote, quote);
            if (closingQuote < 0) return false;
            if (position <= closingQuote)
            {
                range = (openingQuote, closingQuote + 1);
                return true;
            }

            searchStart = closingQuote + 1;
        }

        return false;
    }

    private static int FindClosingQuote(ReadOnlySpan<char> text, int openingQuote, char quote)
    {
        for (var i = openingQuote + 1; i < text.Length; i++)
            if (text[i] == quote && (quote != '\'' || !IsEmbeddedApostrophe(text, i)))
                return i;
        return -1;
    }

    private static bool IsQuoteOpening(ReadOnlySpan<char> text, int position) =>
        position == 0 || !IsTokenPart(text[position - 1]);

    private static bool IsEmbeddedApostrophe(ReadOnlySpan<char> text, int position) =>
        text[position] == '\'' && position > 0 && position + 1 < text.Length &&
        char.IsLetterOrDigit(text[position - 1]) && char.IsLetterOrDigit(text[position + 1]);

    private static bool TryGetWindowsPathRange(ReadOnlySpan<char> text, int position,
        out (int start, int end) range)
    {
        range = default;
        var segmentStart = position;
        while (segmentStart > 0 && IsWindowsPathPart(text[segmentStart - 1])) segmentStart--;

        var start = text.Slice(segmentStart, position - segmentStart + 1).IndexOf('\\');
        if (start < 0) return false;
        start += segmentStart;

        var end = GetWindowsPathEnd(text, start);
        if (position >= end || end <= start + 1) return false;
        range = (start, end);
        return true;
    }

    private static bool IsWindowsPathStart(ReadOnlySpan<char> text, int position) =>
        position >= 0 && position < text.Length && text[position] == '\\';

    private static int GetWindowsPathEnd(ReadOnlySpan<char> text, int start)
    {
        var end = start + 1;
        while (end < text.Length && IsWindowsPathPart(text[end])) end++;
        return end;
    }

    private static (int start, int end) GetToken(ReadOnlySpan<char> text, int position)
    {
        var start = position;
        while (start > 0 && IsTokenPartAt(text, start - 1)) start--;
        var end = position + 1;
        while (end < text.Length && IsTokenPartAt(text, end)) end++;

        while (start < position && text[start] == '.') start++;
        while (end > position + 1 && IsTokenEdgeTrimmed(text[end - 1])) end--;
        return (start, end);
    }

    private static int SkipSpacesForward(ReadOnlySpan<char> text, int position)
    {
        while (position < text.Length && IsSpace(text[position])) position++;
        return position;
    }

    private static int SkipSpacesBackward(ReadOnlySpan<char> text, int position)
    {
        while (position > 0 && IsSpace(text[position - 1])) position--;
        return position;
    }

    private static bool IsTokenPartAt(ReadOnlySpan<char> text, int position) =>
        IsTokenPart(text[position]) || IsEmbeddedApostrophe(text, position);

    private static bool IsTokenPart(char ch) => char.IsLetterOrDigit(ch) || ch is '_' or '.' or '-';

    private static bool IsWindowsPathPart(char ch) =>
        char.IsLetterOrDigit(ch) || ch is '_' or '.' or '-' or '\\' or '?' or '{' or '}' or ':';

    private static bool IsTokenEdgeTrimmed(char ch) => ch is '.' or '-';

    private static bool IsSeparator(char ch) => ch is ':' or '=';

    private static bool IsSpace(char ch) => ch is ' ' or '\t';
}
