using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
        private readonly Dictionary<DocumentViewModel, DocumentContainer> _containers = new();
        private bool _disposed;
        private ProfileSettings _selectedProfile;
        private bool _refreshingProfiles;

        public ObservableCollection<ProfileSettings> Profiles { get; }

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
            Profiles = new ObservableCollection<ProfileSettings>(applicationSettings.GetProfiles());
            _selectedProfile = Profiles.First(profile => string.Equals(profile.Name,
                applicationSettings.GetSelectedProfile().Name, StringComparison.OrdinalIgnoreCase));
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
            Documents.CollectionChanged += OnDocumentsChanged;
            MarkedLinesViewModel = markedLinesViewModelFactory(Documents);
            MergedViewModel = new MergedViewModel(Documents, _searchAutocompleteCache, _savedSearchPatternStore, _applicationSettings, _threadGroupingService) { IsActive = _mergedFilesViewService.IsEnabled };
            _applicationSettings.ProfilesChanged += OnProfilesChanged;
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

        public ProfileSettings SelectedProfile
        {
            get => _selectedProfile;
            set
            {
                if (value == null || _refreshingProfiles || ReferenceEquals(_selectedProfile, value))
                    return;
                if (TryApplyProfile(value))
                {
                    _selectedProfile = value;
                    _applicationSettings.SetSelectedProfile(value.Name);
                }
                InvokePropertyChanged();
            }
        }

        private void OnProfilesChanged()
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                _ = dispatcher.BeginInvoke(OnProfilesChanged);
                return;
            }
            if (_disposed)
                return;
            var profiles = _applicationSettings.GetProfiles();
            var selected = profiles.First(profile => string.Equals(profile.Name,
                _applicationSettings.GetSelectedProfile().Name, StringComparison.OrdinalIgnoreCase));
            if (!TryApplyProfile(selected))
                return;
            _refreshingProfiles = true;
            try
            {
                Profiles.Clear();
                foreach (var profile in profiles)
                    Profiles.Add(profile);
                _selectedProfile = selected;
                InvokePropertyChanged(nameof(SelectedProfile));
            }
            finally
            {
                _refreshingProfiles = false;
            }
        }

        private bool TryApplyProfile(ProfileSettings profile)
        {
            if (_selectedProfile.HasSameConfiguration(profile))
                return true;
            var documents = Documents.ToArray();
            var currentIndex = CurrentDocument == null ? -1 : Array.IndexOf(documents, CurrentDocument);
            var replacements = new List<(DocumentViewModel document, DocumentContainer container)>();
            Colors.ColorSettings colors;
            try
            {
                colors = new Colors.ColorSettings(profile.ColorSettings ?? new Colors.Configuration.ColorSettings());
                foreach (var document in documents)
                {
                    var replacement = CreateDocumentResources(document.DocumentId, profile);
                    replacements.Add(replacement);
                    foreach (var line in document.MarkedLines)
                        replacement.document.MarkedLines.Add(line);
                }
            }
            catch (Exception e)
            {
                foreach (var replacement in replacements)
                {
                    replacement.document.CloseFile();
                    replacement.container.Dispose();
                }
                Trace.TraceError($"Cannot apply profile '{profile.Name}': {e}");
                if (Application.Current != null)
                    MessageBox.Show($"Cannot apply profile '{profile.Name}': {e.Message}", "Profile change failed",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            for (var i = 0; i < replacements.Count; i++)
            {
                var replacement = replacements[i];
                _containers.Add(replacement.document, replacement.container);
                Documents[i] = replacement.document;
            }
            CurrentDocument = currentIndex >= 0 ? replacements[currentIndex].document : null;
            MergedViewModel.SetColorSettings(colors);
            return true;
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
            if (!File.Exists(fileName))
            {
                Trace.TraceError($"File {fileName} is not exists");
                return;
            }

            var alreadyOpen = FindOpenDocument(fileName);
            CurrentDocument = alreadyOpen ?? CreateDocument(fileName);
        }

        private DocumentViewModel? FindOpenDocument(string fileName)
        {
            var target = TryGetFullPath(fileName);
            foreach (var document in Documents)
            {
                if (string.Equals(TryGetFullPath(document.DocumentId), target,
                        StringComparison.OrdinalIgnoreCase))
                    return document;
            }

            return null;
        }

        private static string TryGetFullPath(string path)
        {
            try
            {
                return Path.GetFullPath(path);
            }
            catch (Exception)
            {
                return path;
            }
        }

        private DocumentViewModel CreateDocument(string fileName)
        {
            var (viewModel, container) = CreateDocumentResources(fileName, _selectedProfile);
            _containers.Add(viewModel, container);
            Documents.Add(viewModel);
            return viewModel;
        }

        private (DocumentViewModel document, DocumentContainer container) CreateDocumentResources(
            string fileName, ProfileSettings profile)
        {
            var container = new DocumentContainer(fileName, profile, _applicationSettings, _searchAutocompleteCache, _savedSearchPatternStore, _timelinePlacementService, _threadGroupingService);
            DocumentViewModel viewModel;
            try
            {
                viewModel = container.GetDocumentViewModel();
            }
            catch
            {
                container.Dispose();
                throw;
            }

            return (viewModel, container);
        }

        private void OnDocumentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                foreach (var (document, container) in _containers)
                {
                    document.CloseFile();
                    container.Dispose();
                }
                _containers.Clear();
                return;
            }

            if (e.OldItems == null)
                return;

            foreach (DocumentViewModel document in e.OldItems)
            {
                if (_containers.Remove(document, out var container))
                {
                    document.CloseFile();
                    container.Dispose();
                }
            }
        }

        public event EventHandler? ShowScratchPad;
        
        public object? GetContent(string contentId)
        {
            return contentId == Constants.MarkedLinesContentId ? MarkedLinesViewModel : null;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _applicationSettings.ProfilesChanged -= OnProfilesChanged;
            _timelinePlacementService.Changed -= OnTimelinePlacementChanged;
            _threadGroupingService.Changed -= OnThreadGroupingChanged;
            _mergedFilesViewService.Changed -= OnMergedFilesViewChanged;
            Documents.CollectionChanged -= OnDocumentsChanged;
            MarkedLinesViewModel.Dispose();
            MergedViewModel.Dispose();
            foreach (var (document, container) in _containers)
            {
                document.CloseFile();
                container.Dispose();
            }
            _containers.Clear();
            Documents.Clear();
        }
    }
}
