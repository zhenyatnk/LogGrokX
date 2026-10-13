using System;
using System.Windows.Input;
using LogGrokX.Localization;

namespace LogGrokX;

public sealed class WhatsNewViewModel : ViewModelBase
{
    private readonly string _pageUrl;

    public WhatsNewViewModel(string version, string notes, string pageUrl)
    {
        _pageUrl = pageUrl;
        Version = version.TrimStart('v', 'V');
        ReleaseNotes = string.IsNullOrWhiteSpace(notes) ? Strings.Get("NoReleaseNotes") : notes.Trim();

        OpenReleasePageCommand = new DelegateCommand(() => UpdateCheckService.OpenUrl(_pageUrl));
        CloseCommand = new DelegateCommand(Close);
    }

    public event Action? CloseRequested;

    public string Version { get; }
    public string UpdatedTitle => Strings.Format("UpdatedTitle", Version);

    public string ReleaseNotes { get; }

    public ICommand OpenReleasePageCommand { get; }

    public ICommand CloseCommand { get; }

    private void Close() => CloseRequested?.Invoke();
}
