#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DryIoc;
using LogGrokX.Platform;
using LogGrokX.Search;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Tests;

[TestClass]
public sealed class PlatformBoundaryTests
{
    private static readonly ReleaseInfo Release = new("99.0", "https://example.test/release", "Notes", new Dictionary<string, string>());

    [TestMethod]
    public void ViewModelMembersDoNotExposeWpfOrConcretePlatformServices()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                                   BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var viewModels = typeof(ViewModelBase).Assembly.GetTypes().Where(type => typeof(ViewModelBase).IsAssignableFrom(type));
        foreach (var type in viewModels)
        {
            var members = type.GetFields(flags).Select(field => field.FieldType)
                .Concat(type.GetProperties(flags).Select(property => property.PropertyType))
                .Concat(type.GetMethods(flags).SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType)))
                .Concat(type.GetConstructors(flags).SelectMany(constructor => constructor.GetParameters().Select(parameter => parameter.ParameterType)));
            foreach (var member in members.SelectMany(ExpandType))
            {
                var assembly = member.Assembly.GetName().Name ?? string.Empty;
                Assert.IsFalse(assembly is "PresentationCore" or "PresentationFramework" or "WindowsBase" ||
                               assembly.StartsWith("AvalonDock", StringComparison.Ordinal) || assembly.StartsWith("Wpf.Ui", StringComparison.Ordinal),
                    $"{type.Name} depends on {member.FullName}.");
                Assert.IsFalse(member == typeof(UpdateCheckService) || member == typeof(Theming.UiThemeService),
                    $"{type.Name} must use a platform interface for {member.Name}.");
            }
        }
    }

    [TestMethod]
    public void PlatformContractsHaveNoWindowsDesktopReferences()
    {
        var references = typeof(IUiDispatcher).Assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();
        foreach (var forbidden in new[] { "PresentationCore", "PresentationFramework", "WindowsBase", "LogGrokX" })
            CollectionAssert.DoesNotContain(references, forbidden);
    }

    [TestMethod]
    public void ApplicationCompositionResolvesPlatformInterfacesAndMainViewModel()
    {
        using var container = new DryIoc.Container();
        Bootstrap.App.RegisterDependencies(container);
        var viewModel = container.Resolve<MainWindowViewModel>();

        Assert.IsNotNull(viewModel.MergedViewModel);
        Assert.IsInstanceOfType<IThemeService>(container.Resolve<IThemeService>());
        Assert.IsInstanceOfType<IAppDialogs>(container.Resolve<IAppDialogs>());
        Assert.IsInstanceOfType<IUpdateService>(container.Resolve<IUpdateService>());
        Assert.IsInstanceOfType<ICrashDumpService>(container.Resolve<ICrashDumpService>());
        Assert.IsInstanceOfType<ITitleBarService>(container.Resolve<ITitleBarService>());
    }

    [TestMethod]
    public void MainWindowCommandsUseInjectedDialogsWithoutCreatingWpfViews()
    {
        var settings = new ApplicationSettings();
        var dialogs = new Dialogs();
        var theme = new Theme();
        using var viewModel = new MainWindowViewModel(settings, new SearchAutocompleteCache(), new SavedSearchPatternStore(),
            theme, new TimelinePlacementService(settings), new TextZoomService(settings), new ThreadGroupingService(settings),
            new MergedFilesViewService(), dialogs, documents => new MarkedLines.MarkedLinesViewModel(documents),
            new AcceptColors(), new ManualDispatcher());

        viewModel.OpenSettings.Execute(null);
        viewModel.OpenSupportCommand.Execute(null);
        viewModel.OpenFileCommand.Execute(null);
        viewModel.ToggleThemeCommand.Execute(null);

        Assert.AreEqual(1, dialogs.SettingsCount);
        Assert.AreEqual(1, dialogs.SupportCount);
        Assert.AreEqual(1, dialogs.FileSelectionCount);
        Assert.AreEqual(0, viewModel.Documents.Count);
        Assert.IsTrue(viewModel.IsDarkTheme);
    }

    [TestMethod]
    public async Task DispatcherWithoutSynchronizationContextCanProcessPostedWork()
    {
        var previous = SynchronizationContext.Current;
        IUiDispatcher dispatcher;
        try
        {
            SynchronizationContext.SetSynchronizationContext(null);
            dispatcher = new SynchronizationContextDispatcher();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var work = dispatcher.Post(() => completed.SetResult(dispatcher.CheckAccess()));
        Assert.IsTrue(await completed.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [TestMethod]
    public void AccentPreviewAndCancellationUseThemeInterface()
    {
        var settings = new ApplicationSettings();
        settings.ViewSettings.AccentColor = ViewSettings.AccentColorKind.Blue;
        var theme = new Theme();
        var editor = new Settings.SettingsViewModel(settings, new TimelinePlacementService(settings),
            new ThreadGroupingService(settings), new MergedFilesViewService(), new TextZoomService(settings),
            theme, new AcceptColors());

        editor.View.AccentColor = ViewSettings.AccentColorKind.Red;
        Assert.AreEqual("Red", theme.AccentColor);
        Assert.AreEqual(ViewSettings.AccentColorKind.Blue, settings.ViewSettings.AccentColor);

        editor.Cancel();
        Assert.AreEqual("Blue", theme.AccentColor);
        Assert.AreEqual(ViewSettings.AccentColorKind.Blue, editor.View.AccentColor);
    }

    [TestMethod]
    public void PortableUpdateOpensReleasePageThroughDesktopService()
    {
        var service = new Updates { Installable = false };
        var desktop = new Desktop();
        var viewModel = new UpdateViewModel(service, Release, desktop);
        var closed = 0;
        viewModel.CloseRequested += () => closed++;

        viewModel.UpdateCommand.Execute(null);

        Assert.AreEqual(Release.PageUrl, desktop.Url);
        Assert.AreEqual(0, service.DownloadCount);
        Assert.AreEqual(1, closed);
    }

    [TestMethod]
    public void InstalledUpdateDownloadsOnceAndSkipsThroughInterface()
    {
        var service = new Updates();
        var viewModel = new UpdateViewModel(service, Release, new Desktop());
        viewModel.UpdateCommand.Execute(null);
        viewModel.UpdateCommand.Execute(null);

        Assert.IsTrue(viewModel.IsReady);
        Assert.IsFalse(viewModel.IsDownloading);
        Assert.AreEqual(100.0, viewModel.Progress);
        Assert.AreEqual(1, service.DownloadCount);

        viewModel.SkipCommand.Execute(null);
        Assert.AreEqual(Release.Tag, service.SkippedVersion);
    }

    [TestMethod]
    public async Task UpdateCancellationReleasesDownloadAndAllowsRetry()
    {
        var download = new TaskCompletionSource();
        var service = new Updates { Download = download.Task };
        var viewModel = new UpdateViewModel(service, Release, new Desktop());
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(viewModel.IsDownloading) && !viewModel.IsDownloading) stopped.TrySetResult();
        };
        viewModel.UpdateCommand.Execute(null);
        viewModel.UpdateCommand.Execute(null);
        Assert.AreEqual(1, service.DownloadCount);

        viewModel.CancelDownload();
        Assert.IsTrue(service.Token.IsCancellationRequested);
        download.SetCanceled(service.Token);
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsFalse(viewModel.IsReady);

        service.Download = Task.CompletedTask;
        viewModel.UpdateCommand.Execute(null);
        Assert.IsTrue(viewModel.IsReady);
        Assert.AreEqual(2, service.DownloadCount);
    }

    [TestMethod]
    public void SupportChecksUpdatesAndCopiesDiagnosticsWithoutWindow()
    {
        var service = new Updates();
        var desktop = new Desktop();
        var viewModel = new SupportViewModel(service, desktop);

        viewModel.CheckForUpdatesCommand.Execute(null);
        viewModel.CopyDiagnosticsCommand.Execute(null);

        Assert.AreEqual(1, service.CheckCount);
        Assert.IsTrue(viewModel.CanCheckForUpdates);
        StringAssert.Contains(viewModel.UpdateStatus, Release.Tag);
        Assert.AreEqual(viewModel.DiagnosticsText, desktop.Clipboard);
    }

    [TestMethod]
    public void SearchDebounceAndDisposalUseInjectedScheduler()
    {
        var dispatcher = new ManualDispatcher();
        var patterns = new List<string>();
        var viewModel = new SearchViewModel(pattern =>
        {
            patterns.Add(pattern.Pattern);
            return new SearchDocument { SearchPattern = pattern };
        }, new SearchAutocompleteCache(), new SavedSearchPatternStore(), dispatcher);

        viewModel.TextToSearch = "first";
        viewModel.TextToSearch = "second";
        Assert.AreEqual(0, patterns.Count);
        dispatcher.Tick();
        CollectionAssert.AreEqual(new[] { "second" }, patterns);

        viewModel.TextToSearch = "after close";
        viewModel.Dispose();
        dispatcher.Tick();
        CollectionAssert.AreEqual(new[] { "second" }, patterns);
        Assert.IsFalse(dispatcher.HasActiveTimers);
    }

    [TestMethod]
    public void SavedSearchFiltersAreIndependentAndUnsubscribeOnDisposal()
    {
        var store = new SavedSearchPatternStore();
        store.Items.Clear();
        using var first = new SearchViewModel(_ => new SearchDocument(), new SearchAutocompleteCache(), store, new ManualDispatcher());
        using var second = new SearchViewModel(_ => new SearchDocument(), new SearchAutocompleteCache(), store, new ManualDispatcher());
        first.SavedSearchFilter = "first";
        second.SavedSearchFilter = "second";
        store.Items.Add(SavedSearchPattern.FromSearchPattern("first", new SearchPattern("alpha", false, false)));
        store.Items.Add(SavedSearchPattern.FromSearchPattern("second", new SearchPattern("beta", false, false)));
        Assert.AreEqual(1, first.SavedSearches.Count);
        Assert.AreEqual("first", first.SavedSearches[0].Name);
        Assert.AreEqual(1, second.SavedSearches.Count);
        Assert.AreEqual("second", second.SavedSearches[0].Name);

        first.Dispose();
        store.Items.Add(SavedSearchPattern.FromSearchPattern("first again", new SearchPattern("gamma", false, false)));
        Assert.AreEqual(1, first.SavedSearches.Count);
        second.SavedSearchFilter = string.Empty;
        Assert.AreEqual(3, second.SavedSearches.Count);
    }

    [TestMethod]
    [DataRow(false, 10, 20, CrashDumpCommand.Configure, CrashDumpCommand.Disable)]
    [DataRow(false, 10, null, CrashDumpCommand.Configure, CrashDumpCommand.Configure)]
    [DataRow(true, 10, null, CrashDumpCommand.Configure, CrashDumpCommand.Enable)]
    [DataRow(true, 10, 20, CrashDumpCommand.Configure, CrashDumpCommand.Enable)]
    [DataRow(true, 10, 10, CrashDumpCommand.Configure, CrashDumpCommand.Configure)]
    [DataRow(true, 10, 20, CrashDumpCommand.Disable, CrashDumpCommand.Disable)]
    [DataRow(false, 10, null, CrashDumpCommand.Enable, CrashDumpCommand.Enable)]
    public void CrashDumpConfigurationRespectsDisabledSettingAndExplicitCommands(bool enabled, int maximum,
        int? current, CrashDumpCommand requested, CrashDumpCommand expected) =>
        Assert.AreEqual(expected, WindowsCrashDumpService.GetRequestedCommand(enabled, maximum, current, requested));

    private static IEnumerable<Type> ExpandType(Type type)
    {
        yield return type;
        if (type.HasElementType)
            foreach (var item in ExpandType(type.GetElementType()!)) yield return item;
        foreach (var argument in type.GetGenericArguments())
            foreach (var item in ExpandType(argument)) yield return item;
    }

    private sealed class Desktop : IDesktopService
    {
        public string? Url { get; private set; }
        public string? Clipboard { get; private set; }
        public void OpenUrl(string url) => Url = url;
        public void SetClipboardText(string text) => Clipboard = text;
        public void OpenPath(string path) => throw new InvalidOperationException();
        public void RevealFile(string path) => throw new InvalidOperationException();
    }

    private sealed class Dialogs : IAppDialogs
    {
        public int SettingsCount { get; private set; }
        public int SupportCount { get; private set; }
        public int FileSelectionCount { get; private set; }
        public IReadOnlyList<string> SelectLogFiles() { FileSelectionCount++; return []; }
        public void ShowSettings() => SettingsCount++;
        public void ShowSupport() => SupportCount++;
        public void ShowWarning(string message, string title) => throw new InvalidOperationException();
    }

    private sealed class Theme : IThemeService
    {
        public bool IsDark { get; private set; }
        public string Toggle() { IsDark = !IsDark; return IsDark ? "Dark" : "Light"; }
        public string? AccentColor { get; private set; }
        public void ApplyAccentColor(string accentColor) => AccentColor = accentColor;
    }

    private sealed class AcceptColors : IColorValidator
    {
        public bool IsValid(string value) => true;
    }

    private sealed class Updates : IUpdateService
    {
        public bool Installable { get; init; } = true;
        public Task Download { get; set; } = Task.CompletedTask;
        public int DownloadCount { get; private set; }
        public int CheckCount { get; private set; }
        public string? SkippedVersion { get; private set; }
        public CancellationToken Token { get; private set; }
        public bool CanInstall(ReleaseInfo release) => Installable;
        public Task<ReleaseInfo?> CheckNowAsync() { CheckCount++; return Task.FromResult<ReleaseInfo?>(Release); }
        public Task DownloadInstallerAsync(ReleaseInfo release, IProgress<double> progress, CancellationToken cancellationToken)
        {
            DownloadCount++;
            Token = cancellationToken;
            return Download;
        }
        public void SkipVersion(string tag) => SkippedVersion = tag;
    }

    private sealed class ManualDispatcher : IUiDispatcher
    {
        private readonly List<ManualTimer> _timers = new();
        public bool HasActiveTimers => _timers.Any(timer => timer.IsEnabled);
        public bool CheckAccess() => true;
        public IDisposable Post(Action action) => throw new InvalidOperationException();
        public IUiTimer CreateTimer(TimeSpan interval, Action tick, UiTimerPriority priority = UiTimerPriority.Normal)
        {
            var timer = new ManualTimer(interval, tick);
            _timers.Add(timer);
            return timer;
        }
        public void Tick()
        {
            foreach (var timer in _timers.ToArray())
                if (timer.IsEnabled) timer.Tick();
        }

        private sealed class ManualTimer(TimeSpan interval, Action tick) : IUiTimer
        {
            public TimeSpan Interval { get; set; } = interval;
            public bool IsEnabled { get; private set; }
            public void Start() => IsEnabled = true;
            public void Stop() => IsEnabled = false;
            public void Dispose() => Stop();
            public void Tick() => tick();
        }
    }

    private sealed class SearchDocument : ISearchDocument
    {
        public SearchPattern SearchPattern { get; set; } = SearchPattern.Empty;
        public string Title => string.Empty;
        public string MatchCounterText => string.Empty;
        public bool[] MatchBuckets => [];
        public int CurrentMatchLine => -1;
        public IReadOnlyList<int> SelectedMatchLines => [];
        public Action<int>? NavigateToIndexRequested { get; set; }
        public event PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
        public void FindNext() { }
        public void FindPrevious() { }
        public void FindNext(int anchorOriginalLine) { }
        public void FindPrevious(int anchorOriginalLine) { }
        public void Dispose() { }
    }
}
