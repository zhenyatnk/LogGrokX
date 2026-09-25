using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace LogGrokX;

public class UpdateCheckService
{
    private const string LatestReleaseApiUrl = "https://api.github.com/repos/zhenyatnk/LogGrokX/releases/latest";
    private const string LatestReleasePageUrl = "https://github.com/zhenyatnk/LogGrokX/releases/latest";

    private readonly ApplicationSettings _settings;

    public UpdateCheckService(ApplicationSettings settings)
    {
        _settings = settings;
    }

    public async void CheckOnStartup(Window owner)
    {
        if (!_settings.ViewSettings.CheckForUpdates)
            return;

        try
        {
            var release = await GetLatestReleaseAsync();
            if (release == null)
                return;

            var (tag, url) = release.Value;
            if (!UpdateVersion.IsNewer(tag, BuildInfo.Version))
                return;
            if (string.Equals(ReadSkippedVersion(), tag, StringComparison.OrdinalIgnoreCase))
                return;

            var result = MessageBox.Show(
                owner,
                $"A new version of LogGrokX is available: {tag} (current: {BuildInfo.Version}).{Environment.NewLine}{Environment.NewLine}" +
                $"Yes — open the download page{Environment.NewLine}" +
                $"No — remind me later{Environment.NewLine}" +
                "Cancel — skip this version",
                "LogGrokX update",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Information);

            if (result == MessageBoxResult.Yes)
                OpenUrl(url);
            else if (result == MessageBoxResult.Cancel)
                WriteSkippedVersion(tag);
        }
        catch (Exception e)
        {
            Trace.TraceWarning($"Update check failed: {e.Message}");
        }
    }

    private static async Task<(string Tag, string Url)?> GetLatestReleaseAsync()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LogGrokX");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

        using var response = await client.GetAsync(LatestReleaseApiUrl);
        if (!response.IsSuccessStatusCode)
            return null;

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        var root = document.RootElement;

        if (!root.TryGetProperty("tag_name", out var tagElement))
            return null;
        var tag = tagElement.GetString();
        if (string.IsNullOrWhiteSpace(tag))
            return null;

        var url = root.TryGetProperty("html_url", out var urlElement) ? urlElement.GetString() : null;
        return (tag, string.IsNullOrWhiteSpace(url) ? LatestReleasePageUrl : url);
    }

    private static string SkippedVersionFileName => HomeDirectoryPathProvider.GetDataFileFullPath("skipped-update.settings");

    private static string? ReadSkippedVersion()
    {
        try
        {
            return File.Exists(SkippedVersionFileName) ? File.ReadAllText(SkippedVersionFileName).Trim() : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void WriteSkippedVersion(string tag)
    {
        try
        {
            File.WriteAllText(SkippedVersionFileName, tag);
        }
        catch (Exception e)
        {
            Trace.TraceWarning($"Failed to save skipped update version: {e.Message}");
        }
    }

    private static void OpenUrl(string url)
    {
        using var process = new Process { StartInfo = { FileName = url, UseShellExecute = true } };
        process.Start();
    }
}
