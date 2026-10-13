#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AvalonDock.Themes;
using AvalonDock.Themes.VS2013.Themes;
using LogGrokX.Controls.GridView;
using LogGrokX.Theming;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Tests;

[TestClass]
[DoNotParallelize]
public sealed class DockThemeResourcesTests
{
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
