using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LogGrokX.Data.Symbols;

// Finds a local copy of a source file compiled on another machine: the original path first, then the path
// relative to each source root, dropping leading directories one by one ("d:\build\src\a\b.cpp" is looked up as
// "<root>\build\src\a\b.cpp", "<root>\src\a\b.cpp", "<root>\a\b.cpp" and "<root>\b.cpp").
public static class SourcePathResolver
{
    public const string SourceServerToken = "srv*";

    public static IReadOnlyList<string> ParseRoots(string? sourcePath) =>
        string.IsNullOrWhiteSpace(sourcePath)
            ? Array.Empty<string>()
            : Environment.ExpandEnvironmentVariables(sourcePath).Split(';')
                .Select(root => root.Trim().Trim('"'))
                .Where(root => root.Length > 0 && !IsSourceServer(root))
                .ToArray();

    public static bool UsesSourceServer(string? sourcePath) =>
        !string.IsNullOrWhiteSpace(sourcePath) &&
        sourcePath.Split(';').Any(root => IsSourceServer(root.Trim()));

    public static string? Find(string compiledPath, IEnumerable<string> roots)
    {
        if (string.IsNullOrWhiteSpace(compiledPath))
            return null;

        if (File.Exists(compiledPath))
            return compiledPath;

        var segments = compiledPath.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        var first = segments.Length > 0 && segments[0].EndsWith(':') ? 1 : 0;
        foreach (var root in roots)
        {
            for (var i = first; i < segments.Length; i++)
            {
                var candidate = Path.Combine([root, .. segments[i..]]);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }

    private static bool IsSourceServer(string root) =>
        root.Equals("srv*", StringComparison.OrdinalIgnoreCase) || root.Equals("srv", StringComparison.OrdinalIgnoreCase);
}
