#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using LogGrokX.Colors.Configuration;
using LogGrokX.Data;
using LogGrokX.Data.Monikers;
using LogGrokX.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Tests;

[TestClass]
public class ProfileSettingsTests
{
    private string _path = string.Empty;
    private BinaryDetectionOptions _initialBinaryDetection = BinaryDetectionOptions.All;

    [TestInitialize]
    public void Setup()
    {
        _path = Path.Combine(Path.GetTempPath(), $"loggrokx-profiles-{Guid.NewGuid():N}.yaml");
        _initialBinaryDetection = BinaryDetectionOptions.Current;
    }

    [TestCleanup]
    public void Cleanup()
    {
        File.Delete(_path);
        BinaryDetectionOptions.Current = _initialBinaryDetection;
    }

    [TestMethod]
    public void LegacyConfigurationBecomesDefaultProfile()
    {
        File.WriteAllText(_path, "Settings:\n  ColorSettings:\n    Rules:\n      - RegexString: error\n  LogFormats:\n    - Regex: ^(?<Message>.*)\n");
        var settings = ApplicationSettings.LoadFromFile(_path);
        var profile = settings.GetSelectedProfile();
        Assert.AreEqual("Default", profile.Name);
        Assert.AreEqual("error", profile.ColorSettings!.Rules.Single().RegexString);
        Assert.AreEqual("^(?<Message>.*)", profile.LogFormats!.Single().Regex);
    }

    [TestMethod]
    public void OmittedSectionsInheritLegacyUnlessExplicitlyDisabled()
    {
        File.WriteAllText(_path, "Settings:\n  LogFormats:\n    - Regex: ^legacy$\n  Profiles:\n    - Name: Inherited\n    - Name: Empty\n      InheritLegacySettings: false\n");
        var profiles = ApplicationSettings.LoadFromFile(_path).GetProfiles();
        Assert.AreEqual("^legacy$", profiles[0].LogFormats!.Single().Regex);
        Assert.AreEqual(0, profiles[1].LogFormats!.Length);
        Assert.AreEqual(0, profiles[1].ColorSettings!.Rules.Length);
    }

    [TestMethod]
    public void UnknownSelectionFallsBackAndNamesAreCaseInsensitive()
    {
        var settings = new ApplicationSettings(_path)
        {
            Profiles = [new() { Name = "First" }, new() { Name = "first" }, new() { Name = "Second" }],
            SelectedProfile = "missing"
        };
        Assert.AreEqual(2, settings.GetProfiles().Count);
        Assert.AreEqual("First", settings.GetSelectedProfile().Name);
        settings.SelectedProfile = "SECOND";
        Assert.AreEqual("Second", settings.GetSelectedProfile().Name);
    }

    [TestMethod]
    public void SelectedProfileIsSavedWithoutChangingProfileComments()
    {
        File.WriteAllText(_path, "Settings:\n  Profiles:\n    - Name: First # keep this\n    - Name: Second\n  ViewSettings:\n    BigLineSize: 4096 # keep too\n");
        var settings = ApplicationSettings.LoadFromFile(_path);
        settings.SetSelectedProfile("Second");
        var text = File.ReadAllText(_path);
        StringAssert.Contains(text, "First # keep this");
        StringAssert.Contains(text, "4096 # keep too");
        Assert.AreEqual("Second", ApplicationSettings.LoadFromFile(_path).GetSelectedProfile().Name);
    }

