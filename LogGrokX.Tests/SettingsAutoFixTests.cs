#nullable enable
using System;
using System.IO;
using System.Linq;
using LogGrokX.Data;
using LogGrokX.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Tests;

[TestClass]
[DoNotParallelize]
public sealed class SettingsAutoFixTests
{
    [TestMethod]
    public void FixAndSaveRepairsFixableProblems()
    {
        WithSettings(dialog =>
        {
            dialog.View.BigLineSizeText = "abc";
            dialog.Debug.MaxDumpsCountText = "0";

            var first = dialog.SelectedProfile;
            var firstName = first.Name.Trim();
            dialog.AddProfileCommand.Execute(null);
            var duplicate = dialog.SelectedProfile;
            duplicate.Name = firstName.ToUpperInvariant();
            dialog.AddProfileCommand.Execute(null);
            var unnamed = dialog.SelectedProfile;
            unnamed.Name = " ";

            var format = new LogFormatViewModel
            {
                Regex = @"(?<Time>\S+) (?<Thread>\d+) (?<Message>.*)",
                IndexedFields = "thread\nMissing",
                Transformations = "[\n\\d+",
                XorMask = "zz"
            };
            first.LogFormats.Add(format);

            var rule = new ColorRuleViewModel { RegexString = "[ERROR", ForegroundColor = "notacolor", BackgroundColor = "Red" };
            first.ColorRules.Add(rule);

            var mapping = new ValueMappingListViewModel(new ValueMappingList
            {
                Name = "Codes",
                Enabled = false,
                Items =
                [
                    new ValueMappingItem { Value = "A", Mapping = "1" },
                    new ValueMappingItem { Value = "A", Mapping = "2" },
                    new ValueMappingItem { Value = "", Mapping = "3" }
                ]
            });
            dialog.ValueMappings.Add(mapping);

            Assert.IsFalse(dialog.Save());
            Assert.IsTrue(dialog.HasValidationMessage);
            Assert.IsTrue(dialog.HasAutoFixes);
            Assert.AreEqual(11, dialog.AutoFixes.Count, string.Join(Environment.NewLine, dialog.AutoFixes));

            Assert.IsTrue(dialog.FixAndSave(), dialog.ValidationMessage);
            Assert.IsFalse(dialog.HasValidationMessage);
            Assert.IsFalse(dialog.HasAutoFixes);

            Assert.AreEqual("9728", dialog.View.BigLineSizeText);
            Assert.AreEqual("10", dialog.Debug.MaxDumpsCountText);
            Assert.AreEqual(firstName, first.Name);
            Assert.AreEqual($"{firstName.ToUpperInvariant()} 2", duplicate.Name);
            Assert.IsFalse(string.IsNullOrWhiteSpace(unnamed.Name));
            Assert.AreEqual(3, dialog.Profiles.Select(profile => profile.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());

            CollectionAssert.AreEqual(new[] { "Thread" }, format.IndexedFieldsList.ToArray());
            CollectionAssert.AreEqual(new[] { @"\d+" }, format.TransformationsList.ToArray());
            Assert.AreEqual(string.Empty, format.XorMask);

            Assert.AreEqual(@"\[ERROR", rule.RegexString);
            Assert.AreEqual(string.Empty, rule.ForegroundColor);
            Assert.AreEqual("Red", rule.BackgroundColor);

            Assert.AreEqual(1, mapping.Items.Count);
            Assert.AreEqual("A", mapping.Items[0].Value);
            Assert.AreEqual("1", mapping.Items[0].Mapping);
        });
    }

    [TestMethod]
    public void InvalidLogRegexIsNotFixedAutomatically()
    {
        WithSettings(dialog =>
        {
            dialog.SelectedProfile.LogFormats.Add(new LogFormatViewModel { Regex = "(" });

            Assert.IsFalse(dialog.Save());
            Assert.IsFalse(dialog.HasAutoFixes);
            Assert.IsFalse(dialog.FixAndSave());
            Assert.IsTrue(dialog.HasValidationMessage);
            Assert.AreEqual("(", dialog.SelectedProfile.LogFormats[^1].Regex);
        });
    }

    [TestMethod]
    public void FixAndSaveKeepsRemainingErrors()
    {
        WithSettings(dialog =>
        {
            dialog.View.BigLineSizeText = "-1";
            dialog.SelectedProfile.LogFormats.Add(new LogFormatViewModel { Regex = "(" });

            Assert.IsFalse(dialog.Save());
            Assert.AreEqual(1, dialog.AutoFixes.Count);
            Assert.IsFalse(dialog.FixAndSave());
            Assert.AreEqual("9728", dialog.View.BigLineSizeText);
            Assert.IsTrue(dialog.HasValidationMessage);
            Assert.IsFalse(dialog.HasAutoFixes);
        });
    }

    [TestMethod]
    public void ProblemsPointToTheirFields()
    {
        WithSettings(dialog =>
        {
            dialog.View.BigLineSizeText = "abc";
            dialog.Debug.MaxDumpsCountText = "0";
            var first = dialog.SelectedProfile;
            var format = new LogFormatViewModel { Regex = @"(?<Thread>\d+)", IndexedFields = "Missing", XorMask = "zz" };
            first.LogFormats.Add(format);
            var rule = new ColorRuleViewModel { RegexString = "[", BackgroundColor = "notacolor" };
            first.ColorRules.Add(rule);
            var mapping = new ValueMappingListViewModel(new ValueMappingList
            {
                Name = "Codes",
                Enabled = false,
                Items = [new ValueMappingItem { Value = "A", Mapping = "1" }, new ValueMappingItem { Value = "A", Mapping = "2" }]
            });
            dialog.ValueMappings.Add(mapping);
            dialog.AddProfileCommand.Execute(null);
            var second = dialog.SelectedProfile;
            second.Name = first.Name;

            Assert.IsFalse(dialog.Save());
            CollectionAssert.AreEquivalent(new[]
            {
                SettingsField.BigLineSize, SettingsField.MaxDumpsCount, SettingsField.ProfileName,
                SettingsField.IndexedFields, SettingsField.XorMask, SettingsField.ColorRegex,
                SettingsField.BackgroundColor, SettingsField.MappingValue
            }, dialog.Problems.Select(problem => problem.Field).ToArray());
            Assert.AreSame(second, dialog.Problems.Single(problem => problem.Field == SettingsField.ProfileName).Profile);
            Assert.AreSame(format, dialog.Problems.Single(problem => problem.Field == SettingsField.XorMask).LogFormat);
            Assert.AreSame(mapping.Items[1], dialog.Problems.Single(problem => problem.Field == SettingsField.MappingValue).MappingItem);
            Assert.IsTrue(dialog.AutoFixes.All(fix => fix.CanNavigate));
            Assert.AreEqual(string.Join(Environment.NewLine, dialog.Problems.Select(problem => problem.Message)), dialog.ValidationMessage);
        });
    }

    [TestMethod]
    public void NavigateToSelectsProblemTarget()
    {
        WithSettings(dialog =>
        {
            var first = dialog.SelectedProfile;
            var rule = new ColorRuleViewModel { RegexString = "[" };
            first.ColorRules.Add(rule);
            var mapping = new ValueMappingListViewModel(new ValueMappingList
            {
                Name = "Codes",
                Enabled = false,
                Items = [new ValueMappingItem { Value = "", Mapping = "1" }]
            });
            dialog.ValueMappings.Add(mapping);
            dialog.AddProfileCommand.Execute(null);
            dialog.SelectedValueMapping = null;

            Assert.IsFalse(dialog.Save());
            SettingsProblem? requested = null;
            dialog.NavigationRequested += problem => requested = problem;

            var colorProblem = dialog.Problems.Single(problem => problem.Field == SettingsField.ColorRegex);
            dialog.NavigateTo(colorProblem);
            Assert.AreSame(colorProblem, requested);
            Assert.AreSame(first, dialog.SelectedProfile);
            Assert.AreSame(rule, dialog.SelectedColorRule);

            dialog.NavigateTo(dialog.Problems.Single(problem => problem.Field == SettingsField.MappingValue));
            Assert.AreSame(mapping, dialog.SelectedValueMapping);
            Assert.AreSame(mapping.Items[0], mapping.SelectedItem);
        });
    }

    private static void WithSettings(Action<SettingsViewModel> test)
    {
        var path = Path.Combine(Path.GetTempPath(), $"loggrokx-autofix-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, "Settings:\n  ViewSettings:\n    TimelineAtTop: false\n");
        var previousMapper = ValueMapper.Current;
        try
        {
            var settings = ApplicationSettings.LoadFromFile(path);
            var dialog = new SettingsViewModel(settings, new TimelinePlacementService(settings),
                new ThreadGroupingService(settings), new MergedFilesViewService(), new TextZoomService(settings),
                new LogGrokX.Theming.UiThemeService(), new Platform.WpfColorValidator(), TestServices.Desktop);
            test(dialog);
        }
        finally
        {
            ValueMapper.Current = previousMapper;
            File.Delete(path);
        }
    }
}
