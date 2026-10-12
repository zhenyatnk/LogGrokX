using System;
using System.Threading;
using System.Windows.Input;
using LogGrokX.Localization;

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
    public string AvailableTitle => Strings.Format("AvailableTitle", NewVersion);

    public string ReleaseNotes => string.IsNullOrWhiteSpace(_release.Notes) ? Strings.Get("NoReleaseNotes") : _release.Notes.Trim();

    public bool CanInstall { get; }

    public string UpdateButtonText => CanInstall ? Strings.Get("Update") : Strings.Get("OpenDownloadPage");

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
        Status = Strings.Get("DownloadingUpdate");
        try
        {
            var progress = new Progress<double>(value => Progress = value * 100);
            await _service.DownloadInstallerAsync(_release, progress, _cancellation.Token);
            Progress = 100;
            IsReady = true;
            Status = Strings.Format("UpdateReady", _release.Tag);
        }
        catch (OperationCanceledException)
        {
            Status = string.Empty;
        }
        catch (Exception e)
        {
            Status = Strings.Format("UpdateFailed", e.Message);
        }
        finally
        {
            IsDownloading = false;
        }
    }

    private void Close() => CloseRequested?.Invoke();
}
