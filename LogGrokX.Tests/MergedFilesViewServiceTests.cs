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
            var service = new MergedFilesViewService(settings);
            Assert.IsFalse(service.IsEnabled);
            Assert.IsFalse(settings.ViewSettings.MergedFilesView);
            var changes = 0;
            service.Changed += () => changes++;

            service.SetEnabled(true);
            Assert.IsTrue(service.IsEnabled);
            Assert.IsTrue(settings.ViewSettings.MergedFilesView);
            Assert.AreEqual(1, changes);
            Assert.AreEqual(configuration, File.ReadAllText(path));

            var restarted = new MergedFilesViewService(ApplicationSettings.LoadFromFile(path));
            Assert.IsFalse(restarted.IsEnabled);

            settings.ViewSettings.MergedFilesView = previouslyEnabled;
            var dialog = new SettingsViewModel(settings, new TimelinePlacementService(settings),
                new ThreadGroupingService(settings), service, new TextZoomService(settings));
            Assert.IsTrue(dialog.View.MergedFilesView);
            dialog.SaveCommand.Execute(null);
            Assert.IsTrue(service.IsEnabled);
            Assert.AreEqual(previouslyEnabled, ApplicationSettings.LoadFromFile(path).ViewSettings.MergedFilesView);
            Assert.IsFalse(new MergedFilesViewService(ApplicationSettings.LoadFromFile(path)).IsEnabled);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
