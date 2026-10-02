using System;
using System.Linq;
using LogGrokX.Colors.Configuration;
using LogGrokX.Data;

namespace LogGrokX;

public sealed class ProfileSettings
{
    public string Name { get; set; } = string.Empty;
    public bool? InheritLegacySettings { get; set; }
    public ColorSettings? ColorSettings { get; set; }
    public LogFormat[]? LogFormats { get; set; }

    public bool HasSameConfiguration(ProfileSettings other)
    {
        var rules = ColorSettings?.Rules ?? Array.Empty<ColorRule>();
        var otherRules = other.ColorSettings?.Rules ?? Array.Empty<ColorRule>();
        var formats = LogFormats ?? Array.Empty<LogFormat>();
        var otherFormats = other.LogFormats ?? Array.Empty<LogFormat>();
        return rules.Length == otherRules.Length && formats.Length == otherFormats.Length &&
               rules.Zip(otherRules).All(pair =>
                   pair.First.RegexString == pair.Second.RegexString &&
                   pair.First.ForegroundColor == pair.Second.ForegroundColor &&
                   pair.First.BackgroundColor == pair.Second.BackgroundColor) &&
               formats.Zip(otherFormats).All(pair =>
                   pair.First.Regex == pair.Second.Regex &&
                   pair.First.TimeField == pair.Second.TimeField &&
                   pair.First.TimeFormat == pair.Second.TimeFormat &&
                   pair.First.XorMask == pair.Second.XorMask &&
                   pair.First.IndexedFields.SequenceEqual(pair.Second.IndexedFields) &&
                   pair.First.Transformations.SequenceEqual(pair.Second.Transformations));
    }
}
