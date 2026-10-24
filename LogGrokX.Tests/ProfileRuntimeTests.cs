#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Threading;
using System.Windows;
using LogGrokX.Controls.GridView;
using LogGrokX.Colors.Configuration;
using LogGrokX.Data;
using LogGrokX.MarkedLines;
using LogGrokX.MergedView;
using LogGrokX.Search;
using LogGrokX.Settings;
using LogGrokX.Theming;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Tests;

[TestClass]
public class ProfileRuntimeTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void MergedVennFiltersHideRowsAcrossThreeDocuments(bool useColumnFilter)
    {
        RunOnSta(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), $"loggrokx-merged-filters-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            var settingsPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", "LogGrokX", "appsettings.yaml"));
            var paths = Enumerable.Range(0, 3).Select(i => Path.Combine(directory, $"sample-{i}.log")).ToArray();
            try
            {
                var settings = ApplicationSettings.LoadFromFile(settingsPath);
                settings.SelectedProfile = "Venn";
                using var main = CreateMain(settings);
                main.IsMergedView = true;
                foreach (var path in paths)
                {
                    File.WriteAllText(path,
                        "2026-10-22 13:24:55.848 - [TRAC] - [1:20404:20396:M:U] - [msrdc.exe] - [IPC] - first\n" +
                        "2026-10-22 13:24:55.849 - [WARN] - [2:20405:20397:M:U] - [other.exe] - [FS] - second\n");
                    main.AddDocument(path);
                }

                WaitFor(() => main.Documents.All(document => !document.LogViewModel.IsLoading));
                var merged = main.MergedViewModel;
                WaitFor(() => !merged.IsLoading && merged.Lines.Count == 6);
                foreach (var field in new[] { "Time", "Context", "Message" })
                    Assert.IsFalse(merged.MetaInformation.IsFieldIndexed(field));
                var list = new System.Windows.Controls.ListView { ItemsSource = merged.Lines };
                Assert.AreEqual(6, list.Items.Count);
                _ = list.Items[0];

                foreach (var field in new[] { "Level", "Sid", "Pid", "Thread", "Process", "Source" })
                {
                    var fieldIndex = Array.IndexOf(merged.MetaInformation.FieldNames, field);
                    var selected = (MergedLineViewModel)merged.Lines[0];
                    var excludedValue = selected.GetFieldText(fieldIndex);
                    var revision = merged.Revision;
                    if (useColumnFilter)
                    {
                        var view = (System.Windows.Controls.GridView)new LogGridViewConverter().Convert(merged.CustomView, typeof(System.Windows.Controls.GridView), null!, System.Globalization.CultureInfo.InvariantCulture);
                        foreach (var column in view.Columns)
                            column.HeaderTemplate.Seal();
                        var header = view.Columns.Select(column => ((FrameworkElement)column.HeaderTemplate.LoadContent())
                                .DataContext).OfType<HeaderViewModel>()
                            .Single(item => item.FieldHeader == field);
                        Assert.IsNotNull(header.FilterViewModel);
                        header.FilterViewModel.Elements.Single(element => element.Name == excludedValue).IsActive = false;
                    }
                    else
                    {
                        merged.SelectedItems = new[] { selected };
                        merged.ExcludeCommand.Execute(fieldIndex);
                    }

                    WaitFor(() => merged.Revision > revision && !merged.IsLoading);
                    Assert.AreEqual(3, merged.Lines.Count, $"Filter on {field} did not hide rows.");
                    Assert.AreEqual(3, list.Items.Count, $"Table count did not change for {field}.");
                    Assert.IsTrue(list.Items.Cast<MergedLineViewModel>()
                        .All(line => line.GetFieldText(fieldIndex) != excludedValue), $"Table still shows excluded {field}.");
                    Assert.IsTrue(merged.Lines.Cast<MergedLineViewModel>()
                        .All(line => line.GetFieldText(fieldIndex) != excludedValue));
                    Assert.AreEqual(6, merged.TotalLineCount);
                    revision = merged.Revision;
                    merged.ClearFiltersCommand.Execute(null);
                    WaitFor(() => merged.Revision > revision && !merged.IsLoading);
                    Assert.AreEqual(6, merged.Lines.Count);
                }
            }
            finally
            {
                foreach (var path in paths)
                    File.Delete(path);
                Directory.Delete(directory);
            }
        });
    }

    [TestMethod]
    public void SwitchingProfileWithSameColumnsRefreshesMergedFilters()
    {
        RunOnSta(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), $"loggrokx-merged-profile-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "sample.log");
            var settingsPath = Path.Combine(directory, "appsettings.yaml");
            File.WriteAllText(path, "INFO IPC first\nWARN FIL second\n");
            try
            {
                var levelProfile = Profile("Level", "^(?<Level>\\w+) (?<Source>\\w+) (?<Message>.*)", "Gray");
                levelProfile.LogFormats![0].IndexedFields = ["Level"];
                var sourceProfile = Profile("Source", levelProfile.LogFormats[0].Regex, "Gray");
                sourceProfile.LogFormats![0].IndexedFields = ["Source"];
                var settings = new ApplicationSettings(settingsPath)
                {
                    Profiles = [levelProfile, sourceProfile],
                    SelectedProfile = "Level"
                };
                using var main = CreateMain(settings);
                main.IsMergedView = true;
                main.AddDocument(path);
                var merged = main.MergedViewModel;
                WaitFor(() => !main.CurrentDocument!.LogViewModel.IsLoading && !merged.IsLoading && merged.Lines.Count == 2);
                var originalFields = merged.MetaInformation.FieldNames;
                var viewChanges = 0;
                merged.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(merged.CustomView))
                        viewChanges++;
                };

                main.SelectedProfile = main.Profiles[1];
                WaitFor(() => !main.CurrentDocument!.LogViewModel.IsLoading && !merged.IsLoading && merged.Lines.Count == 2);

                CollectionAssert.AreEqual(originalFields, merged.MetaInformation.FieldNames);
                Assert.IsGreaterThan(0, viewChanges);
                Assert.IsFalse(merged.MetaInformation.IsFieldIndexed("Level"));
                Assert.IsTrue(merged.MetaInformation.IsFieldIndexed("Source"));
                var view = (System.Windows.Controls.GridView)new LogGridViewConverter().Convert(merged.CustomView, typeof(System.Windows.Controls.GridView), null!, System.Globalization.CultureInfo.InvariantCulture);
                var template = view.Columns[3 + Array.IndexOf(merged.MetaInformation.FieldNames, "Source")].HeaderTemplate;
                template.Seal();
                var header = (HeaderViewModel)((FrameworkElement)template.LoadContent()).DataContext;
                Assert.IsNotNull(header.FilterViewModel);
                var revision = merged.Revision;
                header.FilterViewModel.Elements.Single(element => element.Name == "IPC").IsActive = false;
                WaitFor(() => merged.Revision > revision && !merged.IsLoading);
                Assert.AreEqual(1, merged.Lines.Count);
                Assert.AreEqual("Source", merged.ActiveFilters.Single().FieldName);
                Assert.AreEqual("FIL", ((MergedLineViewModel)merged.Lines[0]).GetFieldText(1));
            }
            finally
            {
                File.Delete(path);
                File.Delete(settingsPath);
                Directory.Delete(directory);
            }
        });
    }

    [TestMethod]
    public void InvalidIndexedFieldFromYamlFallsBackToPlainText()
    {
        RunOnSta(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), $"loggrokx-invalid-format-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "sample.log");
            var settingsPath = Path.Combine(directory, "appsettings.yaml");
            using var diagnostics = new StringWriter();
            using var listener = new TextWriterTraceListener(diagnostics);
            Trace.Listeners.Add(listener);
            try
            {
                File.WriteAllText(path, "INFO hello\n");
                File.WriteAllText(settingsPath,
                    "Settings:\n  Profiles:\n    - Name: Broken\n      LogFormats:\n" +
                    "        - Regex: '^(?<Level>[A-Z]+) (?<Message>.*)$'\n          IndexedFields:\n            - Levl\n");
                var settings = ApplicationSettings.LoadFromFile(settingsPath);
                using var main = CreateMain(settings);

                main.AddDocument(path);

                Assert.AreEqual(1, main.Documents.Count);
                Assert.IsNotNull(main.CurrentDocument);
                WaitFor(() => !main.CurrentDocument.LogViewModel.IsLoading);
                CollectionAssert.AreEqual(new[] { "Text" }, main.CurrentDocument.MetaInformation.FieldNames);
                StringAssert.Contains(diagnostics.ToString(), "Broken");
                StringAssert.Contains(diagnostics.ToString(), "Levl");
                StringAssert.Contains(diagnostics.ToString(), "Skipping invalid log format");
                main.Documents.Clear();
                using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            finally
            {
                Trace.Listeners.Remove(listener);
                File.Delete(path);
                File.Delete(settingsPath);
                Directory.Delete(directory);
            }
        });
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void OldClassicConfigWithUnknownProcessFieldLoadsOtherFormats(bool legacy)
    {
        RunOnSta(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), $"loggrokx-old-classic-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            var logPath = Path.Combine(directory, "sample.log");
            var settingsPath = Path.Combine(directory, "appsettings.yaml");
            var defaultSettingsPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", "LogGrokX", "appsettings.yaml"));
            using var diagnostics = new StringWriter();
            using var listener = new TextWriterTraceListener(diagnostics);
            Trace.Listeners.Add(listener);
            try
            {
                var classic = ApplicationSettings.LoadFromFile(defaultSettingsPath).GetProfiles()
                    .Single(profile => profile.Name == "Classic");
                Assert.AreEqual(5, classic.LogFormats!.Length);
                var oldFormat = classic.LogFormats[3];
                oldFormat.IndexedFields = ["Process", .. oldFormat.IndexedFields];
                var formats = classic.LogFormats.Select(format => new LogFormatViewModel(format).ToData());
                var yaml = legacy
                    ? "Settings:\n  LogFormats:\n" + string.Join("\n", SettingsYamlRenderer.RenderLogFormats(formats, 4)) + "\n"
                    : "Settings:\n  SelectedProfile: Classic\n  Profiles:\n" +
                      string.Join("\n", SettingsYamlRenderer.RenderProfiles([new ProfileViewModel(classic)], 4)) + "\n";
                File.WriteAllText(settingsPath, yaml);
                File.WriteAllText(logPath, "2026-01-02 03:04:05.678 INFO main App hello\n");
                var settings = ApplicationSettings.LoadFromFile(settingsPath);
                using var main = CreateMain(settings);

                main.AddDocument(logPath);

                var document = main.CurrentDocument;
                Assert.IsNotNull(document);
                WaitFor(() => !document.LogViewModel.IsLoading);
                CollectionAssert.Contains(document.MetaInformation.FieldNames, "Level");
                Assert.AreEqual(1, document.LogViewModel.Lines.Count);
                StringAssert.Contains(diagnostics.ToString(), legacy ? "Default" : "Classic");
                StringAssert.Contains(diagnostics.ToString(), "Process");
                Assert.AreEqual(yaml, File.ReadAllText(settingsPath));
                CollectionAssert.Contains(settings.GetSelectedProfile().LogFormats![3].IndexedFields, "Process");

                if (!legacy)
                {
                    var plain = Profile("Text", "^(?'Text'.*)", "Gray");
                    settings.UpdateProfiles([plain, classic], "Text");
                    WaitFor(() => !main.CurrentDocument!.LogViewModel.IsLoading);
                    main.SelectedProfile = main.Profiles.Single(profile => profile.Name == "Classic");
                    WaitFor(() => !main.CurrentDocument!.LogViewModel.IsLoading);
                    Assert.AreEqual("Classic", main.SelectedProfile.Name);
                    CollectionAssert.Contains(main.CurrentDocument!.MetaInformation.FieldNames, "Level");
                }
            }
            finally
            {
                Trace.Listeners.Remove(listener);
                File.Delete(logPath);
                File.Delete(settingsPath);
                Directory.Delete(directory);
            }
        });
    }

    [TestMethod]
    public void VennProfileGroupsConsecutiveLinesByThreadInBothViews()
    {
        RunOnSta(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), $"loggrokx-profile-thread-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "venn.log");
            var settingsPath = Path.Combine(directory, "appsettings.yaml");
            var defaultSettingsPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", "LogGrokX", "appsettings.yaml"));
            try
            {
                File.Copy(defaultSettingsPath, settingsPath);
                File.WriteAllLines(path,
                [
                    "2026-07-22 9:01:02.123 - [INFO] - [123:456:789:M:U] - first",
                    "2026-07-22 9:01:02.124 - [INFO] - [123:456:789:M:U] - second",
                    "2026-07-22 9:01:02.125 - [INFO] - [123:456:790:M:U] - third"
                ]);
                var settings = ApplicationSettings.LoadFromFile(settingsPath);
                using var main = CreateMain(settings);
                main.SelectedProfile = main.Profiles.Single(profile => profile.Name == "Venn");
                main.AddDocument(path);
                var document = main.CurrentDocument!;
                WaitFor(() => !document.LogViewModel.IsLoading);
                var log = document.LogViewModel;
                Assert.IsTrue(log.ThreadFieldIndex >= 0);
                Assert.AreEqual(Array.IndexOf(document.MetaInformation.FieldNames, "Thread"), log.ThreadFieldIndex);
                var lines = log.Lines.Cast<ItemViewModel>().OfType<LineViewModel>().ToArray();
                Assert.AreEqual(3, lines.Length);
                Assert.IsTrue(lines[0].HasSameThread(lines[1], log.ThreadFieldIndex));
                Assert.IsFalse(lines[1].HasSameThread(lines[2], log.ThreadFieldIndex));

                main.IsGroupByThread = true;
                Assert.IsTrue(log.GroupByThread);
                Assert.IsTrue(main.MergedViewModel.GroupByThread);
                main.IsMergedView = true;
                WaitFor(() => main.MergedViewModel.TotalLineCount == 3);
                var merged = main.MergedViewModel;
                Assert.AreEqual(Array.IndexOf(merged.MetaInformation.FieldNames, "Thread"), merged.ThreadFieldIndex);
                var mergedLines = merged.Lines.Cast<ItemViewModel>().OfType<MergedLineViewModel>().ToArray();
                Assert.IsTrue(mergedLines[0].HasSameThread(mergedLines[1], merged.ThreadFieldIndex));
                Assert.IsFalse(mergedLines[1].HasSameThread(mergedLines[2], merged.ThreadFieldIndex));

                Assert.IsTrue(ApplicationSettings.LoadFromFile(settingsPath).ViewSettings.GroupByThread);
                main.IsGroupByThread = false;
                Assert.IsFalse(log.GroupByThread);
                Assert.IsFalse(merged.GroupByThread);
                Assert.IsFalse(ApplicationSettings.LoadFromFile(settingsPath).ViewSettings.GroupByThread);
            }
            finally
            {
                File.Delete(path);
                File.Delete(settingsPath);
                Directory.Delete(directory);
            }
        });
    }

    [TestMethod]
    public void SwitchingProfileReparsesDocumentsAndPreservesMarksAndMergedSelection()
    {
        RunOnSta(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), $"loggrokx-profile-runtime-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "sample.log");
            var otherPath = Path.Combine(directory, "other.log");
            File.WriteAllText(path, "INFO sample\n");
            File.WriteAllText(otherPath, "WARN other\n");
            try
            {
                var settings = new ApplicationSettings(Path.Combine(directory, "appsettings.yaml"))
                {
                    Profiles = [Profile("Structured", "^(?'Level'\\w+) (?'Message'.*)", "Red"),
                        Profile("Text", "^(?'Text'.*)", "Blue")],
                    SelectedProfile = "Structured"
                };
                using var main = CreateMain(settings);
                main.IsMergedView = true;
                main.AddDocument(path);
                var first = main.CurrentDocument!;
                WaitFor(() => !first.LogViewModel.IsLoading);
                first.MarkedLines.Add(0);
                main.AddDocument(otherPath);
                var originalCurrent = main.CurrentDocument;
                main.MergedViewModel.AvailableDocuments.Single(item => item.Document.DocumentId == otherPath).IsSelected = false;

                main.SelectedProfile = main.Profiles[1];
                var second = main.CurrentDocument!;
                WaitFor(() => !second.LogViewModel.IsLoading);
                WaitFor(() => main.Documents.All(document => !document.LogViewModel.IsLoading));
                WaitFor(() => !main.MergedViewModel.IsLoading && main.MergedViewModel.TotalLineCount == 1);
                Assert.AreNotSame(originalCurrent, second);
                Assert.AreEqual(otherPath, second.DocumentId);
                Assert.AreEqual(2, main.Documents.Count);
                CollectionAssert.AreEqual(new[] { "Text" }, second.MetaInformation.FieldNames);
                Assert.AreEqual("Blue", second.ColorSettings.Rules.Single().ForegroundColor);
                Assert.AreEqual("Blue", main.MergedViewModel.ColorSettings.Rules.Single().ForegroundColor);
                Assert.IsTrue(main.Documents[0].MarkedLines.Contains(0));
                Assert.AreEqual(1, main.MarkedLinesViewModel.MarkedLines.Count);
                Assert.IsFalse(main.MergedViewModel.AvailableDocuments.Single(item => item.Document.DocumentId == otherPath).IsSelected);
                Assert.IsTrue(main.MergedViewModel.AvailableDocuments.Single(item => item.Document.DocumentId == path).IsSelected);
                CollectionAssert.AreEqual(new[] { "Text" }, main.MergedViewModel.MetaInformation.FieldNames);
                Assert.AreEqual("Text", ApplicationSettings.LoadFromFile(settings.FileName).SelectedProfile);
            }
            finally
            {
                File.Delete(path);
                File.Delete(otherPath);
                File.Delete(Path.Combine(directory, "appsettings.yaml"));
                Directory.Delete(directory);
            }
        });
    }

    [TestMethod]
    public void UpdatedActiveProfileRebuildsDocumentButUnchangedSettingsDoNot()
    {
        RunOnSta(() =>
        {
            var path = Path.GetTempFileName();
            File.WriteAllText(path, "INFO sample\n");
            try
            {
                var settings = new ApplicationSettings
                {
                    Profiles = [Profile("Active", "^(?'Message'.*)", "Red")],
                    SelectedProfile = "Active"
                };
                using var main = CreateMain(settings);
                main.AddDocument(path);
                var original = main.CurrentDocument!;
                WaitFor(() => !original.LogViewModel.IsLoading);
                settings.UpdateProfiles([Profile("Active", "^(?'Message'.*)", "Red")], "Active");
                Assert.AreSame(original, main.CurrentDocument);
                settings.UpdateProfiles([Profile("Active", "^(?'Text'.*)", "Blue")], "Active");
                Assert.AreNotSame(original, main.CurrentDocument);
                CollectionAssert.AreEqual(new[] { "Text" }, main.CurrentDocument!.MetaInformation.FieldNames);
                WaitFor(() => !main.CurrentDocument!.LogViewModel.IsLoading);
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [TestMethod]
    public void FailedProfileChangeKeepsOpenDocumentAndSelection()
    {
        RunOnSta(() =>
        {
            var path = Path.GetTempFileName();
            File.WriteAllText(path, "sample\n");
            try
            {
                var settings = new ApplicationSettings
                {
                    Profiles = [Profile("Valid", "^(?'Message'.*)", "Red"),
                        Profile("Invalid", "^(?'Text'.*)", "invalid-color")],
                    SelectedProfile = "Valid"
                };
                using var main = CreateMain(settings);
                main.AddDocument(path);
                var document = main.CurrentDocument!;
                WaitFor(() => !document.LogViewModel.IsLoading);
                main.SelectedProfile = main.Profiles[1];
                Assert.AreEqual("Valid", main.SelectedProfile.Name);
                Assert.AreSame(document, main.CurrentDocument);
                Assert.AreEqual(1, main.Documents.Count);
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [TestMethod]
    public void FailedReloadRefreshesProfilesKeepsAppliedSettingsAndSuppressesRepeatedErrors()
    {
        RunOnSta(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), $"loggrokx-profile-reload-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "first.log");
            var otherPath = Path.Combine(directory, "second.log");
            var settingsPath = Path.Combine(directory, "appsettings.yaml");
            using var errors = new StringWriter();
            using var listener = new TextWriterTraceListener(errors);
            Trace.Listeners.Add(listener);
            try
            {
                File.WriteAllText(path, "sample\n");
                File.WriteAllText(otherPath, "other\n");
                var settings = new ApplicationSettings(settingsPath)
                {
                    Profiles = [Profile("Active", "^(?'Message'.*)", "Red"),
                        Profile("Removed", "^(?'Message'.*)", "Red")],
                    SelectedProfile = "Active"
                };
                using var main = CreateMain(settings);
                main.AddDocument(path);
                var original = main.CurrentDocument!;
                WaitFor(() => !original.LogViewModel.IsLoading);

                var updatedActive = Profile("Active", "^(?'Text'.*)", "Blue");
                var invalid = Profile("Invalid", "^(?'Text'.*)", "invalid-color");
                settings.UpdateProfiles([updatedActive, Profile("Added", "^(?'Text'.*)", "Gray"), invalid], "Invalid");
                CollectionAssert.AreEqual(new[] { "Active", "Added", "Invalid" }, main.Profiles.Select(profile => profile.Name).ToArray());
                Assert.AreSame(main.Profiles[0], main.SelectedProfile);
                Assert.AreSame(original, main.CurrentDocument);
                Assert.AreEqual(1, Regex.Matches(errors.ToString(), "Cannot apply profile").Count);

                settings.UpdateProfiles([updatedActive, Profile("Renamed", "^(?'Text'.*)", "Gray"), invalid], "Invalid");
                CollectionAssert.AreEqual(new[] { "Active", "Renamed", "Invalid" }, main.Profiles.Select(profile => profile.Name).ToArray());
                Assert.AreEqual(1, Regex.Matches(errors.ToString(), "Cannot apply profile").Count);
                main.AddDocument(otherPath);
                WaitFor(() => !main.CurrentDocument!.LogViewModel.IsLoading);
                CollectionAssert.AreEqual(new[] { "Message" }, main.CurrentDocument!.MetaInformation.FieldNames);
                Assert.AreEqual("Red", main.CurrentDocument.ColorSettings.Rules.Single().ForegroundColor);

                settings.UpdateProfiles([invalid], "Invalid");
                Assert.AreEqual("Invalid", main.Profiles.Single().Name);
                Assert.AreEqual("Active", main.SelectedProfile.Name);
                Assert.AreEqual(1, Regex.Matches(errors.ToString(), "Cannot apply profile").Count);

                var anotherInvalid = Profile("Invalid", "^(?'Text'.*)", "another-invalid-color");
                settings.UpdateProfiles([anotherInvalid], "Invalid");
                Assert.AreEqual(2, Regex.Matches(errors.ToString(), "Cannot apply profile").Count);
                settings.UpdateProfiles([Profile("Invalid", "^(?'Text'.*)", "Blue")], "Invalid");
                WaitFor(() => main.Documents.All(document => !document.LogViewModel.IsLoading));
                Assert.AreEqual("Invalid", main.SelectedProfile.Name);
                CollectionAssert.AreEqual(new[] { "Text" }, main.CurrentDocument!.MetaInformation.FieldNames);
                Assert.AreEqual("Blue", main.CurrentDocument.ColorSettings.Rules.Single().ForegroundColor);

                settings.UpdateProfiles([anotherInvalid], "Invalid");
                Assert.AreEqual(3, Regex.Matches(errors.ToString(), "Cannot apply profile").Count);
                Assert.AreEqual("Blue", main.CurrentDocument.ColorSettings.Rules.Single().ForegroundColor);
            }
            finally
            {
                Trace.Listeners.Remove(listener);
                File.Delete(path);
                File.Delete(otherPath);
                File.Delete(settingsPath);
                Directory.Delete(directory);
            }
        });
    }

    private static ProfileSettings Profile(string name, string regex, string color) => new()
    {
        Name = name,
        InheritLegacySettings = false,
        LogFormats = [new LogFormat { Regex = regex }],
        ColorSettings = new ColorSettings { Rules = [new ColorRule { RegexString = ".*", ForegroundColor = color }] }
    };

    private static MainWindowViewModel CreateMain(ApplicationSettings settings) => new(settings,
        new SearchAutocompleteCache(), new SavedSearchPatternStore(), new UiThemeService(),
        new TimelinePlacementService(settings), new TextZoomService(settings),
        new ThreadGroupingService(settings), new MergedFilesViewService(),
        null, documents => new MarkedLinesViewModel(documents), new Platform.WpfColorValidator());

    private static void WaitFor(Func<bool> ready)
    {
        var frame = new DispatcherFrame();
        var started = DateTime.UtcNow;
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(10), DispatcherPriority.Background,
            (_, _) =>
            {
                if (ready() || DateTime.UtcNow - started > TimeSpan.FromSeconds(5))
                    frame.Continue = false;
            }, Dispatcher.CurrentDispatcher);
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
        Assert.IsTrue(ready(), "Document loading did not finish");
    }

    private static void RunOnSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            try { action(); }
            catch (Exception exception) { error = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null)
            throw error;
    }
}
