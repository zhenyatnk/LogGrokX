using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LogGrokX.Data.Symbols;

// Target is the local path the source file is extracted to (or an http(s) URL to download it from);
// Command is the extraction command (SRCSRVCMD), empty for plain downloads.
public sealed record SourceServerTarget(string Target, string? Command);

// Parser of the "srcsrv" PDB stream written by the source indexing tools (see "Source Server" in the
// Debugging Tools for Windows documentation).
public sealed class SourceServerData
{
    private const int MaxExpansionDepth = 32;
    private readonly Dictionary<string, string> _variables = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string[]> _files = new(StringComparer.OrdinalIgnoreCase);

    private SourceServerData()
    {
    }

    public int FileCount => _files.Count;

    public static SourceServerData Parse(string text)
    {
        var data = new SourceServerData();
        var section = string.Empty;
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.StartsWith("SRCSRV:", StringComparison.OrdinalIgnoreCase))
            {
                section = line[7..].Trim().Split(' ')[0].ToLowerInvariant();
                continue;
            }

            if (line.Length == 0)
                continue;

            if (section is "ini" or "variables")
            {
                var separator = line.IndexOf('=');
                if (separator > 0)
                    data._variables[line[..separator].Trim()] = line[(separator + 1)..];
            }
            else if (section == "source")
            {
                var fields = line.Split('*');
                if (fields[0].Length > 0)
                    data._files.TryAdd(Normalize(fields[0]), fields);
            }
        }

        return data;
    }

    public bool TryGetTarget(string compiledPath, string targetRoot, out SourceServerTarget target)
    {
        target = new SourceServerTarget(string.Empty, null);
        if (!_files.TryGetValue(Normalize(compiledPath), out var fields) ||
            !_variables.TryGetValue("SRCSRVTRG", out var targetTemplate))
            return false;

        var location = Expand(targetTemplate, fields, targetRoot, null, 0);
        var command = _variables.TryGetValue("SRCSRVCMD", out var commandTemplate) && commandTemplate.Trim().Length > 0
            ? Expand(commandTemplate, fields, targetRoot, location, 0)
            : null;
        target = new SourceServerTarget(location, command);
        return location.Length > 0;
    }

    private string Expand(string template, string[] fields, string targetRoot, string? target, int depth)
    {
        if (depth > MaxExpansionDepth || template.IndexOf('%') < 0)
            return template;

        var builder = new StringBuilder(template.Length);
        var position = 0;
        while (position < template.Length)
        {
            var start = template.IndexOf('%', position);
            var end = start < 0 ? -1 : template.IndexOf('%', start + 1);
            if (end < 0)
            {
                builder.Append(template, position, template.Length - position);
                break;
            }

            builder.Append(template, position, start - position);
            var name = template.Substring(start + 1, end - start - 1);
            position = end + 1;
            if (name is "fnvar" or "fnbksl" or "fnfile" && position < template.Length && template[position] == '(')
            {
                var close = FindClosingParenthesis(template, position);
                var argument = Expand(template.Substring(position + 1, close - position - 1), fields, targetRoot, target, depth + 1);
                position = Math.Min(close + 1, template.Length);
                builder.Append(name switch
                {
                    "fnvar" => _variables.TryGetValue(argument, out var value)
                        ? Expand(value, fields, targetRoot, target, depth + 1)
                        : string.Empty,
                    "fnbksl" => argument.Replace('/', '\\'),
                    _ => GetFileName(argument)
                });
                continue;
            }

            builder.Append(ResolveVariable(name, fields, targetRoot, target, depth) ?? template.Substring(start, end - start + 1));
        }

        return builder.ToString();
    }

    private string? ResolveVariable(string name, string[] fields, string targetRoot, string? target, int depth)
    {
        if (name.Length > 3 && name.StartsWith("var", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(name.AsSpan(3), out var index))
            return index >= 1 && index <= fields.Length ? fields[index - 1] : string.Empty;
        if (name.Equals("targ", StringComparison.OrdinalIgnoreCase))
            return targetRoot;
        if (name.Equals("srcsrvtrg", StringComparison.OrdinalIgnoreCase) && target != null)
            return target;
        return _variables.TryGetValue(name, out var value) ? Expand(value, fields, targetRoot, target, depth + 1) : null;
    }

    private static int FindClosingParenthesis(string text, int open)
    {
        var level = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '(')
                level++;
            else if (text[i] == ')' && --level == 0)
                return i;
        }

        return text.Length;
    }

    private static string GetFileName(string path)
    {
        var separator = path.LastIndexOfAny(['\\', '/']);
        return separator < 0 ? path : path[(separator + 1)..];
    }

    private static string Normalize(string path) => path.Trim().Replace('/', '\\');
}
