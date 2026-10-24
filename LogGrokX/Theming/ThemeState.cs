using System.ComponentModel;
using Wpf.Ui.Appearance;

namespace LogGrokX.Theming;

public sealed class ThemeState : INotifyPropertyChanged
{
    public static ThemeState Current { get; } = new();
    public event PropertyChangedEventHandler? PropertyChanged;

    private ThemeState()
    {
        ApplicationThemeManager.Changed += (_, _) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDark)));
    }

    public bool IsDark => ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;
}
