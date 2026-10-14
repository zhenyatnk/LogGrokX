#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AvalonDock.Themes;
using AvalonDock.Themes.VS2013.Themes;
using LogGrokX.Controls.GridView;
using LogGrokX.Controls;
using LogGrokX.Theming;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Wpf.Ui.Appearance;
using Wpf.Ui.Markup;

namespace LogGrokX.Tests;

[TestClass]
[DoNotParallelize]
public sealed class DockThemeResourcesTests
{
    [TestMethod]
    public void TimelineBrushesUpdateAcrossThemeChangesWithoutRecreatingTheControl()
    {
        RunOnSta(() =>
        {
            var timeline = new LogMinimapControl();
            var themes = new ThemesDictionary { Theme = ApplicationTheme.Light };
            var resources = new ResourceDictionary();
            resources.MergedDictionaries.Add(themes);
            resources.MergedDictionaries.Add(new ControlsDictionary());
            resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/LogGrokX;component/Styles/FluentStyles.xaml")
            });
            timeline.SetResourceReference(LogMinimapControl.SelectionBrushProperty, "TimelineSelectionBrush");
            timeline.SetResourceReference(LogMinimapControl.DimBrushProperty, "TimelineDimBrush");
            var window = new Window
            {
                Content = timeline, Resources = resources, Width = 320, Height = 80,
                Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual
            };
            try
            {
                window.Show();
                var dimColors = new System.Collections.Generic.List<Color>();
                foreach (var theme in new[] { ApplicationTheme.Light, ApplicationTheme.Dark, ApplicationTheme.Light })
                {
                    themes.Theme = theme;
                    window.UpdateLayout();
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                    var selection = (SolidColorBrush)timeline.SelectionBrush;
                    var dim = (SolidColorBrush)timeline.DimBrush;
                    Assert.AreEqual((Color)window.FindResource("SystemAccentColorPrimary"), selection.Color);
                    Assert.AreEqual(((SolidColorBrush)window.FindResource("TextFillColorPrimaryBrush")).Color, dim.Color);
                    Assert.AreEqual(0.16, selection.Opacity);
                    Assert.AreEqual(0.10, dim.Opacity);
                    Assert.IsFalse(selection.IsFrozen);
                    Assert.IsFalse(dim.IsFrozen);
                    dimColors.Add(dim.Color);
                }

                Assert.AreNotEqual(dimColors[0], dimColors[1]);
                Assert.AreEqual(dimColors[0], dimColors[2]);

                resources["SystemAccentColorPrimary"] = System.Windows.Media.Colors.Orange;
                window.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                Assert.AreEqual(System.Windows.Media.Colors.Orange, ((SolidColorBrush)timeline.SelectionBrush).Color);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [TestMethod]
    public void DetachedDocumentMenuResolvesOverflowIconAcrossThemeChanges()
    {
        RunOnSta(() =>
        {
            var icon = new System.Windows.Shapes.Path { Resources = new DockThemeResources() };
            var resources = (DockThemeResources)icon.Resources;
            PresentationTraceSources.Refresh();
            using var output = new StringWriter();
            using var listener = new TextWriterTraceListener(output);
            var trace = PresentationTraceSources.ResourceDictionarySource;
            var previousLevel = trace.Switch.Level;
            trace.Switch.Level = SourceLevels.Warning;
            trace.Listeners.Add(listener);
            try
            {
                icon.SetResourceReference(System.Windows.Shapes.Path.DataProperty, "PinDocMenu");
                foreach (var isDark in new[] { false, true, false })
                {
                    resources.Apply(isDark);
                    Assert.IsNotNull(icon.Data);
                    Assert.IsGreaterThan(0.0, icon.Data.GetArea());
                }

                Assert.AreEqual(string.Empty, output.ToString());
            }
            finally
            {
                trace.Listeners.Remove(listener);
                trace.Switch.Level = previousLevel;
            }
        });
    }

    [TestMethod]
    public void DetachedControlsResolveDockBrushesAcrossThemeChanges()
    {
        RunOnSta(() =>
        {
            var resources = new DockThemeResources();
            var control = new Border { Resources = resources };
            var key = new ComponentResourceKey(typeof(ResourceKeys), "TabBackground");
            control.SetResourceReference(Border.BackgroundProperty, key);
            foreach (var isDark in new[] { false, true, false })
            {
                resources.Apply(isDark);
                DictionaryTheme theme = isDark ? new Vs2013DarkTheme() : new Vs2013LightTheme();
                foreach (var id in new[]
                         {
                             "TabBackground", "PanelBorderBrush", "DocumentWellTabSelectedActiveBackground",
                             "DocumentWellTabUnselectedText", "DocumentWellTabUnselectedHoveredText",
                             "DocumentWellTabSelectedActiveText", "DocumentWellTabUnselectedBackground",
                             "DocumentWellTabUnselectedHoveredBackground"
                         })
                {
                    var resourceKey = new ComponentResourceKey(typeof(ResourceKeys), id);
                    Assert.AreEqual(((SolidColorBrush)theme.ThemeResourceDictionary[resourceKey]).Color,
                        ((SolidColorBrush)resources[resourceKey]).Color, id);
                }

                Assert.AreEqual(((SolidColorBrush)theme.ThemeResourceDictionary[key]).Color,
                    ((SolidColorBrush)control.Background).Color);
            }
        });
    }

    [TestMethod]
    public void PinFocusVisualResolvesWithoutThemeSpecificSystemKey()
    {
        RunOnSta(() =>
        {
            var resources = new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/LogGrokX;component/Styles/PinCheckboxStyle.xaml")
            };
            var pin = new PinControl { Style = (Style)resources[typeof(PinControl)] };

            Assert.AreSame(resources["PinFocusVisualStyle"], pin.FocusVisualStyle);
            var focusVisual = new Control { Style = pin.FocusVisualStyle };
            Assert.IsTrue(focusVisual.ApplyTemplate());
        });
    }

    private static void RunOnSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
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
