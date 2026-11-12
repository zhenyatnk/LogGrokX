using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace LogGrokX;

internal sealed class SearchHighlightPattern
{
    private readonly Regex? _wildcardRegex;
    private readonly string[] _wildcardGroups;

    public SearchHighlightPattern(Regex regex)
    {
        _wildcardRegex = CreateWildcardRegex(regex, out _wildcardGroups);
    }

    public IReadOnlyList<(int start, int length, bool isWildcard)> GetRanges(Match match)
    {
        var wildcardRanges = GetWildcardRanges(match);
        if (wildcardRanges == null)
            return new[] { (0, match.Length, false) };

        return Subtract((0, match.Length), wildcardRanges)
            .Select(r => (r.start, r.length, false))
            .Concat(wildcardRanges.Select(r => (r.start, r.length, true)))
            .OrderBy(r => r.start)
            .ToList();
    }

    public IReadOnlyList<(int start, int length)>? GetWildcardRanges(Match match)
    {
        if (_wildcardRegex == null) return null;
        var classified = _wildcardRegex.Match(match.Value);
        if (!classified.Success || classified.Index != 0 || classified.Length != match.Length)
            return null;

        var result = new List<(int start, int length)>();
        foreach (var groupName in _wildcardGroups)
        {
            var group = classified.Groups[groupName];
            if (!group.Success) continue;
            foreach (Capture capture in group.Captures)
                if (capture.Length > 0)
                    result.Add((capture.Index, capture.Length));
        }

        return result.OrderBy(r => r.start).ThenBy(r => r.length).ToList();
    }

    public static IReadOnlyList<(int start, int length)> Subtract((int start, int length) range,
        IReadOnlyList<(int start, int length)> excluded)
    {
        var result = new List<(int start, int length)> { range };
        foreach (var hole in excluded.OrderBy(r => r.start))
        {
            var next = new List<(int start, int length)>();
            foreach (var part in result)
            {
                var partEnd = part.start + part.length;
                var holeEnd = hole.start + hole.length;
                if (hole.start >= partEnd || holeEnd <= part.start)
                {
                    next.Add(part);
                    continue;
                }

                if (hole.start > part.start)
                    next.Add((part.start, hole.start - part.start));
                if (holeEnd < partEnd)
                    next.Add((holeEnd, partEnd - holeEnd));
            }
            result = next;
        }

        return result.Where(r => r.length > 0).ToList();
    }

    private static Regex? CreateWildcardRegex(Regex regex, out string[] wildcardGroups)
    {
        wildcardGroups = Array.Empty<string>();
        var pattern = regex.ToString();
        if (regex.Options.HasFlag(RegexOptions.IgnorePatternWhitespace) ||
            pattern.Contains("(?=") || pattern.Contains("(?!") ||
            pattern.Contains("(?<=") || pattern.Contains("(?<!") ||
            pattern.Contains("(?(") || pattern.Contains("(?#"))
            return null;

        var builder = new StringBuilder(pattern.Length + 32);
        var groups = new List<string>();
        var inCharacterClass = false;
        for (var i = 0; i < pattern.Length;)
        {
            var ch = pattern[i];
            if (ch == '\\')
            {
                if (i + 1 >= pattern.Length) return null;
                if (char.IsDigit(pattern[i + 1])) return null;
                builder.Append(ch).Append(pattern[i + 1]);
                i += 2;
                continue;
            }

            if (ch == '[') inCharacterClass = true;
            else if (ch == ']') inCharacterClass = false;

            if (!inCharacterClass && ch == '.' && i + 1 < pattern.Length && pattern[i + 1] == '*')
            {
                var groupName = CreateGroupName(pattern, groups);
                groups.Add(groupName);
                var lazy = i + 2 < pattern.Length && pattern[i + 2] == '?';
                builder.Append("(?<").Append(groupName).Append(">.*");
                if (lazy) builder.Append('?');
                builder.Append(')');
                i += lazy ? 3 : 2;
                continue;
            }

            builder.Append(ch);
            i++;
        }

        if (groups.Count == 0) return null;
        try
        {
            wildcardGroups = groups.ToArray();
            return new Regex(builder.ToString(), regex.Options, regex.MatchTimeout);
        }
        catch (ArgumentException)
        {
            wildcardGroups = Array.Empty<string>();
            return null;
        }
    }

    private static string CreateGroupName(string pattern, List<string> groups)
    {
        var prefix = "__lg_wildcard";
        var name = prefix + groups.Count;
        while (pattern.Contains(name, StringComparison.Ordinal) || groups.Contains(name))
        {
            prefix = "_" + prefix;
            name = prefix + groups.Count;
        }
        return name;
    }
}
