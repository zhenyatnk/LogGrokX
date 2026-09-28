using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace LogGrokX;

public sealed record ReleaseInfo(string Tag, string PageUrl, string Notes, IReadOnlyDictionary<string, string> Assets);

public sealed record PendingRelease(string Version, string Notes, string PageUrl);

public class UpdateCheckService
{
    private const string LatestReleaseApiUrl = "https://api.github.com/repos/zhenyatnk/LogGrokX/releases/latest";
    private const string LatestReleasePageUrl = "https://github.com/zhenyatnk/LogGrokX/releases/latest";
    private const string ReleaseApiUrlPrefix = "https://api.github.com/repos/zhenyatnk/LogGrokX/releases/tags/";
    private const string ReleasePageUrlPrefix = "https://github.com/zhenyatnk/LogGrokX/releases/tag/";
    private const string ReleaseDownloadUrlPrefix = "https://github.com/zhenyatnk/LogGrokX/releases/download/";
    private const string ChecksumsAssetName = "SHA256SUMS.txt";

    internal const string FallbackReleaseNotes =
        "Release notes could not be loaded from GitHub right now. Open the release page to read them.";

    // Unauthenticated GitHub API allows only 60 requests per hour per IP address, and users behind
    // a corporate NAT/proxy share that quota (#40). The latest tag is therefore resolved through the
    // redirect of the releases/latest web page, which does not consume the API quota; the API is
    // called only when a newer version is found, to get release notes and assets.
    private readonly Dictionary<string, ReleaseInfo> _releaseCache = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _apiRateLimitedUntilUtc = DateTime.MinValue;

    private readonly ApplicationSettings _settings;
    private string? _pendingInstallerPath;
    private bool _isWindowOpen;
    private bool _isDownloading;

    public UpdateCheckService(ApplicationSettings settings)
    {
        _settings = settings;
    }

    public static bool IsInstalled =>
        File.Exists(Path.Combine(AppContext.BaseDirectory, "unins000.exe"));

    public bool HasPendingInstall => _pendingInstallerPath != null;

    public async void CheckOnStartup(Window owner)
    {
        await CheckAutomaticallyAsync(owner);

        var timer = new DispatcherTimer { Interval = TimeSpan.FromHours(1) };
        timer.Tick += async (_, _) => await CheckAutomaticallyAsync(owner);
        timer.Start();
    }

    public async Task<ReleaseInfo?> CheckNowAsync(Window owner)
    {
        var tag = await GetLatestTagAsync();
        WriteLastCheck(DateTime.UtcNow);
        if (tag == null)
            return null;
        if (!UpdateVersion.IsNewer(tag, BuildInfo.Version))
            return new ReleaseInfo(tag, GetReleasePageUrl(tag), string.Empty, new Dictionary<string, string>());

        var release = await GetReleaseDetailsAsync(tag);
        ShowUpdateWindow(owner, release);
        return release;
    }

    // Проверка выполняется при первом запуске (или тике таймера) в новом календарном дне
    // по локальному времени, а не через 24 часа после предыдущей проверки.
    public static bool IsCheckDue(DateTime? lastCheckUtc, DateTime nowUtc) =>
        lastCheckUtc == null || IsCheckDueLocal(lastCheckUtc.Value.ToLocalTime(), nowUtc.ToLocalTime());

    public static bool IsCheckDueLocal(DateTime lastCheckLocal, DateTime nowLocal) =>
        lastCheckLocal.Date != nowLocal.Date;

    private async Task CheckAutomaticallyAsync(Window owner)
    {
        var mode = _settings.ViewSettings.UpdateMode;
        if (mode == ViewSettings.UpdateModeKind.Disabled || _isWindowOpen || HasPendingInstall || _isDownloading)
            return;
        if (!IsCheckDue(ReadLastCheck(), DateTime.UtcNow))
            return;

        try
        {
            var tag = await GetLatestTagAsync();
            WriteLastCheck(DateTime.UtcNow);
            if (tag == null || !UpdateVersion.IsNewer(tag, BuildInfo.Version))
                return;
            if (string.Equals(ReadSkippedVersion(), tag, StringComparison.OrdinalIgnoreCase))
                return;

            var release = await GetReleaseDetailsAsync(tag);

            if (mode == ViewSettings.UpdateModeKind.Install && IsInstalled && GetInstallerAssetName(release) != null)
            {
                await DownloadInBackgroundAsync(release);
                return;
            }

            ShowUpdateWindow(owner, release);
        }
        catch (Exception e)
        {
            Trace.TraceWarning($"Update check failed: {e.Message}");
        }
    }

