using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace LogGrokX;

public sealed record ReleaseInfo(string Tag, string PageUrl, string Notes, IReadOnlyDictionary<string, string> Assets);

public class UpdateCheckService
{
    private const string LatestReleaseApiUrl = "https://api.github.com/repos/zhenyatnk/LogGrokX/releases/latest";
    private const string LatestReleasePageUrl = "https://github.com/zhenyatnk/LogGrokX/releases/latest";

    private readonly ApplicationSettings _settings;
    private string? _pendingInstallerPath;
    private bool _isWindowOpen;

    public UpdateCheckService(ApplicationSettings settings)
    {
        _settings = settings;
    }

    public static bool IsInstalled =>
        File.Exists(Path.Combine(AppContext.BaseDirectory, "unins000.exe"));

    public bool HasPendingInstall => _pendingInstallerPath != null;

    public static readonly TimeSpan CheckInterval = TimeSpan.FromDays(1);

    public async void CheckOnStartup(Window owner)
    {
        await CheckAutomaticallyAsync(owner);

        var timer = new DispatcherTimer { Interval = TimeSpan.FromHours(1) };
        timer.Tick += async (_, _) => await CheckAutomaticallyAsync(owner);
        timer.Start();
    }

    public async Task<ReleaseInfo?> CheckNowAsync(Window owner)
    {
        var release = await GetLatestReleaseAsync();
        WriteLastCheck(DateTime.UtcNow);
        if (release == null || !UpdateVersion.IsNewer(release.Tag, BuildInfo.Version))
            return release;

        ShowUpdateWindow(owner, release);
        return release;
    }

    public static bool IsCheckDue(DateTime? lastCheckUtc, DateTime nowUtc) =>
        lastCheckUtc == null || nowUtc - lastCheckUtc.Value >= CheckInterval || lastCheckUtc.Value > nowUtc;

    private async Task CheckAutomaticallyAsync(Window owner)
    {
        if (!_settings.ViewSettings.CheckForUpdates || _isWindowOpen || HasPendingInstall)
            return;
        if (!IsCheckDue(ReadLastCheck(), DateTime.UtcNow))
            return;

        try
        {
            var release = await GetLatestReleaseAsync();
            WriteLastCheck(DateTime.UtcNow);
            if (release == null || !UpdateVersion.IsNewer(release.Tag, BuildInfo.Version))
                return;
            if (string.Equals(ReadSkippedVersion(), release.Tag, StringComparison.OrdinalIgnoreCase))
                return;

            ShowUpdateWindow(owner, release);
        }
        catch (Exception e)
        {
            Trace.TraceWarning($"Update check failed: {e.Message}");
        }
    }

    private void ShowUpdateWindow(Window owner, ReleaseInfo release)
    {
        if (_isWindowOpen)
            return;

        var window = new UpdateWindow(new UpdateViewModel(this, release));
        if (owner.IsVisible)
            window.Owner = owner;
        _isWindowOpen = true;
        window.Closed += (_, _) => _isWindowOpen = false;
        window.Show();
        window.Activate();
    }

    private static string LastCheckFileName => HomeDirectoryPathProvider.GetDataFileFullPath("last-update-check.settings");

