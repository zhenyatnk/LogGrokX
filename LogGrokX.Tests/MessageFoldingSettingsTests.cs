using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using LogGrokX.Controls.TextRender;
using LogGrokX.Data;
using LogGrokX.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Tests;

[TestClass]
[DoNotParallelize]
public sealed class MessageFoldingSettingsTests
{
    [TestMethod]
    public void MessageFoldingGutterDefaultsOnAndIsPersistedBySettingsEditor()
    {
        var path = Path.Combine(Path.GetTempPath(), $"loggrokx-message-folding-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, "Settings:\n  ViewSettings:\n    UpdateMode: check\n");
        var previous = FoldingLayoutOptions.Instance.UseMessageGutter;
        try
        {
            var settings = ApplicationSettings.LoadFromFile(path);
            var dialog = new SettingsViewModel(settings, new TimelinePlacementService(settings),
                new ThreadGroupingService(settings), new MergedFilesViewService(), new TextZoomService(settings),
                new LogGrokX.Theming.UiThemeService(), new Platform.WpfColorValidator(), TestServices.Desktop,
                new UpdateIndicatorService(settings));
            Assert.IsTrue(settings.ViewSettings.UseMessageFoldingGutter);
            Assert.IsTrue(dialog.View.UseMessageFoldingGutter);

            dialog.View.UseMessageFoldingGutter = false;
            dialog.SaveCommand.Execute(null);

            Assert.IsFalse(settings.ViewSettings.UseMessageFoldingGutter);
            Assert.IsFalse(FoldingLayoutOptions.Instance.UseMessageGutter);
            StringAssert.Contains(File.ReadAllText(path), "UseMessageFoldingGutter: false");
            Assert.IsFalse(ApplicationSettings.LoadFromFile(path).ViewSettings.UseMessageFoldingGutter);
        }
        finally
        {
            FoldingLayoutOptions.Instance.UseMessageGutter = previous;
            File.Delete(path);
        }
    }

    [TestMethod]
    public void HotReloadFromBackgroundThreadUpdatesLiveTextView()
    {
        RunOnSta(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), $"loggrokx-message-folding-reload-{Guid.NewGuid():N}.yaml");
            File.WriteAllText(path, "Settings:\n  ViewSettings:\n    UseMessageFoldingGutter: false\n");
            var previous = FoldingLayoutOptions.Instance.UseMessageGutter;
            try
            {
                var settings = ApplicationSettings.LoadFromFile(path, reloadOnChange: true);
                var view = new TextView { TextModel = new TextModel(10, "plain") };
                TextView.SetUseFixedFoldingGutter(view, true);
                var size = new Size(double.PositiveInfinity, double.PositiveInfinity);
                view.Measure(size);
                view.Arrange(new Rect(view.DesiredSize));
                var control = (TextControl)VisualTreeHelper.GetChild(view, 0);
                var regex = new Regex("plain");
                var initialBounds = control.GetHighlightGeometries(control.TextLines, regex)!.Bounds;

                Task.Run(() => File.WriteAllText(path,
                    "Settings:\n  ViewSettings:\n    UseMessageFoldingGutter: true\n"));

                Rect updatedBounds = default;
                var updated = PumpUntil(() =>
                {
                    updatedBounds = control.GetHighlightGeometries(control.TextLines, regex)!.Bounds;
                    return updatedBounds.X > initialBounds.X;
                }, TimeSpan.FromSeconds(5));

                Assert.IsTrue(updated, "Hot reload did not update the live Message gutter.");
                Assert.IsTrue(settings.ViewSettings.UseMessageFoldingGutter);
                Assert.IsTrue(FoldingLayoutOptions.Instance.UseMessageGutter);
                Assert.AreEqual(initialBounds.X + TextView.ExpanderMargin, updatedBounds.X, 0.001);
            }
            finally
            {
                FoldingLayoutOptions.Instance.UseMessageGutter = previous;
                File.Delete(path);
            }
        });
    }

    private static bool PumpUntil(Func<bool> condition, TimeSpan timeout)
    {
        var frame = new DispatcherFrame();
        var stopwatch = Stopwatch.StartNew();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += (_, _) =>
        {
            if (condition() || stopwatch.Elapsed >= timeout)
                frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
        return condition();
    }

    private static void RunOnSta(Action action)
    {
        ApplicationSettings.Instance();
        Exception error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                error = e;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null)
            throw error;
    }
}
