using System.Collections.Generic;

namespace LogGrokX;

internal class StructuredSourceMap
{
    private readonly string _source;
    private readonly string _formatted;
    private readonly List<int> _sourcePositions;
    private readonly List<int> _formattedPositions;

    protected StructuredSourceMap(string source, string formatted, List<int> sourcePositions,
        List<int> formattedPositions)
    {
        _source = source;
        _formatted = formatted;
        _sourcePositions = sourcePositions;
        _formattedPositions = formattedPositions;
    }

    public static bool TryCreate(StructuredTextKind kind, string source, string formatted,
        out StructuredSourceMap? map)
    {
        if (kind == StructuredTextKind.Json)
        {
            var created = JsonSourceMap.TryCreate(source, formatted, out var jsonMap);
            map = jsonMap;
            return created;
        }

        var xmlCreated = XmlSourceMap.TryCreate(source, formatted, out var xmlMap);
        map = xmlMap;
        return xmlCreated;
    }

    public bool TryGetSubstring(int start, int length, out string? result)
    {
        result = null;
        if (start < 0 || length <= 0 || start > _formatted.Length - length)
            return false;

        var end = start + length;
        var sourceStart = MapStart(start);
        var sourceEnd = MapEnd(end);
        if (sourceStart < 0 || sourceEnd < sourceStart || sourceEnd > _source.Length)
            return false;

        result = _source.Substring(sourceStart, sourceEnd - sourceStart);
        return true;
    }

    public bool TryGetFormattedRange(int sourceStart, int sourceLength,
        out int formattedStart, out int formattedLength)
    {
        formattedStart = 0;
        formattedLength = 0;
        if (sourceStart < 0 || sourceLength <= 0 || sourceStart > _source.Length - sourceLength)
            return false;

        var sourceEnd = sourceStart + sourceLength;
        var index = _sourcePositions.BinarySearch(sourceStart);
        if (index >= 0)
            formattedStart = _formattedPositions[index];
        else
        {
            index = ~index;
            formattedStart = index < _formattedPositions.Count ? _formattedPositions[index] : _formatted.Length;
        }

        int formattedEnd;
        index = _sourcePositions.BinarySearch(sourceEnd - 1);
        if (index >= 0)
            formattedEnd = _formattedPositions[index] + 1;
        else
        {
            index = _sourcePositions.BinarySearch(sourceEnd);
            if (index >= 0)
                formattedEnd = _formattedPositions[index];
            else
            {
                index = ~index;
                formattedEnd = index > 0 ? _formattedPositions[index - 1] + 1 : 0;
            }
        }

        if (formattedEnd <= formattedStart)
            return false;
        formattedLength = formattedEnd - formattedStart;
        return true;
    }

    private int MapStart(int start)
    {
        var index = _formattedPositions.BinarySearch(start);
        if (index >= 0)
            return _sourcePositions[index];

        index = ~index;
        return index == 0 ? 0 : _sourcePositions[index - 1] + 1;
    }

    private int MapEnd(int end)
    {
        var index = _formattedPositions.BinarySearch(end - 1);
        if (index >= 0)
            return _sourcePositions[index] + 1;

        index = _formattedPositions.BinarySearch(end);
        if (index >= 0)
            return _sourcePositions[index];

        index = ~index;
        return index < _sourcePositions.Count ? _sourcePositions[index] : _source.Length;
    }
}