    [TestMethod]
    public void EditingProfilesRoundTripsAndKeepsUnrelatedSettings()
    {
        File.WriteAllText(_path, "Settings:\n  LogFormats:\n    - Regex: ^(?<Message>.*)\n  ViewSettings:\n    BigLineSize: 4096 # keep this\n  CustomValue: keep-me\n");
        var settings = ApplicationSettings.LoadFromFile(_path);
        var editor = CreateEditor(settings);
        editor.SelectedProfile.Name = "My profile: 'one'";
        editor.ColorRules.Add(new ColorRuleViewModel(new ColorRule { RegexString = "error", ForegroundColor = "Red" }));
        editor.LogFormats[0].TimeField = "Time";
        editor.LogFormats[0].TimeFormat = "HH:mm:ss.fff";
        editor.DuplicateProfileCommand.Execute(null);
        editor.SelectedProfile.Name = "Empty";
        editor.ColorRules.Clear();
        editor.LogFormats.Clear();
        Assert.IsFalse(editor.RequiresRestart);
        editor.View.DetectBinary = false;
        editor.View.DetectPem = false;
        editor.View.DetectBase64 = false;
        editor.View.DetectHex = false;
        Assert.IsTrue(editor.RequiresRestart);
        Assert.IsTrue(editor.Save(), editor.ValidationMessage);
        Assert.IsFalse(editor.RequiresRestart);

        var loaded = ApplicationSettings.LoadFromFile(_path);
        Assert.IsFalse(loaded.ViewSettings.DetectBinary);
        Assert.IsFalse(loaded.ViewSettings.DetectPem);
        Assert.IsFalse(loaded.ViewSettings.DetectBase64);
        Assert.IsFalse(loaded.ViewSettings.DetectHex);
        var profiles = loaded.GetProfiles();
        Assert.AreEqual(2, profiles.Count);
        Assert.AreEqual("My profile: 'one'", profiles[0].Name);
        Assert.AreEqual("Red", profiles[0].ColorSettings!.Rules.Single().ForegroundColor);
        Assert.AreEqual("Time", profiles[0].LogFormats!.Single().TimeField);
        Assert.AreEqual("Empty", loaded.GetSelectedProfile().Name);
        Assert.AreEqual(0, profiles[1].LogFormats!.Length);
        Assert.AreEqual(0, profiles[1].ColorSettings!.Rules.Length);
        StringAssert.Contains(File.ReadAllText(_path), "4096 # keep this");
        StringAssert.Contains(File.ReadAllText(_path), "CustomValue: keep-me");
    }

    [TestMethod]
    public void SwitchingEditorProfilesKeepsIndependentUnsavedChanges()
    {
        var editor = CreateEditor(new ApplicationSettings(_path));
        var first = editor.SelectedProfile;
        editor.ColorRules.Add(new ColorRuleViewModel { RegexString = "first" });
        editor.AddProfileCommand.Execute(null);
        var second = editor.SelectedProfile;
        editor.ColorRules.Add(new ColorRuleViewModel { RegexString = "second" });
        editor.SelectedProfile = first;
        Assert.AreEqual("first", editor.ColorRules.Single().RegexString);
        editor.SelectedProfile = second;
        Assert.AreEqual("second", editor.ColorRules.Single().RegexString);
    }

    [TestMethod]
    public void InvalidNamesAndFormatsInUnselectedProfilePreventSave()
    {
        var editor = CreateEditor(new ApplicationSettings(_path));
        var first = editor.SelectedProfile;
        editor.AddProfileCommand.Execute(null);
        editor.SelectedProfile.Name = first.Name.ToLowerInvariant();
        Assert.IsFalse(editor.Save());
        editor.SelectedProfile.Name = "Other";
        editor.LogFormats.Add(new LogFormatViewModel { Regex = "[" });
        editor.SelectedProfile = first;
        Assert.IsFalse(editor.Save());
        Assert.IsFalse(File.Exists(_path));
    }

    [TestMethod]
    public void RemovingLastProfileKeepsOneProfile()
    {
        var editor = CreateEditor(new ApplicationSettings(_path));
        editor.RemoveProfileCommand.Execute(null);
        Assert.AreEqual(1, editor.Profiles.Count);
        editor.AddProfileCommand.Execute(null);
        editor.RemoveProfileCommand.Execute(null);
        Assert.AreEqual(1, editor.Profiles.Count);
        Assert.AreSame(editor.Profiles[0], editor.SelectedProfile);
    }

    [TestMethod]
    public void SavingUnchangedProfilesPreservesTheirComments()
    {
        File.WriteAllText(_path, "Settings:\n  Profiles:\n    - Name: First # name comment\n      LogFormats:\n        - Regex: ^(?<Message>.*) # format comment\n");
        var editor = CreateEditor(ApplicationSettings.LoadFromFile(_path));
        Assert.IsTrue(editor.Save(), editor.ValidationMessage);
        StringAssert.Contains(File.ReadAllText(_path), "First # name comment");
        StringAssert.Contains(File.ReadAllText(_path), "# format comment");
    }

