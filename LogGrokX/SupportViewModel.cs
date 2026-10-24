using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Input;
using LogGrokX.Localization;
using LogGrokX.Platform;

namespace LogGrokX
{
    public class SupportViewModel : ViewModelBase
    {
        private const string RepositoryUrl = "https://github.com/zhenyatnk/LogGrokX";

        private readonly IUpdateService? _updateCheckService;
        private readonly IDesktopService _desktop;
        private bool _isCheckingForUpdates;
        private string _updateStatus = string.Empty;

        public SupportViewModel(IUpdateService? updateCheckService = null, IDesktopService? desktop = null)
        {
            _updateCheckService = updateCheckService;
            _desktop = desktop ?? DesktopServices.Current;
            CheckForUpdatesCommand = new DelegateCommand(CheckForUpdates);
            OpenReleasesCommand = new DelegateCommand(() => _desktop.OpenUrl($"{RepositoryUrl}/releases/latest"));
            OpenIssuesCommand = new DelegateCommand(() => _desktop.OpenUrl($"{RepositoryUrl}/issues/new"));
            OpenRepositoryCommand = new DelegateCommand(() => _desktop.OpenUrl(RepositoryUrl));
            OpenLogsFolderCommand = new DelegateCommand(OpenLogsFolder);
            CopyDiagnosticsCommand = new DelegateCommand(() => _desktop.SetClipboardText(DiagnosticsText));
        }

        public string Version => BuildInfo.Version;

        public string Commit => ShortCommit(ThisAssembly.Git.Commit);

        public string Branch => ThisAssembly.Git.Branch;

        public string RuntimeVersion => RuntimeInformation.FrameworkDescription;

        public string OsVersion => RuntimeInformation.OSDescription;

        public string Architecture => RuntimeInformation.ProcessArchitecture.ToString();

        public string DiagnosticsText =>
            $"LogGrokX {Version}{Environment.NewLine}" +
            $"Commit: {Commit} ({Branch}){Environment.NewLine}" +
            $"Runtime: {RuntimeVersion}{Environment.NewLine}" +
            $"OS: {OsVersion}{Environment.NewLine}" +
            $"Architecture: {Architecture}";

        public ICommand CheckForUpdatesCommand { get; }

        public bool CanCheckForUpdates => _updateCheckService != null && !_isCheckingForUpdates;

        public string UpdateStatus
        {
            get => _updateStatus;
            private set
            {
                _updateStatus = value;
                InvokePropertyChanged();
            }
        }

        public ICommand OpenReleasesCommand { get; }

        public ICommand OpenIssuesCommand { get; }

        public ICommand OpenRepositoryCommand { get; }

        public ICommand OpenLogsFolderCommand { get; }

        public ICommand CopyDiagnosticsCommand { get; }

        private async void CheckForUpdates()
        {
            if (_updateCheckService == null || _isCheckingForUpdates)
                return;

            _isCheckingForUpdates = true;
            InvokePropertyChanged(nameof(CanCheckForUpdates));
            UpdateStatus = Strings.Get("CheckingUpdates");
            try
            {
                var release = await _updateCheckService.CheckNowAsync();
                UpdateStatus = release == null
                    ? Strings.Get("ReleaseInfoUnavailable")
                    : UpdateVersion.IsNewer(release.Tag, Version)
                        ? Strings.Format("VersionAvailable", release.Tag)
                        : Strings.Get("LatestVersion");
            }
            catch (Exception e)
            {
                UpdateStatus = Strings.Format("UpdateCheckFailed", e.Message);
            }
            finally
            {
                _isCheckingForUpdates = false;
                InvokePropertyChanged(nameof(CanCheckForUpdates));
            }
        }

        private static string ShortCommit(string commit)
        {
            if (string.IsNullOrEmpty(commit) || commit.Trim('0').Length == 0)
                return Strings.Get("Unknown");
            return commit.Substring(0, Math.Min(8, commit.Length));
        }

        private void OpenLogsFolder()
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LogGrokX");
            if (Directory.Exists(path)) _desktop.OpenPath(path);
        }
    }
}
