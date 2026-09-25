using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection.Metadata;
using System.Windows;
using System.Windows.Input;
using LogGrokX.AvalonDockExtensions;
using LogGrokX.Data;
using LogGrokX.MarkedLines;
using LogGrokX.MergedView;
using LogGrokX.Search;
using LogGrokX.Theming;
using Microsoft.Win32;

namespace LogGrokX
{
    public class MainWindowViewModel : ViewModelBase, IContentProvider, IDisposable
    {
        private DocumentViewModel? _currentDocument;
        private readonly ApplicationSettings _applicationSettings;
        private readonly SearchAutocompleteCache _searchAutocompleteCache;
        private readonly SavedSearchPatternStore _savedSearchPatternStore;
        private readonly UiThemeService _themeService;
        private readonly TimelinePlacementService _timelinePlacementService;
        private readonly TextZoomService _textZoomService;
        private readonly ThreadGroupingService _threadGroupingService;
        private readonly MergedFilesViewService _mergedFilesViewService;
        private readonly UpdateCheckService _updateCheckService;

        public ObservableCollection<DocumentViewModel> Documents { get; }

        public MarkedLinesViewModel MarkedLinesViewModel { get; }

        public MergedViewModel MergedViewModel { get; }
        
        public ICommand OpenFileCommand => new DelegateCommand(OpenFile);

        public ICommand OnDocumentCloseCommand => DelegateCommand.Create((DocumentViewModel document) =>
        {
            document.CloseFile();
        });

        public ICommand DropCommand => new DelegateCommand(
            obj=> OpenFiles((IEnumerable<string>)obj), 
            o => o is IEnumerable<string>);

        public MainWindowViewModel(ApplicationSettings applicationSettings, 
            SearchAutocompleteCache searchAutocompleteCache, 
            SavedSearchPatternStore savedSearchPatternStore,
            UiThemeService themeService,
            TimelinePlacementService timelinePlacementService,
            TextZoomService textZoomService,
            ThreadGroupingService threadGroupingService,
            MergedFilesViewService mergedFilesViewService,
            UpdateCheckService updateCheckService,
            Func<ObservableCollection<DocumentViewModel>, MarkedLinesViewModel> markedLinesViewModelFactory)
        {
            _applicationSettings = applicationSettings;
            _searchAutocompleteCache = searchAutocompleteCache;
            _savedSearchPatternStore = savedSearchPatternStore;
            _themeService = themeService;
            _timelinePlacementService = timelinePlacementService;
            _textZoomService = textZoomService;
            _threadGroupingService = threadGroupingService;
            _mergedFilesViewService = mergedFilesViewService;
            _updateCheckService = updateCheckService;
            _timelinePlacementService.Changed += OnTimelinePlacementChanged;
            _threadGroupingService.Changed += OnThreadGroupingChanged;
            _mergedFilesViewService.Changed += OnMergedFilesViewChanged;
            Documents = new ObservableCollection<DocumentViewModel>();
            MarkedLinesViewModel = markedLinesViewModelFactory(Documents);
            MergedViewModel = new MergedViewModel(Documents, _searchAutocompleteCache, _savedSearchPatternStore, _applicationSettings, _threadGroupingService) { IsActive = _mergedFilesViewService.IsEnabled };
            OpenSettings = new DelegateCommand(OpenSettingsWindow);
            OpenSupportCommand = new DelegateCommand(OpenSupport);
            ToggleThemeCommand = new DelegateCommand(ToggleTheme);
            ZoomInCommand = new DelegateCommand(() => _textZoomService.Increase());
            ZoomOutCommand = new DelegateCommand(() => _textZoomService.Decrease());
            ResetZoomCommand = new DelegateCommand(() => _textZoomService.Reset());

            MarkedLinesViewModel.NavigationRequested += (document, index) =>
            {
                if (IsMergedViewActive && MergedViewModel.TryNavigateToDocumentLine(document, index))
                    return;

                CurrentDocument = document;
                document.NavigateTo(index);
            };
        }

        public DocumentViewModel? CurrentDocument
        {
            get => _currentDocument;
            set
            {
                if (_currentDocument == value) return;
                
                if (_currentDocument != null)
                    _currentDocument.IsCurrentDocument = false;
                
                _currentDocument = value;
                
                if (_currentDocument != null)
                    _currentDocument.IsCurrentDocument = true;
                
                InvokePropertyChanged();
            }
        }

