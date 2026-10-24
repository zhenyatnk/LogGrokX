using System;
using System.Collections.Generic;
using System.Windows;
using LogGrokX.Localization;
using LogGrokX.Settings;
using Microsoft.Win32;

namespace LogGrokX.Platform;

public sealed class WpfAppDialogs(Func<SettingsViewModel> settingsFactory, IUpdateService updates,
    IDesktopService desktop) : IAppDialogs
{
    public IReadOnlyList<string> SelectLogFiles()
    {
        var dialog = new OpenFileDialog
        {
            DefaultExt = "log", Filter = Strings.Get("OpenFileFilter"), Multiselect = true
        };
        return dialog.ShowDialog(Application.Current?.MainWindow) == true ? dialog.FileNames : Array.Empty<string>();
    }

    public void ShowWarning(string message, string title)
    {
        if (Application.Current?.MainWindow is { } owner)
            MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    public void ShowSettings() => ShowDialog(new SettingsWindow(settingsFactory()));

    public void ShowSupport() => ShowDialog(new SupportWindow(updates, desktop));

    private static void ShowDialog(Window window)
    {
        window.Owner = Application.Current?.MainWindow;
        window.ShowDialog();
    }
}
