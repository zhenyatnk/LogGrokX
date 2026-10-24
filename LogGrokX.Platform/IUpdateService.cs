namespace LogGrokX.Platform;

public interface IUpdateService
{
    bool CanInstall(ReleaseInfo release);
    Task<ReleaseInfo?> CheckNowAsync();
    Task DownloadInstallerAsync(ReleaseInfo release, IProgress<double> progress, CancellationToken cancellationToken);
    void SkipVersion(string tag);
}

public interface IUpdatePresenter
{
    void ShowUpdate(IUpdateService service, ReleaseInfo release, Action closed);
}
