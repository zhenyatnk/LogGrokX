using System;
using System.Collections.Generic;
using System.Linq;

namespace LogGrokX.Settings
{
    public sealed class ColorRuleData
    {
        public string RegexString { get; init; } = string.Empty;
        public string ForegroundColor { get; init; } = string.Empty;
        public string BackgroundColor { get; init; } = string.Empty;
    }

    public sealed class LogFormatData
    {
        public string Regex { get; init; } = string.Empty;
        public IReadOnlyList<string> IndexedFields { get; init; } = Array.Empty<string>();
        public string TimeField { get; init; } = string.Empty;
        public string TimeFormat { get; init; } = string.Empty;
        public IReadOnlyList<string> Transformations { get; init; } = Array.Empty<string>();
        public string XorMask { get; init; } = string.Empty;
    }

    public static class SettingsYamlRenderer
    {
        public static IReadOnlyList<string> RenderProfiles(IEnumerable<ProfileViewModel> profiles, int itemIndent)
        {
            var lines = new List<string>();
            var pad = new string(' ', itemIndent);
            foreach (var profile in profiles)
            {
                lines.Add($"{pad}- Name: {FormatScalar(profile.Name.Trim())}");
                lines.Add($"{pad}  InheritLegacySettings: false");
                lines.Add($"{pad}  ColorSettings:");
                lines.Add($"{pad}    Rules:{(profile.ColorRules.Count == 0 ? " []" : string.Empty)}");
                lines.AddRange(RenderColorRules(profile.ColorRules.Select(rule => rule.ToData()), itemIndent + 6));
                lines.Add($"{pad}  LogFormats:{(profile.LogFormats.Count == 0 ? " []" : string.Empty)}");
                lines.AddRange(RenderLogFormats(profile.LogFormats.Select(format => format.ToData()), itemIndent + 4));
            }
            return lines;
        }

        public static IReadOnlyList<string> RenderColorRules(IEnumerable<ColorRuleData> rules, int itemIndent)
        {
            var lines = new List<string>();
            var itemPad = new string(' ', itemIndent);
            var fieldPad = new string(' ', itemIndent + 2);

            foreach (var rule in rules)
            {
                lines.Add($"{itemPad}- RegexString: {FormatScalar(rule.RegexString)}");
                if (!string.IsNullOrEmpty(rule.ForegroundColor))
                    lines.Add($"{fieldPad}ForegroundColor: {FormatScalar(rule.ForegroundColor)}");
                if (!string.IsNullOrEmpty(rule.BackgroundColor))
                    lines.Add($"{fieldPad}BackgroundColor: {FormatScalar(rule.BackgroundColor)}");
            }

            return lines;
        }

        public static IReadOnlyList<string> RenderLogFormats(IEnumerable<LogFormatData> formats, int itemIndent)
        {
            var lines = new List<string>();
            var itemPad = new string(' ', itemIndent);
            var fieldPad = new string(' ', itemIndent + 2);
            var listPad = new string(' ', itemIndent + 4);

            foreach (var format in formats)
            {
                lines.Add($"{itemPad}- Regex: {FormatScalar(format.Regex)}");

                if (format.IndexedFields.Count > 0)
                {
                    lines.Add($"{fieldPad}IndexedFields:");
                    foreach (var field in format.IndexedFields)
                        lines.Add($"{listPad}- {FormatScalar(field)}");
                }

                if (!string.IsNullOrEmpty(format.TimeField))
                    lines.Add($"{fieldPad}TimeField: {FormatScalar(format.TimeField)}");
                if (!string.IsNullOrEmpty(format.TimeFormat))
                    lines.Add($"{fieldPad}TimeFormat: {FormatScalar(format.TimeFormat)}");

                if (format.Transformations.Count > 0)
                {
                    lines.Add($"{fieldPad}Transformations:");
                    foreach (var transformation in format.Transformations)
                        lines.Add($"{listPad}- {FormatScalar(transformation)}");
                }

                if (!string.IsNullOrEmpty(format.XorMask))
                    lines.Add($"{fieldPad}XorMask: {FormatScalar(format.XorMask)}");
            }

            return lines;
        }

        public static string FormatScalar(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return "''";

            return NeedsQuoting(value) ? $"'{value.Replace("'", "''")}'" : value;
        }

        private static bool NeedsQuoting(string value)
        {
            if (value != value.Trim())
                return true;
            if (value.Contains(": ", StringComparison.Ordinal) || value.Contains(" #", StringComparison.Ordinal))
                return true;
            if ("-?:,[]{}#&*!|>'\"%@`".IndexOf(value[0]) >= 0)
                return true;

            switch (value.ToLowerInvariant())
            {
                case "null":
                case "~":
                case "true":
                case "false":
                case "yes":
                case "no":
                case "on":
                case "off":
                case "y":
                case "n":
                    return true;
            }

            return false;
        }
    }
}
