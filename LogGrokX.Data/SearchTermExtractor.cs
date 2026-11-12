using System;
using System.Text.RegularExpressions;

namespace LogGrokX.Data;

public static class SearchTermExtractor
{
    public static (int start, int length)? GetTermRange(ReadOnlySpan<char> text, int position)
    {
        if (text.IsEmpty) return null;
        position = Math.Clamp(position, 0, text.Length - 1);

        if (IsSeparator(text[position]))
            return GetPairAroundSeparator(text, position);

        if (!IsTokenPart(text[position]))
        {
            if (position == 0 || !IsTokenPart(text[position - 1])) return null;
            position--;
        }

        var (start, end) = GetToken(text, position);

        var separator = SkipSpacesForward(text, end);
        if (separator < text.Length && IsSeparator(text[separator]))
        {
            var valueStart = SkipSpacesForward(text, separator + 1);
            if (valueStart < text.Length && IsTokenPart(text[valueStart]))
                return (start, GetToken(text, valueStart).end - start);
        }

        separator = SkipSpacesBackward(text, start) - 1;
        if (separator >= 0 && IsSeparator(text[separator]))
        {
            var keyEnd = SkipSpacesBackward(text, separator);
            if (keyEnd > 0 && IsTokenPart(text[keyEnd - 1]))
            {
                var keyStart = GetToken(text, keyEnd - 1).start;
                return (keyStart, end - keyStart);
            }
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
        var valueStart = SkipSpacesForward(text, separator + 1);
        if (keyEnd == 0 || !IsTokenPart(text[keyEnd - 1]) ||
            valueStart >= text.Length || !IsTokenPart(text[valueStart]))
            return null;

        var start = GetToken(text, keyEnd - 1).start;
        return (start, GetToken(text, valueStart).end - start);
    }

    private static (int start, int end) GetToken(ReadOnlySpan<char> text, int position)
    {
        var start = position;
        while (start > 0 && IsTokenPart(text[start - 1])) start--;
        var end = position + 1;
        while (end < text.Length && IsTokenPart(text[end])) end++;

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

    private static bool IsTokenPart(char ch) => char.IsLetterOrDigit(ch) || ch is '_' or '.' or '-';

    private static bool IsTokenEdgeTrimmed(char ch) => ch is '.' or '-';

    private static bool IsSeparator(char ch) => ch is ':' or '=';

    private static bool IsSpace(char ch) => ch is ' ' or '\t';
}
