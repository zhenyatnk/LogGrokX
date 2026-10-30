using LogGrokX.Platform.Colors;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Input;
using LogGrokX.AvalonDockExtensions;
using LogGrokX.Data;
using LogGrokX.Localization;
using LogGrokX.MarkedLines;
using LogGrokX.MergedView;
using LogGrokX.Search;
using LogGrokX.Platform;

namespace LogGrokX
{
    public class MainWindowViewModel : ViewModelBase, IContentProvider, IDisposable
    {
        private DocumentViewModel? _currentDocument;
        private readonly ApplicationSettings _applicationSettings;
        private readonly SearchAutocompleteCache _searchAutocompleteCache;
        private readonly SavedSearchPatternStore _savedSearchPatternStore;
        private readonly IThemeService _themeService;
        private readonly IColorValidator _colorValidator;
        private readonly TimelinePlacementService _timelinePlacementService;
        private readonly TextZoomService _textZoomService;
        private readonly ThreadGroupingService _threadGroupingService;
        private readonly MergedFilesViewService _mergedFilesViewService;
        private readonly IAppDialogs _dialogs;
        private readonly Dictionary<DocumentViewModel, DocumentContainer> _containers = new();
        private readonly IUiDispatcher _dispatcher;
        private readonly IDesktopService _desktop;
        private readonly DisplaySettingsMonitor _displaySettings;
        private bool _disposed;
        private ProfileSettings _selectedProfile;
        private ProfileSettings _appliedProfile;
        private ProfileSettings? _lastFailedProfile;
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
            IThemeService themeService,
            TimelinePlacementService timelinePlacementService,
            TextZoomService textZoomService,
            ThreadGroupingService threadGroupingService,
            MergedFilesViewService mergedFilesViewService,
            IAppDialogs dialogs,
            Func<ObservableCollection<DocumentViewModel>, MarkedLinesViewModel> markedLinesViewModelFactory,
            IColorValidator colorValidator, IUiDispatcher dispatcher, IDesktopService desktop,
            DisplaySettingsMonitor displaySettings)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _desktop = desktop ?? throw new ArgumentNullException(nameof(desktop));
            _displaySettings = displaySettings ?? throw new ArgumentNullException(nameof(displaySettings));
            _applicationSettings = applicationSettings;
            Profiles = new ObservableCollection<ProfileSettings>(applicationSettings.GetProfiles());
            _selectedProfile = Profiles.First(profile => string.Equals(profile.Name,
                applicationSettings.GetSelectedProfile().Name, StringComparison.OrdinalIgnoreCase));
            _appliedProfile = _selectedProfile;
            _searchAutocompleteCache = searchAutocompleteCache;
            _savedSearchPatternStore = savedSearchPatternStore;
            _themeService = themeService;
            _colorValidator = colorValidator;
            _timelinePlacementService = timelinePlacementService;
            _textZoomService = textZoomService;
            _threadGroupingService = threadGroupingService;
            _mergedFilesViewService = mergedFilesViewService;
            _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
            _timelinePlacementService.Changed += OnTimelinePlacementChanged;
            _threadGroupingService.Changed += OnThreadGroupingChanged;
            _mergedFilesViewService.Changed += OnMergedFilesViewChanged;
            Documents = new ObservableCollection<DocumentViewModel>();
            Documents.CollectionChanged += OnDocumentsChanged;
            MarkedLinesViewModel = markedLinesViewModelFactory(Documents);
            MergedViewModel = new MergedViewModel(Documents, _searchAutocompleteCache, _savedSearchPatternStore, _applicationSettings, _threadGroupingService, _timelinePlacementService, _colorValidator, _dispatcher, _dialogs) { IsActive = _mergedFilesViewService.IsEnabled };
            _applicationSettings.ProfilesChanged += OnProfilesChanged;
            OpenSettings = new DelegateCommand(OpenSettingsWindow);
            OpenSupportCommand = new DelegateCommand(OpenSupport);
            ToggleThemeCommand = new DelegateCommand(ToggleTheme);
            CycleTimelineCommand = new DelegateCommand(_timelinePlacementService.CyclePlacement);
            ZoomInCommand = new DelegateCommand(() => _textZoomService.Increase());
            ZoomOutCommand = new DelegateCommand(() => _textZoomService.Decrease());
            ResetZoomCommand = new DelegateCommand(() => _textZoomService.Reset());
            ActivateSearchCommand = new DelegateCommand(() => ExecuteActiveSearchCommand(search => search.Activate, document => document.SearchViewModel.Activate));
            FindNextCommand = new DelegateCommand(() => ExecuteActiveSearchCommand(search => search.FindNextCommand, document => document.FindNextCommand));
            FindPreviousCommand = new DelegateCommand(() => ExecuteActiveSearchCommand(search => search.FindPreviousCommand, document => document.FindPreviousCommand));

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
            var dispatcher = _dispatcher;
            if (!dispatcher.CheckAccess())
            {
                _ = dispatcher.Post(OnProfilesChanged);
                return;
            }
            if (_disposed)
                return;
            var profiles = _applicationSettings.GetProfiles();
            var selected = profiles.First(profile => string.Equals(profile.Name,
                _applicationSettings.GetSelectedProfile().Name, StringComparison.OrdinalIgnoreCase));
            if (!TryApplyProfile(selected))
                selected = profiles.FirstOrDefault(profile => string.Equals(profile.Name,
                    _appliedProfile.Name, StringComparison.OrdinalIgnoreCase)) ?? _appliedProfile;
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
            if (_appliedProfile.HasSameConfiguration(profile))
            {
                _appliedProfile = profile;
                _lastFailedProfile = null;
                return true;
            }
            var documents = Documents.ToArray();
            var currentIndex = CurrentDocument == null ? -1 : Array.IndexOf(documents, CurrentDocument);
            var replacements = new List<(DocumentViewModel document, DocumentContainer container)>();
            Platform.Colors.ColorRuleSet colors;
            try
            {
                colors = new Platform.Colors.ColorRuleSet(profile.ColorSettings ?? new Platform.Colors.Configuration.ColorSettings(), _colorValidator);
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
                if (_lastFailedProfile == null || !_lastFailedProfile.HasSameConfiguration(profile))
                {
                    _lastFailedProfile = profile;
                    Trace.TraceError($"Cannot apply profile '{profile.Name}': {e}");
                    _dialogs.ShowWarning(Strings.Format("ProfileChangeError", profile.Name, e.Message), Strings.Get("ProfileChangeFailed"));
                }
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
            _appliedProfile = profile;
            _lastFailedProfile = null;
            return true;
        }

