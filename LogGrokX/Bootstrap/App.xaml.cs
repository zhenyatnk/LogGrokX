using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using DryIoc;
using LogGrokX.Data;
using LogGrokX.Diagnostics;
using LogGrokX.MarkedLines;
using LogGrokX.Search;
using LogGrokX.Theming;
using Splat.DryIoc;

namespace LogGrokX.Bootstrap
{
    public partial class App
    {
        private readonly Container _container;
        public App()
        {
#if DEBUG
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
#endif
            TracesLogger.Initialize();
            ExceptionsLogger.Initialize();
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            _container = new Container();
            LoggerRegistrationHelper.Register(_container);

            Trace.TraceInformation($"Git info: {ThisAssembly.Git.Branch}:{ThisAssembly.Git.Commit}");

            _container.UseDryIocDependencyResolver();
            RegisterDependencies(_container);
            Trace.TraceInformation("Initialization complete.");
            
            InitializeComponent();
        }

        public void OnNextInstanceStared(IEnumerable<string> commandLine)
        {
            ProcessCommandLine(commandLine);

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (MainWindow == null) throw new InvalidOperationException();
                if (MainWindow.WindowState == WindowState.Minimized)
                {
                    MainWindow.WindowState = WindowState.Normal;
                }

                MainWindow.Activate();
                MainWindow.Topmost = true;
                MainWindow.Topmost = false;
                MainWindow.Focus(); 
            }));
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            PerfProbe.Start();

            _container.Resolve<UiThemeService>().ApplySavedTheme();

            var mainWindow = _container.Resolve<MainWindow>();
            mainWindow.Show();
            ShowWhatsNewIfAny(mainWindow);
            _container.Resolve<UpdateCheckService>().CheckOnStartup(mainWindow);
            
            ProcessCommandLine(e.Args.Where(item => item != null));
        }

        private static void ShowWhatsNewIfAny(Window owner)
        {
            var pending = UpdateCheckService.TakePendingReleaseForCurrentVersion();
            if (pending == null)
                return;

            var window = new WhatsNewWindow(new WhatsNewViewModel(pending.Version, pending.Notes, pending.PageUrl));
            if (owner.IsVisible)
                window.Owner = owner;
            window.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _container.Resolve<UpdateCheckService>().LaunchPendingInstaller();
            _container.Resolve<SearchAutocompleteCache>().Save();
            _container.Resolve<SavedSearchPatternStore>().Save();
            _container.Dispose();
        }

        private void RegisterDependencies(IRegistrator container)
        {
            container.RegisterDelegate(ApplicationSettings.Instance);
            container.Register<MainWindowViewModel>(Reuse.Singleton);
            container.Register<SearchAutocompleteCache>(Reuse.Singleton); 
            container.Register<SavedSearchPatternStore>(Reuse.Singleton);
            container.Register<UiThemeService>(Reuse.Singleton);
            container.Register<TimelinePlacementService>(Reuse.Singleton);
            container.Register<TextZoomService>(Reuse.Singleton);
            container.Register<ThreadGroupingService>(Reuse.Singleton);
            container.Register<MergedFilesViewService>(Reuse.Singleton);
            container.Register<UpdateCheckService>(Reuse.Singleton);
            container.Register<MarkedLinesViewModel>();
            container.Register<MainWindow>();
        }

        private void ProcessCommandLine(IEnumerable<string> commandLine)
        {            
            var mainVm = _container.Resolve<MainWindowViewModel>();
            foreach (var item in commandLine)
            {
                Dispatcher.BeginInvoke(new Action(() => { mainVm.AddDocument(item); }));
            }
        }
    }
}
