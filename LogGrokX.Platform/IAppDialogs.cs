namespace LogGrokX.Platform;

public interface IAppDialogs
{
    IReadOnlyList<string> SelectLogFiles();
    void ShowWarning(string message, string title);
    void ShowSettings();
    void ShowSupport();
    void ShowSavedSearches();
}
