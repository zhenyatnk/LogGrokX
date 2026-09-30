#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Threading;
using LogGrokX.Colors.Configuration;
using LogGrokX.Data;
using LogGrokX.MarkedLines;
using LogGrokX.Search;
using LogGrokX.Theming;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Tests;

[TestClass]
public class ProfileRuntimeTests
{
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
                settings.ViewSettings.MergedFilesView = true;
                using var main = CreateMain(settings);
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
                Assert.AreNotSame(originalCurrent, second);
                Assert.AreEqual(otherPath, second.DocumentId);
                Assert.AreEqual(2, main.Documents.Count);
                CollectionAssert.AreEqual(new[] { "Text" }, second.MetaInformation.FieldNames);
                Assert.AreEqual(System.Windows.Media.Colors.Blue, second.ColorSettings.Rules.Single().ForegroundColor!.Value);
                Assert.AreEqual(System.Windows.Media.Colors.Blue, main.MergedViewModel.ColorSettings.Rules.Single().ForegroundColor!.Value);
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
        new ThreadGroupingService(settings), new MergedFilesViewService(settings),
        new UpdateCheckService(settings), documents => new MarkedLinesViewModel(documents));

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
