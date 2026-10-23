using System;
using System.Collections;
using System.Windows;
using System.Windows.Media;
using AvalonDock.Themes;
using AvalonDock.Themes.VS2013.Themes;

namespace LogGrokX.Theming;

public sealed class DockThemeResources : ResourceDictionary
{
    public DockThemeResources()
    {
        MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/AvalonDock.Themes.VS2013;component/Themes/Icons/IconGeometry.xaml", UriKind.Relative)
        });
        Apply(false);
    }

    public void Apply(bool isDark)
    {
        DictionaryTheme theme = isDark ? new Vs2013DarkTheme() : new Vs2013LightTheme();
        foreach (DictionaryEntry resource in theme.ThemeResourceDictionary)
        {
            if (resource.Key is ComponentResourceKey && resource.Value is Brush)
                this[resource.Key] = resource.Value;
        }
    }

    public void ApplyAccentColor()
    {
        if (Application.Current?.Resources["SystemAccentColorPrimary"] is Color accentColor)
            ApplyAccentColor(accentColor);
    }

    internal void ApplyAccentColor(Color accentColor)
    {
        ApplyAccentColor(this, accentColor);
    }

    internal static void ApplyAccentColor(ResourceDictionary resources)
    {
        if (Application.Current?.Resources["SystemAccentColorPrimary"] is Color accentColor)
            ApplyAccentColor(resources, accentColor);
    }

    internal static void ApplyAccentColor(ResourceDictionary resources, Color accentColor)
    {
        resources[ResourceKeys.ControlAccentBrushKey] = new SolidColorBrush(accentColor);
        resources[ResourceKeys.DocumentWellTabSelectedActiveBackground] = new SolidColorBrush(accentColor);
        resources[ResourceKeys.DocumentWellTabSelectedInactiveBackground] = new SolidColorBrush(accentColor);
        resources[ResourceKeys.DocumentWellTabUnselectedHoveredBackground] = new SolidColorBrush(accentColor);
        resources[ResourceKeys.ToolWindowCaptionActiveBackground] = new SolidColorBrush(accentColor);
        resources[ResourceKeys.ToolWindowTabSelectedActiveBackground] = new SolidColorBrush(accentColor);
        resources[ResourceKeys.ToolWindowTabSelectedInactiveBackground] = new SolidColorBrush(accentColor);
        resources[ResourceKeys.ToolWindowTabUnselectedHoveredBackground] = new SolidColorBrush(accentColor);
        resources[ResourceKeys.AutoHideTabHoveredBackground] = new SolidColorBrush(accentColor);
        resources[ResourceKeys.AutoHideTabHoveredBorder] = new SolidColorBrush(accentColor);
        resources[ResourceKeys.NavigatorWindowSelectedBackground] = new SolidColorBrush(accentColor);
    }
}
