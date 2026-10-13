using System;
using System.Globalization;
using System.Resources;

namespace LogGrokX.Localization;

public static class Strings
{
    internal static ResourceManager Resources { get; } = new("LogGrokX.Localization.Strings", typeof(Strings).Assembly);

    public static string Get(string key) =>
        Resources.GetString(key, CultureInfo.CurrentUICulture) ?? throw new ArgumentException($"Unknown localization key: {key}", nameof(key));

    public static string Format(string key, params object[] arguments) =>
        string.Format(CultureInfo.CurrentUICulture, Get(key), arguments);

    public static string LineCount(long count)
    {
        var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var last = count % 10;
        var lastTwo = count % 100;
        var form = language switch
        {
            "ru" when last == 1 && lastTwo != 11 => "One",
            "ru" when last is >= 2 and <= 4 && lastTwo is not (>= 12 and <= 14) => "Few",
            "pl" when count == 1 => "One",
            "pl" when last is >= 2 and <= 4 && lastTwo is not (>= 12 and <= 14) => "Few",
            _ when count == 1 => "One",
            _ => "Many"
        };
        return Format("LineCount" + form, count);
    }
}