    private static DateTime? ReadLastCheck()
    {
        try
        {
            if (!File.Exists(LastCheckFileName))
                return null;
            return DateTime.TryParse(File.ReadAllText(LastCheckFileName).Trim(), CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var value)
                ? value
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void WriteLastCheck(DateTime nowUtc)
    {
        try
        {
            File.WriteAllText(LastCheckFileName, nowUtc.ToString("O", CultureInfo.InvariantCulture));
        }
        catch (Exception e)
        {
            Trace.TraceWarning($"Failed to save last update check time: {e.Message}");
        }
    }

    public static string? GetInstallerAssetName(ReleaseInfo release)
    {
        var arch = RuntimeInformation.ProcessArchitecture == Architecture.X86 ? "x86" : "x64";
        var version = release.Tag.TrimStart('v', 'V');
        var name = $"LogGrokX-{version}-{arch}-setup.exe";
        return release.Assets.ContainsKey(name) ? name : null;
    }

    public async Task DownloadInstallerAsync(ReleaseInfo release, IProgress<double> progress, CancellationToken cancellationToken)
    {
        var assetName = GetInstallerAssetName(release) ?? throw new InvalidOperationException("Installer is not available for this release.");
        var directory = Path.Combine(Path.GetTempPath(), "LogGrokX", "Update");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, assetName);

        using var client = CreateClient(TimeSpan.FromMinutes(10));
        using (var response = await client.GetAsync(release.Assets[assetName], HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? 0;
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = File.Create(path);
            var buffer = new byte[81920];
            long received = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                received += read;
                if (total > 0)
                    progress.Report((double)received / total);
            }
        }

        if (release.Assets.TryGetValue("SHA256SUMS.txt", out var sumsUrl))
        {
            var sums = await client.GetStringAsync(sumsUrl, cancellationToken);
            var expected = FindChecksum(sums, assetName);
            if (expected != null)
            {
                await using var stream = File.OpenRead(path);
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
                if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(path);
                    throw new InvalidOperationException("Checksum of the downloaded installer does not match.");
                }
            }
        }

        _pendingInstallerPath = path;
    }

    public void LaunchPendingInstaller()
    {
        if (_pendingInstallerPath == null || !File.Exists(_pendingInstallerPath))
            return;

        try
        {
            var perMachine = IsPerMachineInstall();
            var installerArgs = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /AUTOUPDATE=1 " + (perMachine ? "/ALLUSERS" : "/CURRENTUSER");
            var verb = perMachine ? " -Verb RunAs" : string.Empty;
            var script =
                $"Wait-Process -Id {Environment.ProcessId} -ErrorAction SilentlyContinue; " +
                $"Start-Process -FilePath '{_pendingInstallerPath.Replace("'", "''")}' -ArgumentList '{installerArgs}'{verb}";

            using var process = new Process
            {
                StartInfo =
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -NonInteractive -WindowStyle Hidden -Command \"{script}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
        }
        catch (Exception e)
        {
            Trace.TraceWarning($"Failed to launch update installer: {e.Message}");
        }
    }

    public static string? FindChecksum(string sums, string fileName)
    {
        foreach (var line in sums.Split('\n'))
        {
            var parts = line.Trim().Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && string.Equals(parts[1].TrimStart('*').Trim(), fileName, StringComparison.OrdinalIgnoreCase))
                return parts[0];
        }

        return null;
    }

    public void SkipVersion(string tag)
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

    public static void OpenUrl(string url)
    {
        using var process = new Process { StartInfo = { FileName = url, UseShellExecute = true } };
        process.Start();
    }

    private static bool IsPerMachineInstall()
    {
        var baseDirectory = AppContext.BaseDirectory;
        return new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 }
            .Select(Environment.GetFolderPath)
            .Where(folder => !string.IsNullOrEmpty(folder))
            .Any(folder => baseDirectory.StartsWith(folder, StringComparison.OrdinalIgnoreCase));
    }

    private static HttpClient CreateClient(TimeSpan timeout)
    {
        var client = new HttpClient { Timeout = timeout };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LogGrokX");
        return client;
    }

    private static async Task<ReleaseInfo?> GetLatestReleaseAsync()
    {
        using var client = CreateClient(TimeSpan.FromSeconds(10));
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

        using var response = await client.GetAsync(LatestReleaseApiUrl);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        var root = document.RootElement;

        var tag = root.TryGetProperty("tag_name", out var tagElement) ? tagElement.GetString() : null;
        if (string.IsNullOrWhiteSpace(tag))
            return null;

        var url = root.TryGetProperty("html_url", out var urlElement) ? urlElement.GetString() : null;
        var notes = root.TryGetProperty("body", out var bodyElement) ? bodyElement.GetString() : null;

        var assets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("assets", out var assetsElement) && assetsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assetsElement.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                var download = asset.TryGetProperty("browser_download_url", out var d) ? d.GetString() : null;
                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(download))
                    assets[name] = download;
            }
        }

        return new ReleaseInfo(tag, string.IsNullOrWhiteSpace(url) ? LatestReleasePageUrl : url, notes ?? string.Empty, assets);
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
}
