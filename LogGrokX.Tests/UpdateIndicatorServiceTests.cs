using System;
using System.IO;
using LogGrokX.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Tests;

[TestClass]
public sealed class UpdateIndicatorServiceTests
{
    [TestMethod]
    public void VisibilityDefaultsOnAndIsPersistedBySettingsEditor()
    {
        var path = Path.Combine(Path.GetTempPath(), $"loggrokx-update-indicator-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, "Settings:\n  ViewSettings:\n    UpdateMode: check\n");
        try
        {
            var settings = ApplicationSettings.LoadFromFile(path);
            var service = new UpdateIndicatorService(settings);
            Assert.IsTrue(service.IsVisible);
            var dialog = new SettingsViewModel(settings, new TimelinePlacementService(settings),
                new ThreadGroupingService(settings), new MergedFilesViewService(), new TextZoomService(settings),
                new LogGrokX.Theming.UiThemeService(), new Platform.WpfColorValidator(), TestServices.Desktop, service);
            Assert.IsTrue(dialog.View.ShowUpdateIndicator);

            dialog.View.ShowUpdateIndicator = false;
            dialog.SaveCommand.Execute(null);

            Assert.IsFalse(service.IsVisible);
            StringAssert.Contains(File.ReadAllText(path), "ShowUpdateIndicator: false");
            Assert.IsFalse(new UpdateIndicatorService(ApplicationSettings.LoadFromFile(path)).IsVisible);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
