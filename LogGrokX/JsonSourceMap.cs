using System;
using System.Collections.Generic;

namespace LogGrokX;

internal sealed class JsonSourceMap : StructuredSourceMap
{
    private JsonSourceMap(string source, string formatted, List<int> sourcePositions,
        List<int> formattedPositions) : base(source, formatted, sourcePositions, formattedPositions) { }

    public static bool TryCreate(string source, string formatted, out JsonSourceMap? map)
    {
        map = null;
        var formattedPositions = new List<int>();
        var sourcePositions = new List<int>();
        var sourceIndex = 0;
        var formattedIndex = 0;
        var inString = false;
        var escaped = false;

        while (true)
        {
            if (!inString)
            {
                SkipWhitespace(source, ref sourceIndex);
                SkipWhitespace(formatted, ref formattedIndex);
            }

            if (sourceIndex >= source.Length || formattedIndex >= formatted.Length)
                break;
            if (source[sourceIndex] != formatted[formattedIndex])
                return false;

            sourcePositions.Add(sourceIndex);
            formattedPositions.Add(formattedIndex);

            var ch = formatted[formattedIndex];
            if (inString)
            {
                if (escaped) escaped = false;
                else if (ch == '\\') escaped = true;
                else if (ch == '"') inString = false;
            }
            else if (ch == '"')
            {
                inString = true;
            }

            sourceIndex++;
            formattedIndex++;
        }

        if (sourceIndex != source.Length || formattedIndex != formatted.Length ||
            formattedPositions.Count == 0)
            return false;

        map = new JsonSourceMap(source, formatted, sourcePositions, formattedPositions);
        return true;
    }

    public static bool TryGetSubstring(string source, string formatted, int start, int length,
        out string? result)
    {
        result = null;
        return TryCreate(source, formatted, out var map) &&
               map!.TryGetSubstring(start, length, out result);
    }

    private static void SkipWhitespace(string text, ref int position)
    {
        while (position < text.Length && char.IsWhiteSpace(text[position]))
            position++;
    }
}
