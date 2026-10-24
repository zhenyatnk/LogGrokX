using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using LogGrokX.Platform;

namespace LogGrokX.Colors;

public sealed record ColorRuleDefinition(Regex Regex, string ForegroundColor, string BackgroundColor)
{
    public bool IsMatch(string text) => Regex.IsMatch(text);
}

public class ColorRuleSet
{
    private static readonly ConcurrentDictionary<string, Regex> Regexes = new();
    public IReadOnlyList<ColorRuleDefinition> Rules { get; }

    public ColorRuleSet(Configuration.ColorSettings configuration, IColorValidator? validator = null)
    {
        Rules = configuration.Rules.Select(rule =>
        {
            if (validator != null && (!validator.IsValid(rule.ForegroundColor) || !validator.IsValid(rule.BackgroundColor)))
                throw new FormatException("Invalid color.");
            return new ColorRuleDefinition(Regexes.GetOrAdd(rule.RegexString, pattern => new Regex(pattern, RegexOptions.Compiled)),
                rule.ForegroundColor, rule.BackgroundColor);
        }).ToArray();
    }
}
