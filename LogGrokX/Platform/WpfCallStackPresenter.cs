using System.Windows;
using LogGrokX.Symbols;

namespace LogGrokX.Platform;

public sealed class WpfCallStackPresenter : ICallStackPresenter
{
    public void Show(CallStackViewModel viewModel)
    {
        var window = new CallStackWindow(viewModel);
        if (Application.Current?.MainWindow is { IsVisible: true } owner)
            window.Owner = owner;
        window.Show();
        window.Activate();
    }
}