        public ICommand OpenSettings { get; }

        public ICommand OpenSupportCommand { get; }

        public ICommand ToggleThemeCommand { get; }

        public ICommand CycleTimelineCommand { get; }

        public ICommand ZoomInCommand { get; }

        public ICommand ZoomOutCommand { get; }

        public ICommand ResetZoomCommand { get; }

        public ICommand ActivateSearchCommand { get; }

        public ICommand FindNextCommand { get; }

        public ICommand FindPreviousCommand { get; }

        private void ExecuteActiveSearchCommand(Func<SearchViewModel, ICommand> mergedCommand,
            Func<DocumentViewModel, ICommand> documentCommand)
        {
            var command = IsMergedViewActive && IsMergedView
                ? mergedCommand(MergedViewModel.Search)
                : CurrentDocument is { } document ? documentCommand(document) : null;

            if (command?.CanExecute(null) == true)
                command.Execute(null);
        }

        public bool IsDarkTheme => _themeService.IsDark;

        public string WindowTitle => $"LogGrokX {BuildInfo.Version}";

        public bool IsTimelineAtTop
        {
            get => _timelinePlacementService.IsAtTop;
            set => _timelinePlacementService.SetAtTop(value);
        }

        public bool IsTimelineVisible => _timelinePlacementService.IsVisible;

        public string TimelineToolTip => Strings.Get(!IsTimelineVisible
            ? "TimelineOffHint"
            : IsTimelineAtTop ? "TimelineTopHint" : "TimelineBottomHint");

        private void OnTimelinePlacementChanged()
        {
            InvokePropertyChanged(nameof(IsTimelineAtTop));
            InvokePropertyChanged(nameof(IsTimelineVisible));
            InvokePropertyChanged(nameof(TimelineToolTip));
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

        private void OpenSupport() => _dialogs.ShowSupport();

        private void OpenSettingsWindow() => _dialogs.ShowSettings();

        private void ToggleTheme()
        {
            _themeService.Toggle();
            InvokePropertyChanged(nameof(IsDarkTheme));
        }

        private void OpenFile()
        {
            foreach (var fileName in _dialogs.SelectLogFiles())
            {
                Trace.TraceInformation($"Open document {fileName}.");
                AddDocument(fileName);
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
            try
            {
                CurrentDocument = alreadyOpen ?? CreateDocument(fileName);
            }
            catch (Exception e) when (e is FormatException or ArgumentException)
            {
                Trace.TraceError($"Cannot open '{fileName}': {e.Message}");
                _dialogs.ShowWarning(e.Message, Strings.Get("LogFormats"));
            }
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
            var (viewModel, container) = CreateDocumentResources(fileName, _appliedProfile);
            _containers.Add(viewModel, container);
            Documents.Add(viewModel);
            return viewModel;
        }

        private (DocumentViewModel document, DocumentContainer container) CreateDocumentResources(
            string fileName, ProfileSettings profile)
        {
            var container = new DocumentContainer(fileName, profile, _applicationSettings, _searchAutocompleteCache, _savedSearchPatternStore, _timelinePlacementService, _threadGroupingService, _dispatcher, _desktop, _dialogs, _colorValidator, _displaySettings);
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
