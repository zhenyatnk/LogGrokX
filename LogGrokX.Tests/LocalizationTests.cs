using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using LogGrokX.Localization;
using LogGrokX.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Tests;

[TestClass]
[DoNotParallelize]
public sealed class LocalizationTests
{
    [TestMethod]
    [DataRow("en-US", "en", "english")]
    [DataRow("ru-RU", "ru", "russian")]
    [DataRow("de-DE", "de", "german")]
    [DataRow("fr-CA", "fr", "french")]
    [DataRow("es-MX", "es", "spanish")]
    [DataRow("ja-JP", "ja", "japanese")]
    [DataRow("pl-PL", "pl", "polish")]
    [DataRow("pt-PT", "pt-BR", "brazilianportuguese")]
    public void RegionalLanguagesResolveToSupportedCatalogAndInstaller(string regional, string expected, string installer)
    {
        Assert.AreEqual(expected, LocalizationService.NormalizeLanguage(regional));
        Assert.AreEqual(expected, LocalizationService.ResolveCulture("system", CultureInfo.GetCultureInfo(regional)).Name);
        using var culture = new CultureScope(expected);
        Assert.AreEqual(installer, LocalizationService.InstallerLanguage);
    }

    [TestMethod]
    public void UnsupportedSystemCultureFallsBackToEnglish()
    {
        Assert.AreEqual("en", LocalizationService.ResolveCulture("system", CultureInfo.GetCultureInfo("it-IT")).Name);
        Assert.AreEqual("de", LocalizationService.ResolveCulture("de", CultureInfo.GetCultureInfo("ru-RU")).Name);
        Assert.AreEqual("system", LocalizationService.NormalizeLanguage(null));
        Assert.AreEqual("system", LocalizationService.NormalizeLanguage("invalid_language"));
    }

    [TestMethod]
    public void StartupCultureAppliesToUiThreadsAndKeepsDataCulture()
    {
        var previousUi = CultureInfo.CurrentUICulture;
        var previousDefault = CultureInfo.DefaultThreadCurrentUICulture;
        var dataCulture = CultureInfo.CurrentCulture;
        try
        {
            LocalizationService.Initialize("de-DE");
            Assert.AreEqual("de", CultureInfo.CurrentUICulture.Name);
            Assert.AreEqual("de", CultureInfo.DefaultThreadCurrentUICulture.Name);
            Assert.AreEqual(dataCulture, CultureInfo.CurrentCulture);
        }
        finally
        {
            CultureInfo.DefaultThreadCurrentUICulture = previousDefault;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }

    [TestMethod]
    public void EveryLanguageHasCompleteCatalogAndMatchingFormatArguments()
    {
        var neutral = Strings.Resources.GetResourceSet(CultureInfo.InvariantCulture, true, true);
        Assert.IsNotNull(neutral);
        var expected = neutral.Cast<DictionaryEntry>().ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value);

        foreach (var language in LocalizationService.Languages.Where(language => language.Value != "system"))
        {
            var translated = Strings.Resources.GetResourceSet(CultureInfo.GetCultureInfo(language.Value), true, false);
            Assert.IsNotNull(translated, language.Value);
            var actual = translated.Cast<DictionaryEntry>().ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value);
            CollectionAssert.AreEquivalent(expected.Keys.ToArray(), actual.Keys.ToArray(), language.Value);
            foreach (var (key, text) in actual)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(text), $"{language.Value}: {key}");
                Assert.AreEqual(CompositeFormat.Parse(expected[key]).MinimumArgumentCount,
                    CompositeFormat.Parse(text).MinimumArgumentCount, $"{language.Value}: {key}");
                CollectionAssert.AreEquivalent(Arguments(expected[key]), Arguments(text), $"{language.Value}: {key}");
            }
        }
    }

    [TestMethod]
    [DataRow("en", "Settings")]
    [DataRow("ru", "Настройки")]
    [DataRow("de", "Einstellungen")]
    [DataRow("fr", "Paramètres")]
    [DataRow("es", "Configuración")]
    [DataRow("ja", "設定")]
    [DataRow("pl", "Ustawienia")]
    [DataRow("pt-BR", "Configurações")]
    public void MarkupAndSettingsUseSelectedCatalog(string language, string title)
    {
        using var culture = new CultureScope(language);
        Assert.AreEqual(title, new LocExtension("Settings").ProvideValue(null));
        var settings = new ViewSettingsViewModel(new ViewSettings { Language = language });
        Assert.AreEqual(language, settings.Language);
        Assert.AreEqual(9, settings.LanguageOptions.Count);
        Assert.AreEqual(Strings.Get("UpdatesDisabled"), settings.UpdateModeOptions[0].Title);
        Assert.AreEqual(Strings.Get("BigLinePrune"), settings.BigLineOptions[0].Title);
    }

    [TestMethod]
    [DataRow("ru", 1L, "1 строка")]
    [DataRow("ru", 2L, "2 строки")]
    [DataRow("ru", 5L, "5 строк")]
    [DataRow("ru", 11L, "11 строк")]
    [DataRow("ru", 21L, "21 строка")]
    [DataRow("ru", 112L, "112 строк")]
    [DataRow("pl", 1L, "1 wiersz")]
    [DataRow("pl", 22L, "22 wiersze")]
    [DataRow("pl", 21L, "21 wierszy")]
    [DataRow("ja", 2L, "2 行")]
    public void LineCountsUseLanguageGrammar(string language, long count, string expected)
    {
        using var culture = new CultureScope(language);
        Assert.AreEqual(expected, Strings.LineCount(count));
    }

    [TestMethod]
    public void DisplayLocalizationKeepsLogCulture()
    {
        var current = CultureInfo.CurrentCulture;
        using var culture = new CultureScope("ru");
        Assert.AreEqual(current, CultureInfo.CurrentCulture);
        Assert.AreEqual("1,5 с", DurationFormatter.Format(TimeSpan.FromSeconds(1.5).Ticks));
    }

    [TestMethod]
    [DataRow("ru")]
    [DataRow("de")]
    [DataRow("ja")]
    public void SavingLanguageUsesStableYamlKeysAndRequiresRestart(string language)
    {
        using var culture = new CultureScope(language);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".yaml");
        var previousDetection = BinaryDetectionOptions.Current;
        try
        {
            File.WriteAllText(path, "Settings:\n  ViewSettings:\n    BigLineSize: 4096 # keep\n");
            var settings = ApplicationSettings.LoadFromFile(path);
            var editor = ProfileSettingsTests.CreateEditor(settings);
            editor.View.Language = language;
            Assert.IsTrue(editor.RequiresRestart);
            Assert.IsTrue(editor.Save(), editor.ValidationMessage);
            var saved = File.ReadAllText(path);
            StringAssert.Contains(saved, "Language: " + language);
            StringAssert.Contains(saved, "# keep");
            Assert.AreEqual(language, ApplicationSettings.LoadFromFile(path).ViewSettings.Language);
            Assert.AreEqual(language, settings.ViewSettings.Language);
        }
        finally
        {
            BinaryDetectionOptions.Current = previousDetection;
            File.Delete(path);
        }
    }

    private static string[] Arguments(string value) => Regex.Matches(value, @"\{(\d+)(?:[^}]*)\}")
        .Select(match => match.Groups[1].Value).Distinct().ToArray();

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentUICulture;

        public CultureScope(string name) => CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);

        public void Dispose() => CultureInfo.CurrentUICulture = _previous;
    }
}