        public ICommand OpenSettings { get; }

        public ICommand OpenSupportCommand { get; }

        public ICommand ToggleThemeCommand { get; }

        public ICommand ZoomInCommand { get; }

        public ICommand ZoomOutCommand { get; }

        public ICommand ResetZoomCommand { get; }

        public bool IsDarkTheme => _themeService.IsDark;

        public string WindowTitle => $"LogGrokX {BuildInfo.Version}";

        public bool IsTimelineAtTop
        {
            get => _timelinePlacementService.IsAtTop;
            set => _timelinePlacementService.SetAtTop(value);
        }

        private void OnTimelinePlacementChanged()
        {
            InvokePropertyChanged(nameof(IsTimelineAtTop));
        }

        public bool IsGroupByThread
        {
            get => _threadGroupingService.IsEnabled;
            set => _threadGroupingService.SetEnabled(value);
        }

        private void OnThreadGroupingChanged()
        {
            InvokePropertyChanged(nameof(IsGroupByThread));
        }

        public bool IsMergedView
        {
            get => _mergedFilesViewService.IsEnabled;
            set => _mergedFilesViewService.SetEnabled(value);
        }

        public bool IsMergedViewActive { get; set; }

        private void OnMergedFilesViewChanged()
        {
            MergedViewModel.IsActive = _mergedFilesViewService.IsEnabled;
            InvokePropertyChanged(nameof(IsMergedView));
        }

        private static readonly AvalonDock.Themes.Vs2013LightTheme LightDockTheme = new();
        private static readonly AvalonDock.Themes.Vs2013DarkTheme DarkDockTheme = new();

        public AvalonDock.Themes.Theme DockTheme => _themeService.IsDark ? DarkDockTheme : LightDockTheme;

        private void OpenSupport()
        {
            var window = new SupportWindow(_updateCheckService);
            if (Application.Current?.MainWindow is { } owner)
                window.Owner = owner;
            window.ShowDialog();
        }

        private void OpenSettingsWindow()
        {
            var viewModel = new LogGrokX.Settings.SettingsViewModel(
                _applicationSettings,
                _timelinePlacementService,
                _threadGroupingService,
                _mergedFilesViewService,
                _textZoomService);
            var window = new LogGrokX.Settings.SettingsWindow(viewModel);
            if (Application.Current?.MainWindow is { } owner)
                window.Owner = owner;
            window.ShowDialog();
        }

        private void ToggleTheme()
        {
            _themeService.Toggle();
            InvokePropertyChanged(nameof(IsDarkTheme));
            InvokePropertyChanged(nameof(DockTheme));
        }

        private void OpenFile()
        {
            var dialog = new OpenFileDialog
            {
                DefaultExt = "log",
                Filter = "All Files|*.*|Log files(*.log)|*.log|Text files(*.txt)|*.txt",
                Multiselect = true
            };

            var dialogResult = dialog.ShowDialog();
            if (dialogResult.GetValueOrDefault())
            {
                foreach (var fileName in dialog.FileNames)
                {
                    Trace.TraceInformation($"Open document {fileName}.");
                    AddDocument(fileName);
                }
            }
            
            ShowScratchPad?.Invoke(this, new EventArgs());
        }
        
        private void OpenFiles(IEnumerable<string> files)
        {
            foreach (var file in files)
            {
                AddDocument(file);
            }
        }

        public void AddDocument(string fileName)
        {
            if (File.Exists(fileName))
                CurrentDocument = CreateDocument(fileName);
            else
                Trace.TraceError($"File {fileName} is not exists");
        }

        private DocumentViewModel CreateDocument(string fileName)
        {
            var container = new DocumentContainer(fileName, _applicationSettings, _searchAutocompleteCache, _savedSearchPatternStore, _timelinePlacementService, _threadGroupingService);
            var viewModel = container.GetDocumentViewModel();
            Documents.Add(viewModel);
            Documents.CollectionChanged += (o, e) =>
            {
                if (Documents.Contains(viewModel))
                    return;
                container.Dispose();
            };
            return viewModel;
        }

        public event EventHandler? ShowScratchPad;
        
        public object? GetContent(string contentId)
        {
            return contentId == Constants.MarkedLinesContentId ? MarkedLinesViewModel : null;
        }

        public void Dispose()
        {
            Documents.Clear();
        }
    }
}
