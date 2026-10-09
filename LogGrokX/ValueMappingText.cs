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

    private static string MapStructured(string fragment, FieldValueMapper mapper, StructuredTextKind kind)
    {
        var valueRanges = kind == StructuredTextKind.Json
            ? GetJsonStringValues(fragment)
            : GetXmlValues(fragment);

        var matches = new List<ValueMappingMatch>();
        foreach (var (start, end) in valueRanges)
            matches.AddRange(mapper.FindMatches(fragment, start, end - start));

        if (matches.Count == 0)
            return fragment;

        var mapped = FieldValueMapper.Apply(fragment, matches,
            kind == StructuredTextKind.Json ? EscapeJson : SecurityElement.Escape);
        return IsSameStructure(mapped, kind) ? mapped : fragment;
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
