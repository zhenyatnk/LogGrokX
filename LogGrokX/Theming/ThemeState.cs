using Wpf.Ui.Appearance;

namespace LogGrokX.Theming;

public sealed class ThemeState : ViewModelBase
{
    public static ThemeState Current { get; } = new();

    private ThemeState()
    {
        ApplicationThemeManager.Changed += (_, _) => InvokePropertyChanged(nameof(IsDark));
    }

    public bool IsDark => ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;
}