    [TestMethod]
    public void DefaultProfilesParseVennTimeAndKeepPlainTextIndependent()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "LogGrokX", "appsettings.yaml"));
        var settings = ApplicationSettings.LoadFromFile(path);
        Assert.AreEqual(0, settings.LogFormats.Length);
        Assert.AreEqual(0, settings.ColorSettings.Rules.Length);
        var profiles = settings.GetProfiles();
        Assert.AreEqual(3, profiles.Count);
        var venn = profiles.Single(profile => profile.Name == "Venn");
        var format = venn.LogFormats!.Single();
        const string line = "2026-07-22 9:01:02.123 - [ERRO] - [123:456:789:M:U] - [agent.exe] - [irql 2] - [driver.cpp:42] - operation failed";
        var parser = new RegexBasedLineParser(new LogMetaInformation(format));
        var metadata = new LineMetaInformation(new int[LineMetaInformation.GetSizeInts(format.FieldNames.Length)],
            format.FieldNames.Length);
        Assert.IsTrue(parser.TryParse(line, 0, line.Length, metadata.ParsedLineComponents, out var ticks));
        Assert.AreEqual(new DateTime(2026, 7, 22, 9, 1, 2, 123).Ticks, ticks);
        Assert.AreEqual("789", Regex.Match(line, format.Regex).Groups["Tid"].Value);
        var plain = profiles.Single(profile => profile.Name == "Plain text");
        Assert.AreEqual(0, plain.ColorSettings!.Rules.Length);
        CollectionAssert.AreEqual(new[] { "Text" }, plain.LogFormats!.Single().FieldNames);
    }

    [TestMethod]
    [DataRow("\tERR\t failure", "Red", null)]
    [DataRow("prefix error suffix", "Red", null)]
    [DataRow("prefix error: failure", "Red", null)]
    [DataRow("error", null, null)]
    [DataRow("prefix error", null, null)]
    [DataRow("error suffix", null, null)]
    [DataRow("prefix ERROR suffix", null, null)]
    [DataRow("prefix terror suffix", null, null)]
    [DataRow("prefix error_code suffix", null, null)]
    [DataRow("No error\tERR\t", null, null)]
    [DataRow("error: 0x0\tERR\t", null, null)]
    [DataRow("\tERR\t fatal", "Red", null)]
    [DataRow("\tWRN\t warning", "Chocolate", null)]
    [DataRow("fatal problem", null, "#FFCD5C5C")]
    public void KasperskyHighlightingKeepsMatchingAndRulePriority(string line,
        string? foreground, string? background)
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "LogGrokX", "appsettings.yaml"));
        var profile = ApplicationSettings.LoadFromFile(path).GetProfiles()
            .Single(profile => profile.Name == "Kaspersky");
        var colors = new Colors.ColorSettings(profile.ColorSettings!);
        var match = colors.Rules.FirstOrDefault(rule => rule.IsMatch(line));

        System.Windows.Media.Color? ParseColor(string? value) => value == null ? null :
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(value);

        Assert.AreEqual(ParseColor(foreground), match?.ForegroundColor);
        Assert.AreEqual(ParseColor(background), match?.BackgroundColor);
    }

    [TestMethod]
    public async Task ProfilesReloadWhenConfigurationFileChanges()
    {
        File.WriteAllText(_path, "Settings:\n  Profiles:\n    - Name: First\n");
        var settings = ApplicationSettings.LoadFromFile(_path, reloadOnChange: true);
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        settings.ProfilesChanged += () => changed.TrySetResult();
        File.WriteAllText(_path, "Settings:\n  SelectedProfile: Second\n  Profiles:\n    - Name: Second\n  ViewSettings:\n    DetectBinary: false\n    DetectPem: false\n    DetectBase64: false\n    DetectHex: false\n");
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual("Second", settings.GetSelectedProfile().Name);
        Assert.IsFalse(settings.ViewSettings.DetectBinary);
        Assert.IsFalse(settings.ViewSettings.DetectPem);
        Assert.IsFalse(settings.ViewSettings.DetectBase64);
        Assert.IsFalse(settings.ViewSettings.DetectHex);
    }

    internal static SettingsViewModel CreateEditor(ApplicationSettings settings) => new(settings,
        new TimelinePlacementService(settings), new ThreadGroupingService(settings),
        new MergedFilesViewService(settings), new TextZoomService(settings));
}
