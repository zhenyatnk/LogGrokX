using System;
using System.Threading;
using System.Windows.Input;

namespace LogGrokX;

public sealed class UpdateViewModel : ViewModelBase
{
    private readonly UpdateCheckService _service;
    private readonly ReleaseInfo _release;
    private CancellationTokenSource? _cancellation;
    private bool _isDownloading;
    private bool _isReady;
    private double _progress;
    private string _status = string.Empty;

    public UpdateViewModel(UpdateCheckService service, ReleaseInfo release)
    {
        _service = service;
        _release = release;
        CanInstall = UpdateCheckService.IsInstalled && UpdateCheckService.GetInstallerAssetName(release) != null;

        UpdateCommand = new DelegateCommand(Update);
        LaterCommand = new DelegateCommand(Close);
        SkipCommand = new DelegateCommand(() =>
        {
            _service.SkipVersion(_release.Tag);
            Close();
        });
        OpenReleasePageCommand = new DelegateCommand(() => UpdateCheckService.OpenUrl(_release.PageUrl));
    }

    public event Action? CloseRequested;

    public string CurrentVersion => BuildInfo.Version;

    public string NewVersion => _release.Tag;

    public string ReleaseNotes => string.IsNullOrWhiteSpace(_release.Notes) ? "No release notes." : _release.Notes.Trim();

    public bool CanInstall { get; }

    public string UpdateButtonText => CanInstall ? "Update" : "Open download page";

    public bool IsDownloading
    {
        get => _isDownloading;
        private set
        {
            _isDownloading = value;
            InvokePropertyChanged();
            InvokePropertyChanged(nameof(IsIdle));
        }
    }

    public bool IsReady
    {
        get => _isReady;
        private set
        {
            _isReady = value;
            InvokePropertyChanged();
            InvokePropertyChanged(nameof(IsIdle));
        }
    }

    public bool IsIdle => !IsDownloading && !IsReady;

    public double Progress
    {
        get => _progress;
        private set
        {
            _progress = value;
            InvokePropertyChanged();
        }
    }

    public string Status
    {
        get => _status;
        private set
        {
            _status = value;
            InvokePropertyChanged();
        }
    }

    public ICommand UpdateCommand { get; }

    public ICommand LaterCommand { get; }

    public ICommand SkipCommand { get; }

    public ICommand OpenReleasePageCommand { get; }

    public void CancelDownload() => _cancellation?.Cancel();

    private async void Update()
    {
        if (!CanInstall)
        {
            UpdateCheckService.OpenUrl(_release.PageUrl);
            Close();
            return;
        }

        _cancellation = new CancellationTokenSource();
        IsDownloading = true;
        Status = "Downloading update...";
        try
        {
            var progress = new Progress<double>(value => Progress = value * 100);
            await _service.DownloadInstallerAsync(_release, progress, _cancellation.Token);
            Progress = 100;
            IsReady = true;
            Status = $"LogGrokX {_release.Tag} will be installed silently when you close the application.";
        }
        catch (OperationCanceledException)
        {
            Status = string.Empty;
        }
        catch (Exception e)
        {
            Status = $"Update failed: {e.Message}";
        }
        finally
        {
            IsDownloading = false;
        }
    }

    private void Close() => CloseRequested?.Invoke();
}
