namespace LogGrokX.Platform;

public interface IDesktopService
{
    void OpenUrl(string url);
    void OpenPath(string path);
    void RevealFile(string path);
    void SetClipboardText(string text);
}

public interface IAppDialogs
{
    IReadOnlyList<string> SelectLogFiles();
    void ShowWarning(string message, string title);
    void ShowSettings();
    void ShowSupport();
}

public interface IThemeService
{
    bool IsDark { get; }
    string Toggle();
    void ApplyAccentColor(string accentColor);
}

public interface IColorValidator
{
    bool IsValid(string value);
}

public static class DesktopServices
{
    public static IDesktopService Current { get; set; } = new UnavailableDesktopService();

    private sealed class UnavailableDesktopService : IDesktopService
    {
        public void OpenUrl(string url) => throw new PlatformNotSupportedException();
        public void OpenPath(string path) => throw new PlatformNotSupportedException();
        public void RevealFile(string path) => throw new PlatformNotSupportedException();
        public void SetClipboardText(string text) => throw new PlatformNotSupportedException();
    }
}
