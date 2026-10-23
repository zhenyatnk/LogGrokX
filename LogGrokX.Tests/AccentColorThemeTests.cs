#nullable enable

using System;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using AvalonDock.Themes.VS2013.Themes;
using LogGrokX.Theming;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Tests;

[TestClass]
[DoNotParallelize]
public sealed class AccentColorThemeTests
{
    [TestMethod]
    public void SelectedAccentUpdatesButtonAndDockBrushes()
    {
        RunOnSta(() =>
        {
            var resources = new ResourceDictionary
            {
                ["SystemAccentColorPrimary"] = Color.FromRgb(1, 2, 3),
                ["SystemAccentColorSecondary"] = Color.FromRgb(4, 5, 6),
                ["SystemAccentColorTertiary"] = Color.FromRgb(7, 8, 9),
                ["AccentFillColorDefault"] = Color.FromRgb(10, 11, 12),
                ["AccentFillColorSecondary"] = Color.FromArgb(229, 13, 14, 15),
                ["AccentFillColorTertiary"] = Color.FromArgb(204, 16, 17, 18)
            };
            UiThemeService.ApplyDerivedAccentBrushes(resources);

            AssertBrushMatchesColor(resources, "AccentFillColorDefaultBrush", "AccentFillColorDefault");
            AssertBrushMatchesColor(resources, "AccentFillColorSecondaryBrush", "AccentFillColorSecondary");
            AssertBrushMatchesColor(resources, "AccentFillColorTertiaryBrush", "AccentFillColorTertiary");
            AssertBrushMatchesColor(resources, "AccentButtonBackground", "AccentFillColorDefault");
            AssertBrushMatchesColor(resources, "AccentButtonBackgroundPointerOver", "AccentFillColorSecondary");
            AssertBrushMatchesColor(resources, "AccentButtonBackgroundPressed", "AccentFillColorTertiary");
            AssertBrushMatchesColor(resources, "SystemAccentColorPrimaryBrush", "SystemAccentColorPrimary");

            var dockResources = new ResourceDictionary();
            var accent = (Color)resources["SystemAccentColorPrimary"];
            DockThemeResources.ApplyAccentColor(dockResources, accent);
            foreach (var key in new[]
                     {
                         ResourceKeys.DocumentWellTabSelectedActiveBackground,
                         ResourceKeys.DocumentWellTabSelectedInactiveBackground,
                         ResourceKeys.DocumentWellTabUnselectedHoveredBackground,
                         ResourceKeys.ToolWindowCaptionActiveBackground,
                         ResourceKeys.ToolWindowTabSelectedActiveBackground,
                         ResourceKeys.ToolWindowTabSelectedInactiveBackground,
                         ResourceKeys.ToolWindowTabUnselectedHoveredBackground,
                         ResourceKeys.AutoHideTabHoveredBackground,
                         ResourceKeys.AutoHideTabHoveredBorder
                     })
            {
                Assert.AreEqual(accent, ((SolidColorBrush)dockResources[key]).Color, key.ToString());
            }
        });
    }

    [TestMethod]
    public void SelectedAccentUpdatesCheckBoxFillBrushes()
    {
        RunOnSta(() =>
        {
            var resources = new ResourceDictionary
            {
                ["SystemAccentColorPrimary"] = Color.FromRgb(1, 2, 3),
                ["SystemAccentColorSecondary"] = Color.FromRgb(4, 5, 6),
                ["SystemAccentColorTertiary"] = Color.FromRgb(7, 8, 9),
                ["AccentFillColorDefault"] = Color.FromRgb(10, 11, 12),
                ["AccentFillColorSecondary"] = Color.FromArgb(229, 13, 14, 15),
                ["AccentFillColorTertiary"] = Color.FromArgb(204, 16, 17, 18)
            };
            UiThemeService.ApplyDerivedAccentBrushes(resources);

            AssertBrushMatchesColor(resources, "CheckBoxCheckBackgroundFillChecked", "SystemAccentColorPrimary");
            AssertBrushMatchesColor(resources, "CheckBoxCheckBackgroundFillCheckedPointerOver", "AccentFillColorSecondary");
            AssertBrushMatchesColor(resources, "CheckBoxCheckBackgroundFillCheckedPressed", "AccentFillColorTertiary");
        });
    }

    [TestMethod]
    public void SelectedAccentKeepsExistingButtonBrushInstances()
    {
        RunOnSta(() =>
        {
            var previousBrush = new SolidColorBrush(System.Windows.Media.Colors.Blue);
            var resources = new ResourceDictionary
            {
                ["SystemAccentColorPrimary"] = Color.FromRgb(1, 2, 3),
                ["SystemAccentColorSecondary"] = Color.FromRgb(4, 5, 6),
                ["SystemAccentColorTertiary"] = Color.FromRgb(7, 8, 9),
                ["AccentFillColorDefault"] = Color.FromRgb(10, 11, 12),
                ["AccentFillColorSecondary"] = Color.FromArgb(229, 13, 14, 15),
                ["AccentFillColorTertiary"] = Color.FromArgb(204, 16, 17, 18),
                ["AccentButtonBackground"] = previousBrush
            };
            var previousBrushes = UiThemeService.CaptureAccentBrushes(resources);
            resources["AccentButtonBackground"] = new SolidColorBrush(System.Windows.Media.Colors.Red);

            UiThemeService.ApplyDerivedAccentBrushes(resources, previousBrushes);

            Assert.AreSame(previousBrush, resources["AccentButtonBackground"]);
            AssertBrushMatchesColor(resources, "AccentButtonBackground", "AccentFillColorDefault");
        });
    }

    private static void AssertBrushMatchesColor(ResourceDictionary resources, string brushKey, string colorKey)
    {
        Assert.AreEqual((Color)resources[colorKey],
            ((SolidColorBrush)resources[brushKey]).Color, brushKey);
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
