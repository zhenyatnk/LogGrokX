using System;
using System.Linq;
using System.Windows;

namespace LogGrokX.Platform;

public sealed class WpfUpdatePresenter(IDesktopService desktop) : IUpdatePresenter
{
    public void ShowUpdate(IUpdateService service, ReleaseInfo release, Action closed)
    {
        var window = new UpdateWindow(new UpdateViewModel(service, release, desktop));
        var owner = Application.Current?.Windows.OfType<SupportWindow>().FirstOrDefault(w => w.IsVisible)
            ?? Application.Current?.MainWindow;
        if (owner is { IsVisible: true })
            window.Owner = owner;
        window.Closed += (_, _) => closed();
        window.Show();
        window.Activate();
    }
}
