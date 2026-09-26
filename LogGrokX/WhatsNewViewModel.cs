using System;
using System.Windows.Input;

namespace LogGrokX;

public sealed class WhatsNewViewModel : ViewModelBase
{
    private readonly string _pageUrl;

    public WhatsNewViewModel(string version, string notes, string pageUrl)
    {
        _pageUrl = pageUrl;
        Version = version.TrimStart('v', 'V');
        ReleaseNotes = string.IsNullOrWhiteSpace(notes) ? "No release notes." : notes.Trim();

        OpenReleasePageCommand = new DelegateCommand(() => UpdateCheckService.OpenUrl(_pageUrl));
        CloseCommand = new DelegateCommand(Close);
    }

    public event Action? CloseRequested;

    public string Version { get; }

    public string ReleaseNotes { get; }

    public ICommand OpenReleasePageCommand { get; }

    public ICommand CloseCommand { get; }

    private void Close() => CloseRequested?.Invoke();
}
