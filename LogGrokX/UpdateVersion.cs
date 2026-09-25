using System;

namespace LogGrokX;

public static class UpdateVersion
{
    public static Version? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var value = text.Trim();
        if (value.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            value = value.Substring(1);

        var end = 0;
        while (end < value.Length && (char.IsDigit(value[end]) || value[end] == '.'))
            end++;
        value = value.Substring(0, end).Trim('.');

        if (!value.Contains('.'))
            value += ".0";

        return Version.TryParse(value, out var version) ? Normalize(version) : null;
    }

    public static bool IsNewer(string? latest, string? current)
    {
        var latestVersion = Parse(latest);
        var currentVersion = Parse(current);
        return latestVersion != null && currentVersion != null && latestVersion > currentVersion;
    }

    private static Version Normalize(Version version) =>
        new(version.Major, version.Minor, Math.Max(version.Build, 0), Math.Max(version.Revision, 0));
}
