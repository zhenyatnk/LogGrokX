using System;
using System.Globalization;
using System.Windows.Data;
using AvalonDock.Themes;

namespace LogGrokX.Theming;

public sealed class DockThemeConverter : IValueConverter
{
    private static readonly Vs2013LightTheme Light = new();
    private static readonly Vs2013DarkTheme Dark = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        ApplyAccentColor();
        return value is true ? Dark : Light;
    }

    internal static void ApplyAccentColor()
    {
        DockThemeResources.ApplyAccentColor(Light.ThemeResourceDictionary);
        DockThemeResources.ApplyAccentColor(Dark.ThemeResourceDictionary);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
