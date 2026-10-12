using System;
using System.Collections.Generic;
using System.Globalization;

namespace LogGrokX.Localization;

public static class LocalizationService
{
    private static readonly CultureInfo SystemCulture = CultureInfo.CurrentUICulture;

    public static IReadOnlyList<LanguageOption> Languages { get; } =
    [
        new("system", "LanguageSystem"),
        new("en", "English"),
        new("ru", "Русский"),
        new("de", "Deutsch"),
        new("fr", "Français"),
        new("es", "Español"),
        new("ja", "日本語"),
        new("pl", "Polski"),
        new("pt-BR", "Português (Brasil)")
    ];

    public static string NormalizeLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language) || language.Equals("system", StringComparison.OrdinalIgnoreCase))
            return "system";

        try
        {
            return CultureInfo.GetCultureInfo(language.Trim()).TwoLetterISOLanguageName switch
            {
                "en" => "en",
                "ru" => "ru",
                "de" => "de",
                "fr" => "fr",
                "es" => "es",
                "ja" => "ja",
                "pl" => "pl",
                "pt" => "pt-BR",
                _ => "system"
            };
        }
        catch (CultureNotFoundException)
        {
            return "system";
        }
    }

    public static CultureInfo ResolveCulture(string? language, CultureInfo systemCulture)
    {
        var normalized = NormalizeLanguage(language);
        if (normalized == "system")
            normalized = NormalizeLanguage(systemCulture.Name);
        return CultureInfo.GetCultureInfo(normalized == "system" ? "en" : normalized);
    }

    public static void Initialize(string? language)
    {
        var culture = ResolveCulture(language, SystemCulture);
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    public static string InstallerLanguage => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName switch
    {
        "ru" => "russian",
        "de" => "german",
        "fr" => "french",
        "es" => "spanish",
        "ja" => "japanese",
        "pl" => "polish",
        "pt" => "brazilianportuguese",
        _ => "english"
    };
}

public sealed record LanguageOption(string Value, string Name)
{
    public string Title => Value == "system" ? Strings.Get(Name) : Name;
}
