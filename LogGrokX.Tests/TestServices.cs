#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using LogGrokX.MarkedLines;
using LogGrokX.Platform;
using LogGrokX.Search;
using LogGrokX.Theming;

namespace LogGrokX.Tests;

internal static class TestServices
{
    internal static IDesktopService Desktop { get; } = new DesktopStub();
    internal static IAppDialogs Dialogs { get; } = new DialogsStub();
    internal static IUpdateService Updates { get; } = new UpdatesStub();
    internal static IUiDispatcher Inline { get; } = new InlineDispatcher();
    internal static DisplaySettingsMonitor DisplaySettings { get; } = new(Inline);

    internal static MainWindowViewModel CreateMain(ApplicationSettings settings)
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("UI fixtures require an STA thread.");
        var dispatcher = new WpfUiDispatcher(Dispatcher.CurrentDispatcher);
        return new MainWindowViewModel(settings, new SearchAutocompleteCache(), new SavedSearchPatternStore(),
            new UiThemeService(), new TimelinePlacementService(settings), new TextZoomService(settings),
            new ThreadGroupingService(settings), new MergedFilesViewService(), Dialogs,
            documents => new MarkedLinesViewModel(documents), new WpfColorValidator(), dispatcher, Desktop,
            new DisplaySettingsMonitor(dispatcher));
    }

    private sealed class DesktopStub : IDesktopService
    {
        public void OpenUrl(string url) => throw new InvalidOperationException("Unexpected shell command.");
        public void OpenPath(string path) => throw new InvalidOperationException("Unexpected shell command.");
        public void RevealFile(string path) => throw new InvalidOperationException("Unexpected shell command.");
        public void SetClipboardText(string text) => throw new InvalidOperationException("Unexpected clipboard command.");
    }

    private sealed class DialogsStub : IAppDialogs
    {
        public IReadOnlyList<string> SelectLogFiles() => [];
        public void ShowWarning(string message, string title) { }
        public void ShowSettings() => throw new InvalidOperationException("Unexpected dialog.");
        public void ShowSupport() => throw new InvalidOperationException("Unexpected dialog.");
        public void ShowSavedSearches() => throw new InvalidOperationException("Unexpected dialog.");
    }

    private sealed class UpdatesStub : IUpdateService
    {
        public bool CanInstall(ReleaseInfo release) => false;
        public Task<ReleaseInfo?> CheckNowAsync() => Task.FromResult<ReleaseInfo?>(null);
        public Task DownloadInstallerAsync(ReleaseInfo release, IProgress<double> progress, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Unexpected download.");
        public void SkipVersion(string tag) { }
    }

    private sealed class InlineDispatcher : IUiDispatcher
    {
        public bool CheckAccess() => true;
        public IDisposable Post(Action action) => throw new InvalidOperationException("Unexpected dispatch.");
        public IUiTimer CreateTimer(TimeSpan interval, Action tick, UiTimerPriority priority = UiTimerPriority.Normal) =>
            throw new InvalidOperationException("Unexpected timer.");
    }
}
