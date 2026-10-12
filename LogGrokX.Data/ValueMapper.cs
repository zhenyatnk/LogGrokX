using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace LogGrokX.Data;

public sealed class ValueMapper
{
    private readonly IReadOnlyList<ValueMappingList> _lists;
    private readonly ConcurrentDictionary<string, FieldValueMapper> _fieldMappers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, FieldValueMapper[]> _fieldSetMappers = new(StringComparer.Ordinal);
    private readonly FieldValueMapper _wholeLineMapper;

    public ValueMapper(IEnumerable<ValueMappingList>? lists)
    {
        _lists = (lists ?? Enumerable.Empty<ValueMappingList>())
            .Where(list => list.Enabled && list.Items.Any(item => !string.IsNullOrEmpty(item.Value)))
            .ToList();
        _wholeLineMapper = CreateMapper(null);
    }

    public static ValueMapper Empty { get; } = new(null);

    public static ValueMapper Current { get; set; } = Empty;

    public bool IsEmpty => _lists.Count == 0;

    public FieldValueMapper ForField(string? fieldName) =>
        fieldName == null
            ? _wholeLineMapper
            : _fieldMappers.GetOrAdd(fieldName, CreateMapper);

    public FieldValueMapper[] ForFields(IReadOnlyList<string> fieldNames) =>
        _fieldSetMappers.GetOrAdd(string.Join('\u0001', fieldNames), _ => fieldNames.Select(ForField).ToArray());

    private FieldValueMapper CreateMapper(string? fieldName)
    {
        var matchers = _lists
            .Where(list => IsApplicable(list, fieldName))
            .Select(ValueMatcher.Create)
            .OfType<ValueMatcher>()
            .ToArray();

        return matchers.Length == 0 ? FieldValueMapper.None : new FieldValueMapper(matchers);
    }

    private static bool IsApplicable(ValueMappingList list, string? fieldName)
    {
        var fields = list.Fields.Where(f => !string.IsNullOrWhiteSpace(f)).ToArray();
        if (fields.Length == 0)
            return true;

        return fieldName != null &&
               fields.Any(f => string.Equals(f.Trim(), fieldName, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class FieldValueMapper
{
    private readonly ValueMatcher[] _matchers;

    internal FieldValueMapper(ValueMatcher[] matchers) => _matchers = matchers;

    public static FieldValueMapper None { get; } = new(Array.Empty<ValueMatcher>());

    public bool IsEmpty => _matchers.Length == 0;

    public static string Format(string value, string mapping) => $"{value}({mapping})";

    public IReadOnlyList<ValueMappingMatch> FindMatches(string text) => FindMatches(text, 0, text.Length);

    public IReadOnlyList<ValueMappingMatch> FindMatches(string text, int start, int length)
    {
        if (_matchers.Length == 0 || length <= 0)
            return Array.Empty<ValueMappingMatch>();

        var candidates = new List<(ValueMappingMatch match, int order)>();
        for (var order = 0; order < _matchers.Length; order++)
            _matchers[order].Collect(text, start, length, order, candidates);

        if (candidates.Count == 0)
            return Array.Empty<ValueMappingMatch>();

        candidates.Sort((left, right) =>
        {
            var byStart = left.match.Start.CompareTo(right.match.Start);
            if (byStart != 0)
                return byStart;
            var byLength = right.match.Length.CompareTo(left.match.Length);
            return byLength != 0 ? byLength : left.order.CompareTo(right.order);
        });

        var result = new List<ValueMappingMatch>(candidates.Count);
        var end = -1;
        foreach (var (match, _) in candidates)
        {
            if (match.Start < end)
                continue;
            result.Add(match);
            end = match.Start + match.Length;
        }

        return result;
    }

    public string Map(string text)
    {
        var matches = FindMatches(text);
        return matches.Count == 0 ? text : Apply(text, matches, mapping => mapping);
    }

    public static string Apply(string text, IReadOnlyList<ValueMappingMatch> matches, Func<string, string> encodeMapping)
    {
        var builder = new StringBuilder(text.Length + matches.Count * 8);
        var position = 0;
        foreach (var match in matches)
        {
            builder.Append(text, position, match.Start + match.Length - position);
            builder.Append('(').Append(encodeMapping(match.Mapping)).Append(')');
            position = match.Start + match.Length;
        }

        builder.Append(text, position, text.Length - position);
        return builder.ToString();
    }
}

internal sealed class ValueMatcher
{
    private readonly Regex _regex;
    private readonly Dictionary<string, string> _mappings;

    private ValueMatcher(Regex regex, Dictionary<string, string> mappings)
    {
        _regex = regex;
        _mappings = mappings;
    }

    public static ValueMatcher? Create(ValueMappingList list)
    {
        var mappings = new Dictionary<string, string>(
            list.IgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var item in list.Items)
        {
            var value = item.Value?.Trim();
            if (string.IsNullOrEmpty(value))
                continue;
            mappings.TryAdd(value, item.Mapping ?? string.Empty);
        }

        if (mappings.Count == 0)
            return null;

        var pattern = string.Join("|", mappings.Keys
            .OrderByDescending(key => key.Length)
            .Select(ToPattern));
        var options = RegexOptions.CultureInvariant | (list.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);
        return new ValueMatcher(new Regex(pattern, options), mappings);
    }

    public void Collect(string text, int start, int length, int order, List<(ValueMappingMatch, int)> result)
    {
        var match = _regex.Match(text, start, length);
        while (match.Success)
        {
            if (match.Length > 0 && _mappings.TryGetValue(match.Value, out var mapping))
                result.Add((new ValueMappingMatch(match.Index, match.Length, mapping), order));
            match = match.NextMatch();
        }
    }

    private static string ToPattern(string value)
    {
        var escaped = Regex.Escape(value);
        var prefix = IsWordChar(value[0]) ? @"(?<![\w])" : string.Empty;
        var suffix = IsWordChar(value[^1]) ? @"(?![\w])" : string.Empty;
        return prefix + escaped + suffix;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';
}
