namespace LogGrokX.Platform;

public enum UpdateState { None, Available, Downloading, Ready }

public readonly record struct UpdateStatus(UpdateState State, string? Version, double Progress);

public interface IUpdateService
{
    UpdateStatus Status { get; }
    event Action? StatusChanged;
    bool CanInstall(ReleaseInfo release);
    Task<ReleaseInfo?> CheckNowAsync();
    Task DownloadAvailableAsync();
    Task DownloadInstallerAsync(ReleaseInfo release, IProgress<double> progress, CancellationToken cancellationToken);
    void SkipVersion(string tag);
    bool RestartAndInstall(IEnumerable<string> openFiles);
}

public interface IUpdatePresenter
{
    void ShowUpdate(IUpdateService service, ReleaseInfo release, Action closed);
}
