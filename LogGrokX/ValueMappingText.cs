using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using LogGrokX.Data;

namespace LogGrokX;

public static class ValueMappingText
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string Apply(string source, FieldValueMapper? mapper)
    {
        if (mapper == null || mapper.IsEmpty || string.IsNullOrEmpty(source))
            return source;

        var matches = mapper.FindMatches(source);
        if (matches.Count == 0)
            return source;

        var structuredRanges = TextOperations.GetStructuredRanges(source);
        if (structuredRanges.Count == 0)
            return FieldValueMapper.Apply(source, matches, mapping => mapping);

        var builder = new StringBuilder(source.Length + matches.Count * 8);
        var position = 0;
        foreach (var (start, length, kind) in structuredRanges)
        {
            AppendPlain(builder, source, position, start - position, matches);
            builder.Append(MapStructured(source.Substring(start, length), mapper, kind));
            position = start + length;
        }

        AppendPlain(builder, source, position, source.Length - position, matches);
        return builder.ToString();
    }

    private static void AppendPlain(StringBuilder builder, string source, int start, int length,
        IReadOnlyList<ValueMappingMatch> matches)
    {
        if (length <= 0)
            return;

        var end = start + length;
        var position = start;
        foreach (var match in matches)
        {
            if (match.Start < start || match.Start + match.Length > end)
                continue;

            builder.Append(source, position, match.Start + match.Length - position);
            builder.Append('(').Append(match.Mapping).Append(')');
            position = match.Start + match.Length;
        }

        builder.Append(source, position, end - position);
    }

    private static string MapStructured(string fragment, FieldValueMapper mapper, StructuredTextKind kind) =>
        kind == StructuredTextKind.Json ? MapJson(fragment, mapper) : MapXml(fragment, mapper);

    private static string MapXml(string fragment, FieldValueMapper mapper)
    {
        var matches = new List<ValueMappingMatch>();
        foreach (var (start, end) in GetXmlValues(fragment))
            matches.AddRange(mapper.FindMatches(fragment, start, end - start));

        if (matches.Count == 0)
            return fragment;

        var mapped = FieldValueMapper.Apply(fragment, matches, SecurityElement.Escape);
        return IsSameStructure(mapped, StructuredTextKind.Xml) ? mapped : fragment;
    }

    private static string MapJson(string fragment, FieldValueMapper mapper)
    {
        var matches = new List<(ValueMappingMatch Match, bool IsNumber)>();
        foreach (var (start, end) in GetJsonStringValues(fragment))
            foreach (var match in mapper.FindMatches(fragment, start, end - start))
                matches.Add((match, false));
        foreach (var (start, end) in GetJsonNumberValues(fragment))
            foreach (var match in mapper.FindMatches(fragment, start, end - start))
                matches.Add((match, true));

        if (matches.Count == 0)
            return fragment;

        matches.Sort(static (left, right) => left.Match.Start.CompareTo(right.Match.Start));

        var builder = new StringBuilder(fragment.Length + matches.Count * 8);
        var position = 0;
        foreach (var (match, isNumber) in matches)
        {
            builder.Append(fragment, position, match.Start + match.Length - position);
            if (isNumber)
                builder.Append(" /*").Append(SanitizeComment(match.Mapping)).Append("*/");
            else
                builder.Append('(').Append(EscapeJson(match.Mapping)).Append(')');
            position = match.Start + match.Length;
        }

        builder.Append(fragment, position, fragment.Length - position);
        var mapped = builder.ToString();
        return IsSameStructure(mapped, StructuredTextKind.Json) ? mapped : fragment;
    }

    private static string SanitizeComment(string mapping)
    {
        var builder = new StringBuilder(mapping.Length);
        foreach (var ch in mapping)
        {
            switch (ch)
            {
                case '\r' or '\n':
                    break;
                case '{' or '}' or '[' or ']':
                    builder.Append(' ');
                    break;
                case '/' when builder.Length > 0 && builder[^1] == '*':
                    builder.Append(' ').Append(ch);
                    break;
                default:
                    builder.Append(ch);
                    break;
            }
        }

        return builder.ToString();
    }

    private static bool IsSameStructure(string fragment, StructuredTextKind kind)
    {
        var ranges = TextOperations.GetStructuredRanges(fragment);
        return ranges.Count == 1 && ranges[0] == (0, fragment.Length, kind);
    }

    private static string EscapeJson(string text)
    {
        var encoded = JsonSerializer.Serialize(text, JsonOptions);
        return encoded.Substring(1, encoded.Length - 2);
    }

    internal static List<(int start, int end)> GetJsonStringValues(string json)
    {
        var result = new List<(int start, int end)>();
        var start = -1;
        for (var i = 0; i < json.Length; i++)
        {
            var ch = json[i];
            if (start >= 0 && ch == '\\')
            {
                i++;
                continue;
            }

            if (ch != '"')
                continue;

            if (start < 0)
            {
                start = i + 1;
                continue;
            }

            if (!IsJsonKey(json, i + 1))
                result.Add((start, i));
            start = -1;
        }

        return result;
    }

    private static bool IsJsonKey(string json, int position)
    {
        while (position < json.Length && char.IsWhiteSpace(json[position]))
            position++;
        return position < json.Length && json[position] == ':';
    }

    internal static List<(int start, int end)> GetJsonNumberValues(string json)
    {
        var result = new List<(int start, int end)>();
        var i = 0;
        while (i < json.Length)
        {
            var ch = json[i];
            if (ch == '"')
            {
                i = SkipJsonString(json, i);
                continue;
            }

            if (ch == '/' && i + 1 < json.Length && json[i + 1] == '*')
            {
                var commentEnd = json.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = commentEnd < 0 ? json.Length : commentEnd + 2;
                continue;
            }

            if (ch == '/' && i + 1 < json.Length && json[i + 1] == '/')
            {
                var commentEnd = json.IndexOf('\n', i + 2);
                i = commentEnd < 0 ? json.Length : commentEnd + 1;
                continue;
            }

            if (ch == '-' || char.IsAsciiDigit(ch))
            {
                var start = i;
                i++;
                while (i < json.Length && IsJsonNumberChar(json[i]))
                    i++;
                result.Add((start, i));
                continue;
            }

            i++;
        }

        return result;
    }

    private static int SkipJsonString(string json, int position)
    {
        position++;
        while (position < json.Length)
        {
            if (json[position] == '\\')
            {
                position += 2;
                continue;
            }

            if (json[position] == '"')
                return position + 1;
            position++;
        }

        return position;
    }

    private static bool IsJsonNumberChar(char c) =>
        char.IsAsciiDigit(c) || c is '.' or 'e' or 'E' or '+' or '-';

    internal static List<(int start, int end)> GetXmlValues(string xml)
    {
        var result = new List<(int start, int end)>();
        var textStart = 0;
        var i = 0;
        while (i < xml.Length)
        {
            if (xml[i] != '<')
            {
                i++;
                continue;
            }

            if (i > textStart)
                result.Add((textStart, i));

            if (string.CompareOrdinal(xml, i, "<![CDATA[", 0, 9) == 0)
            {
                var cdataEnd = xml.IndexOf("]]>", i + 9, StringComparison.Ordinal);
                if (cdataEnd < 0)
                    return result;
                result.Add((i + 9, cdataEnd));
                i = textStart = cdataEnd + 3;
                continue;
            }

            if (string.CompareOrdinal(xml, i, "<!--", 0, 4) == 0)
            {
                var commentEnd = xml.IndexOf("-->", i + 4, StringComparison.Ordinal);
                if (commentEnd < 0)
                    return result;
                i = textStart = commentEnd + 3;
                continue;
            }

            i++;
            while (i < xml.Length && xml[i] != '>')
            {
                if (xml[i] is '"' or '\'')
                {
                    var quote = xml[i];
                    var valueEnd = xml.IndexOf(quote, i + 1);
                    if (valueEnd < 0)
                        return result;
                    result.Add((i + 1, valueEnd));
                    i = valueEnd;
                }

                i++;
            }

            i++;
            textStart = i;
        }

        if (textStart < xml.Length)
            result.Add((textStart, xml.Length));

        return result.Where(range => range.end > range.start).ToList();
    }
}
