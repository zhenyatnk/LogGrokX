using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace LogGrokX.Settings
{
    public sealed class YamlSettingsFile
    {
        private readonly string _fileName;
        private readonly List<string> _lines;

        public YamlSettingsFile(string fileName)
        {
            _fileName = fileName;
            _lines = File.Exists(fileName)
                ? File.ReadAllLines(fileName).ToList()
                : new List<string>();
        }

        public bool HasChanges { get; private set; }

        public void SetScalar(string section, string key, string yamlValue)
        {
            if (!TryFindKey(section, key, out var index, out _))
            {
                var bounds = EnsureSection(section);
                _lines.Insert(bounds.end, $"{new string(' ', ChildIndent(bounds))}{key}: {yamlValue}");
                HasChanges = true;
                return;
            }

            var line = _lines[index];
            var match = Regex.Match(line, $@"^(\s*{Regex.Escape(key)}\s*:\s*)(.*)$");
            if (!match.Success)
                return;

            var current = match.Groups[2].Value;
            var comment = ExtractInlineComment(current);
            var value = comment.Length > 0 ? current[..^comment.Length] : current;
            if (value.Trim() == yamlValue)
                return;

            _lines[index] = match.Groups[1].Value + yamlValue + comment;
            HasChanges = true;
        }

        public void ReplaceSequence(string section, string key, Func<int, IReadOnlyList<string>> render, bool force = false)
        {
            if (!TryFindKey(section, key, out var keyIndex, out var sectionEnd))
            {
                var bounds = EnsureSection(section);
                _lines.Insert(bounds.end, $"{new string(' ', ChildIndent(bounds))}{key}:");
                HasChanges = true;
                TryFindKey(section, key, out keyIndex, out sectionEnd);
            }

            var keyIndent = IndentOf(_lines[keyIndex]);
            var (start, end) = FindSequenceBounds(keyIndex, keyIndent, sectionEnd);
            var itemIndent = start >= 0 ? IndentOf(_lines[start]) : keyIndent + 2;

            var desired = render(itemIndent).ToList();
            var existing = start >= 0
                ? _lines.GetRange(start, end - start)
                    .Where(IsContentLine)
                    .Select(NormalizeForComparison)
                    .ToList()
                : new List<string>();

            if (!force && existing.SequenceEqual(desired.Select(NormalizeForComparison)) &&
                !_lines[keyIndex].Contains("[]", StringComparison.Ordinal))
                return;

            if (start >= 0)
                _lines.RemoveRange(start, end - start);

            var insertAt = start >= 0 ? start : keyIndex + 1;
            _lines.InsertRange(insertAt, desired);
            var comment = ExtractInlineComment(_lines[keyIndex]);
            _lines[keyIndex] = $"{new string(' ', keyIndent)}{key}:{(desired.Count == 0 ? " []" : string.Empty)}{comment}";
            HasChanges = true;
        }

        public void Save()
        {
            if (!HasChanges)
                return;

            File.WriteAllLines(_fileName, _lines, new UTF8Encoding(false));
        }

        private bool TryFindKey(string section, string key, out int keyIndex, out int sectionEnd)
        {
            keyIndex = -1;
            sectionEnd = -1;

            var (start, end, indent) = FindSectionBounds(section);
            if (start < 0)
                return false;

            sectionEnd = end;
            var childIndent = ChildIndent((start, end, indent));
            var keyRegex = new Regex($@"^(\s*){Regex.Escape(key)}\s*:");
            for (var i = start + 1; i < end; i++)
            {
                if (!IsContentLine(_lines[i]))
                    continue;

                var match = keyRegex.Match(_lines[i]);
                if (match.Success && match.Groups[1].Value.Length == childIndent)
                {
                    keyIndex = i;
                    return true;
                }
            }

            return false;
        }

        private int ChildIndent((int start, int end, int indent) bounds) => _lines
            .Skip(bounds.start + 1).Take(bounds.end - bounds.start - 1)
            .Where(IsContentLine).Select(IndentOf).Where(indent => indent > bounds.indent)
            .DefaultIfEmpty(bounds.indent + 2).Min();

        private (int start, int end, int indent) EnsureSection(string section)
        {
            var bounds = FindSectionBounds(section);
            if (bounds.start >= 0)
                return bounds;
            if (section == "Settings")
                _lines.Add("Settings:");
            else
            {
                var parent = EnsureSection("Settings");
                _lines.Insert(parent.end, $"{new string(' ', ChildIndent(parent))}{section}:");
            }
            HasChanges = true;
            return FindSectionBounds(section);
        }

        private (int start, int end, int indent) FindSectionBounds(string section)
        {
            var sectionRegex = new Regex($@"^(\s*){Regex.Escape(section)}\s*:(?:\s*#.*)?$");
            for (var i = 0; i < _lines.Count; i++)
            {
                var match = sectionRegex.Match(_lines[i]);
                if (!match.Success)
                    continue;

                var indent = match.Groups[1].Value.Length;
                var end = _lines.Count;
                for (var j = i + 1; j < _lines.Count; j++)
                {
                    if (!IsContentLine(_lines[j]))
                        continue;
                    if (IndentOf(_lines[j]) <= indent)
                    {
                        end = j;
                        break;
                    }
                }

                return (i, end, indent);
            }

            return (-1, -1, -1);
        }

        private (int start, int end) FindSequenceBounds(int keyIndex, int keyIndent, int sectionEnd)
        {
            var start = -1;
            for (var i = keyIndex + 1; i < sectionEnd; i++)
            {
                if (!IsContentLine(_lines[i]))
                    continue;
                if (IndentOf(_lines[i]) > keyIndent)
                    start = i;
                break;
            }

            if (start < 0)
                return (-1, -1);

            var end = sectionEnd;
            for (var i = start; i < sectionEnd; i++)
            {
                if (!IsContentLine(_lines[i]))
                    continue;
                if (IndentOf(_lines[i]) <= keyIndent)
                {
                    end = i;
                    break;
                }
            }

            while (end > start && !IsContentLine(_lines[end - 1]))
                end--;

            return (start, end);
        }

        private static string NormalizeForComparison(string line)
        {
            var comment = ExtractInlineComment(line);
            var content = comment.Length > 0 ? line[..^comment.Length] : line;
            var withoutQuotes = content.Replace("'", string.Empty).Replace("\"", string.Empty);
            var tokens = withoutQuotes.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" ", tokens).Replace(" :", ":");
        }

        private static string ExtractInlineComment(string value)
        {
            var inSingleQuote = false;
            var inDoubleQuote = false;
            for (var i = 1; i < value.Length; i++)
            {
                var c = value[i];
                if (c == '\'' && !inDoubleQuote)
                    inSingleQuote = !inSingleQuote;
                else if (c == '"' && !inSingleQuote)
                    inDoubleQuote = !inDoubleQuote;
                else if (c == '#' && !inSingleQuote && !inDoubleQuote && char.IsWhiteSpace(value[i - 1]))
                    return value[(i - 1)..];
            }

            return string.Empty;
        }

        private static bool IsContentLine(string line) =>
            !string.IsNullOrWhiteSpace(line) &&
            !line.TrimStart().StartsWith("#", StringComparison.Ordinal);

        private static int IndentOf(string line)
        {
            var indent = 0;
            while (indent < line.Length && (line[indent] == ' ' || line[indent] == '\t'))
                indent++;

            return indent;
        }
    }
}