    private async Task DownloadInBackgroundAsync(ReleaseInfo release)
    {
        _isDownloading = true;
        try
        {
            await DownloadInstallerAsync(release, new Progress<double>(), CancellationToken.None);
            Trace.TraceInformation($"Update {release.Tag} downloaded and will be installed on exit.");
        }
        finally
        {
            _isDownloading = false;
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

    private static string PendingReleaseFileName => HomeDirectoryPathProvider.GetDataFileFullPath("pending-update.json");

    private static void SavePendingRelease(ReleaseInfo release)
    {
        try
        {
            var pending = new PendingRelease(release.Tag, release.Notes, release.PageUrl);
            File.WriteAllText(PendingReleaseFileName, JsonSerializer.Serialize(pending));
        }
        catch (Exception e)
        {
            Trace.TraceWarning($"Failed to save pending release notes: {e.Message}");
        }
    }

    public static PendingRelease? TakePendingReleaseForCurrentVersion()
    {
        try
        {
            if (!File.Exists(PendingReleaseFileName))
                return null;

            var json = File.ReadAllText(PendingReleaseFileName);
            File.Delete(PendingReleaseFileName);

            var pending = JsonSerializer.Deserialize<PendingRelease>(json);
            return pending != null &&
                   string.Equals(NormalizeVersion(pending.Version), NormalizeVersion(BuildInfo.Version),
                       StringComparison.OrdinalIgnoreCase)
                ? pending
                : null;
        }
        catch (Exception e)
        {
            Trace.TraceWarning($"Failed to read pending release notes: {e.Message}");
            return null;
        }
    }

    private static string NormalizeVersion(string version) => version.Trim().TrimStart('v', 'V');

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

        if (release.Assets.TryGetValue(ChecksumsAssetName, out var sumsUrl))
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
        SavePendingRelease(release);
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

    private static HttpClient CreateClient(TimeSpan timeout, bool allowAutoRedirect = true)
    {
        var handler = CreateHandler();
        handler.AllowAutoRedirect = allowAutoRedirect;
        var client = new HttpClient(handler, disposeHandler: true) { Timeout = timeout };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LogGrokX");
        return client;
    }

    // Corporate proxies often require NTLM/Kerberos authentication (HTTP 407).
    // Use the system proxy and pass the current Windows user's credentials to it.
    internal static SocketsHttpHandler CreateHandler() => new()
    {
        UseProxy = true,
        DefaultProxyCredentials = CredentialCache.DefaultCredentials
    };

    // Resolves the latest release tag from the redirect of https://github.com/.../releases/latest
    // (-> .../releases/tag/<tag>). Falls back to the API only if the redirect cannot be parsed.
    private async Task<string?> GetLatestTagAsync()
    {
        using (var client = CreateClient(TimeSpan.FromSeconds(10), allowAutoRedirect: false))
        using (var response = await client.GetAsync(LatestReleasePageUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            var location = response.Headers.Location;
            if (location != null && !location.IsAbsoluteUri)
                location = new Uri(new Uri(LatestReleasePageUrl), location);

            var tag = ParseTagFromReleaseUrl(location);
            if (tag != null)
                return tag;

            Trace.TraceWarning(
                $"Could not resolve the latest release from {LatestReleasePageUrl} (status {(int)response.StatusCode}), falling back to GitHub API.");
        }

        var release = await GetReleaseFromApiAsync(LatestReleaseApiUrl);
        return release?.Tag;
    }

    internal static string? ParseTagFromReleaseUrl(Uri? url)
    {
        if (url == null || !url.IsAbsoluteUri)
            return null;

        var segments = url.AbsolutePath.Trim('/').Split('/');
        if (segments.Length < 2 || !string.Equals(segments[^2], "tag", StringComparison.OrdinalIgnoreCase))
            return null;

        var tag = Uri.UnescapeDataString(segments[^1]).Trim();
        return tag.Length == 0 ? null : tag;
    }

    // Gets release notes and assets for a tag. If the API is unavailable (e.g. rate limit exceeded),
    // returns a release with the well-known asset URLs published by the release workflow.
    private async Task<ReleaseInfo> GetReleaseDetailsAsync(string tag)
    {
        if (_releaseCache.TryGetValue(tag, out var cached))
            return cached;

        var release = await GetReleaseFromApiAsync(ReleaseApiUrlPrefix + Uri.EscapeDataString(tag));
        if (release != null && string.Equals(release.Tag, tag, StringComparison.OrdinalIgnoreCase))
            return release;

        return CreateFallbackRelease(tag);
    }

    private async Task<ReleaseInfo?> GetReleaseFromApiAsync(string url)
    {
        var nowUtc = DateTime.UtcNow;
        if (nowUtc < _apiRateLimitedUntilUtc)
        {
            Trace.TraceInformation($"GitHub API rate limit is exceeded until {_apiRateLimitedUntilUtc:O}, skipping request.");
            return null;
        }

        try
        {
            using var client = CreateClient(TimeSpan.FromSeconds(10));
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            using var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                // Status is checked without EnsureSuccessStatusCode to avoid first-chance exceptions in the log.
                var resetUtc = GetRateLimitResetUtc(response.StatusCode, response.Headers, nowUtc);
                if (resetUtc != null)
                {
                    _apiRateLimitedUntilUtc = resetUtc.Value;
                    Trace.TraceWarning($"GitHub API rate limit exceeded, next request after {resetUtc.Value:O}.");
                }
                else
                {
                    Trace.TraceWarning($"GitHub API request failed: {(int)response.StatusCode} ({response.ReasonPhrase}).");
                }

                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var document = await JsonDocument.ParseAsync(stream);
            var release = ParseRelease(document.RootElement);
            if (release != null)
                _releaseCache[release.Tag] = release;
            return release;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            Trace.TraceWarning($"GitHub API request failed: {e.Message}");
            return null;
        }
    }

    // Returns the time until which GitHub API requests should not be made, or null if the response
    // is not a rate limit error. See https://docs.github.com/rest/using-the-rest-api/rate-limits-for-the-rest-api
    internal static DateTime? GetRateLimitResetUtc(HttpStatusCode statusCode, HttpResponseHeaders headers, DateTime nowUtc)
    {
        if (statusCode != HttpStatusCode.Forbidden && statusCode != HttpStatusCode.TooManyRequests)
            return null;

        var retryAfter = headers.RetryAfter;
        if (retryAfter?.Delta is { } delta)
            return nowUtc + delta;
        if (retryAfter?.Date is { } date)
            return date.UtcDateTime;

        if (TryGetHeader(headers, "X-RateLimit-Remaining") == "0" &&
            long.TryParse(TryGetHeader(headers, "X-RateLimit-Reset"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var reset))
            return DateTimeOffset.FromUnixTimeSeconds(reset).UtcDateTime;

        // Secondary rate limit without explicit reset time: wait at least a minute.
        return statusCode == HttpStatusCode.TooManyRequests || TryGetHeader(headers, "X-RateLimit-Remaining") == "0"
            ? nowUtc.AddMinutes(1)
            : null;
    }

    private static string? TryGetHeader(HttpResponseHeaders headers, string name) =>
        headers.TryGetValues(name, out var values) ? values.FirstOrDefault()?.Trim() : null;

    internal static ReleaseInfo CreateFallbackRelease(string tag)
    {
        var version = tag.TrimStart('v', 'V');
        var downloadPrefix = ReleaseDownloadUrlPrefix + Uri.EscapeDataString(tag) + "/";
        var assets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { $"LogGrokX-{version}-x64-setup.exe", $"LogGrokX-{version}-x86-setup.exe", ChecksumsAssetName })
            assets[name] = downloadPrefix + Uri.EscapeDataString(name);

        return new ReleaseInfo(tag, GetReleasePageUrl(tag), FallbackReleaseNotes, assets);
    }

    private static string GetReleasePageUrl(string tag) => ReleasePageUrlPrefix + Uri.EscapeDataString(tag);

    private static ReleaseInfo? ParseRelease(JsonElement root)
    {
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

        return new ReleaseInfo(tag, string.IsNullOrWhiteSpace(url) ? GetReleasePageUrl(tag) : url, notes ?? string.Empty, assets);
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
