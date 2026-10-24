using System;
using System.IO;
using LogGrokX.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Tests;

[TestClass]
public class MergedFilesViewServiceTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void MergeIsEnabledOnlyForCurrentSession(bool previouslyEnabled)
    {
        var path = Path.Combine(Path.GetTempPath(), $"loggrokx-merge-session-{Guid.NewGuid():N}.yaml");
        var configuration = $"Settings:\n  ViewSettings:\n    MergedFilesView: {previouslyEnabled.ToString().ToLowerInvariant()}\n";
        File.WriteAllText(path, configuration);
        try
        {
            var settings = ApplicationSettings.LoadFromFile(path);
            var service = new MergedFilesViewService();
            Assert.IsFalse(service.IsEnabled);
            var changes = 0;
            service.Changed += () => changes++;

            service.SetEnabled(true);
            Assert.IsTrue(service.IsEnabled);
            Assert.AreEqual(1, changes);
            Assert.AreEqual(configuration, File.ReadAllText(path));

            var restarted = new MergedFilesViewService();
            Assert.IsFalse(restarted.IsEnabled);

            var dialog = new SettingsViewModel(settings, new TimelinePlacementService(settings),
                new ThreadGroupingService(settings), service, new TextZoomService(settings),
                new LogGrokX.Theming.UiThemeService(), new Platform.WpfColorValidator());
            Assert.IsTrue(dialog.View.MergedFilesView);
            dialog.SaveCommand.Execute(null);
            Assert.IsTrue(service.IsEnabled);
            Assert.IsFalse(new MergedFilesViewService().IsEnabled);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void SavingSettingsDoesNotPersistMergedView()
    {
        var path = Path.Combine(Path.GetTempPath(), $"loggrokx-merge-settings-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, "Settings:\n  ViewSettings:\n    TimelineAtTop: false\n");
        try
        {
            var settings = ApplicationSettings.LoadFromFile(path);
            var service = new MergedFilesViewService();
            var dialog = new SettingsViewModel(settings, new TimelinePlacementService(settings),
                new ThreadGroupingService(settings), service, new TextZoomService(settings),
                new LogGrokX.Theming.UiThemeService(), new Platform.WpfColorValidator());
            dialog.View.MergedFilesView = true;
            dialog.SaveCommand.Execute(null);
            Assert.IsTrue(service.IsEnabled);
            Assert.IsFalse(File.ReadAllText(path).Contains("MergedFilesView", StringComparison.Ordinal));
            Assert.IsFalse(new MergedFilesViewService().IsEnabled);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
