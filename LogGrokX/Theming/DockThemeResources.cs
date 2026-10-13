using System;
using System.Collections;
using System.Windows;
using System.Windows.Media;
using AvalonDock.Themes;

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
}
