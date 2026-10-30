using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using LogGrokX.Platform;
using System.Windows.Input;
using LogGrokX.Controls;

namespace LogGrokX.Search
{
    // ReSharper disable once ClassNeverInstantiated.Global
    public class SearchViewModel : ViewModelBase, IDataErrorInfo, IDisposable
    {
        private readonly Func<SearchPattern, ISearchDocument> _searchDocumentViewModelFactory;
        private string _textToSearch = string.Empty;
        private  bool _isCaseSensitive = SearchPattern.Empty.IsCaseSensitive;
        private bool _useRegex = SearchPattern.Empty.UseRegex;

        private IUiTimer? _searchPatternThrottleTimer;
        private IUiTimer? _autocompletionThrottleTimer;
        
        private readonly TimeSpan _searchPatternCommitThrottleInterval = TimeSpan.FromMilliseconds(500);
        private readonly TimeSpan _autoCompletionThrottleInterval = TimeSpan.FromSeconds(2);

        private SearchPattern _searchPattern = SearchPattern.Empty;
            
        private ISearchDocument? _currentDocument;
        private Regex? _highlightRegex;
        private readonly SearchAutocompleteCache _searchAutocompleteCache;
        private readonly SavedSearchPatternStore _savedSearchPatternStore;
        private readonly IAppDialogs _dialogs;
        private string _savedSearchFilter = string.Empty;
        private readonly IUiDispatcher _dispatcher;
        private bool _disposed;
        private string _newSearchName = string.Empty;
        private bool _isSavedSearchesOpen;
        private bool _isSaveSearchEditorOpen;
        private SavedSearchPattern? _pendingReplacement;

        public SearchViewModel(Func<SearchPattern, ISearchDocument> searchDocumentViewModelFactory,
            SearchAutocompleteCache searchAutocompleteCache,
            SavedSearchPatternStore savedSearchPatternStore, IUiDispatcher dispatcher, IAppDialogs dialogs)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
            _searchDocumentViewModelFactory =
                pattern =>
                {
                    var newDocument = searchDocumentViewModelFactory(pattern);
                    newDocument.NavigateToIndexRequested += i => CurrentLineChanged?.Invoke(i);
                    return newDocument;
                };
                
            ClearSearchCommand = new DelegateCommand(ClearSearch);
            CloseDocumentCommand = DelegateCommand.Create<ISearchDocument>(CloseDocument);
            AddNewSearchCommand = new DelegateCommand(() => AddNewSearch(_searchPattern.Clone()));
            FindNextCommand = new DelegateCommand(() => CurrentDocument?.FindNext());
            FindPreviousCommand = new DelegateCommand(() => CurrentDocument?.FindPrevious());
            SearchTextCommand = new DelegateCommand(SearchText, text => !string.IsNullOrEmpty(text as string));
            Activate = new DelegateCommand(() => SetFocusRequest.Invoke());
            SaveSearchCommand = new DelegateCommand(SaveCurrentSearch);
            ShowSaveSearchEditorCommand = new DelegateCommand(ShowSaveSearchEditor);
            CancelSaveSearchEditorCommand = new DelegateCommand(ResetSaveSearchEditor);
            ManageSavedSearchesCommand = new DelegateCommand(ManageSavedSearches);
            CloseSavedSearchesCommand = new DelegateCommand(() => IsSavedSearchesOpen = false);
            ConfirmReplaceSearchCommand = new DelegateCommand(CommitSaveCurrentSearch);
            CancelReplaceSearchCommand = new DelegateCommand(CancelReplaceSearch);
            ApplySavedSearchCommand = DelegateCommand.Create<SavedSearchPattern>(ApplySavedSearch);
            BeginEditSavedSearchCommand = DelegateCommand.Create<SavedSearchPattern>(BeginEditSavedSearch);
            CommitEditSavedSearchCommand = DelegateCommand.Create<SavedSearchPattern>(CommitEditSavedSearch);
            CancelEditSavedSearchCommand = DelegateCommand.Create<SavedSearchPattern>(CancelEditSavedSearch);
            DeleteSavedSearchCommand = DelegateCommand.Create<SavedSearchPattern>(DeleteSavedSearch);

            Documents = new ObservableCollection<ISearchDocument>();
            _searchAutocompleteCache = searchAutocompleteCache;
            _savedSearchPatternStore = savedSearchPatternStore;
        }
        public event Action<int>? CurrentLineChanged;

        public event Action<Regex>? CurrentSearchChanged;

        public ISearchDocument? CurrentDocument
        {
            get => _currentDocument;
            set
            {
                if (_currentDocument == value) return;

                if (_currentDocument != null)
                    _currentDocument.PropertyChanged -= OnCurrentDocumentPropertyChanged;

                SetAndRaiseIfChanged(ref _currentDocument,  value);

                if (_currentDocument != null)
                    _currentDocument.PropertyChanged += OnCurrentDocumentPropertyChanged;

                if (_currentDocument == null)
                {
                    TextToSearch = string.Empty;
                }
                else
                {
                    var searchPattern = _currentDocument.SearchPattern;
                    TextToSearch = searchPattern.Pattern;
                    IsCaseSensitive = searchPattern.IsCaseSensitive;
                    UseRegex = searchPattern.UseRegex;
                }

                InvokePropertyChanged(nameof(MatchCounterText));
                InvokePropertyChanged(nameof(CurrentMatchBuckets));
                InvokePropertyChanged(nameof(CurrentMatchLine));
                InvokePropertyChanged(nameof(CurrentSelectedMatchLines));
            }
        }

        private void OnCurrentDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ISearchDocument.MatchCounterText))
                InvokePropertyChanged(nameof(MatchCounterText));

            if (e.PropertyName == nameof(ISearchDocument.MatchBuckets))
                InvokePropertyChanged(nameof(CurrentMatchBuckets));

            if (e.PropertyName == nameof(ISearchDocument.CurrentMatchLine))
                InvokePropertyChanged(nameof(CurrentMatchLine));

            if (e.PropertyName == nameof(ISearchDocument.SelectedMatchLines))
                InvokePropertyChanged(nameof(CurrentSelectedMatchLines));
        }

        public string MatchCounterText => CurrentDocument?.MatchCounterText ?? string.Empty;

        public bool[] CurrentMatchBuckets => CurrentDocument?.MatchBuckets ?? Array.Empty<bool>();

        public int CurrentMatchLine => CurrentDocument?.CurrentMatchLine ?? -1;

        public IReadOnlyList<int> CurrentSelectedMatchLines =>
            CurrentDocument?.SelectedMatchLines ?? Array.Empty<int>();

        public Regex? HighlightRegex
        {
            get => _highlightRegex;
            private set => SetAndRaiseIfChanged(ref _highlightRegex, value);
        }

        public ICommand FindNextCommand { get; }
        public ICommand FindPreviousCommand { get; }

        public void FindNext(int anchorOriginalLine) => CurrentDocument?.FindNext(anchorOriginalLine);

        public void FindPrevious(int anchorOriginalLine) => CurrentDocument?.FindPrevious(anchorOriginalLine);

        public SetFocusRequest SetFocusRequest { get; } = new(); 

        public string TextToSearch
        {
            get => _textToSearch;
            set => CommitSearchPattern(ref _textToSearch, value, _searchPatternCommitThrottleInterval);
        }

        public bool IsCaseSensitive
        {
            get => _isCaseSensitive;
            set => CommitSearchPattern(ref _isCaseSensitive, value, TimeSpan.Zero);
        }

        public bool UseRegex
        {
            get => _useRegex;
            set
            {
                CommitSearchPattern(ref _useRegex, value, TimeSpan.Zero);
                InvokePropertyChanged(nameof(TextToSearch));
            }
        }

        public bool IsFilterEnabled => !_searchPattern.IsEmpty;

        public bool IsFilterDisabled => _searchPattern.IsEmpty;
        
        public ObservableCollection<ISearchDocument> Documents { get; }

        public string Error => string.Empty;

        public IEnumerable<string> AutoCompleteList => _searchAutocompleteCache.Items;

        public ObservableCollection<SavedSearchPattern> SavedSearches => _savedSearchPatternStore.Items;

        public string SavedSearchFilter
        {
            get => _savedSearchFilter;
            set
            {
                if (_savedSearchFilter == value) return;
                _savedSearchFilter = value;
                InvokePropertyChanged();
            }
        }

        public SetFocusRequest SavedSearchFilterFocusRequest { get; } = new();

        public bool IsSavedSearchesOpen
        {
            get => _isSavedSearchesOpen;
            set
            {
                if (_isSavedSearchesOpen == value) return;
                _isSavedSearchesOpen = value;
                InvokePropertyChanged();
                if (value)
                    SavedSearchFilterFocusRequest.Invoke();
                else
                    ResetSaveSearchEditor();
            }
        }

        public bool IsSaveSearchEditorOpen
        {
            get => _isSaveSearchEditorOpen;
            private set => SetAndRaiseIfChanged(ref _isSaveSearchEditorOpen, value);
        }

        public bool IsReplaceConfirmationVisible => _pendingReplacement != null;

        public bool CanSaveCurrentSearch => !_searchPattern.IsEmpty && _searchPattern.IsValid;

        public string CurrentSearchPattern => _searchPattern.Pattern;

        public string NewSearchName
        {
            get => _newSearchName;
            set => SetAndRaiseIfChanged(ref _newSearchName, value);
        }

        public string this[string columnName] =>
            columnName switch
            {
                nameof(TextToSearch) =>
                    new SearchPattern(TextToSearch, IsCaseSensitive, UseRegex).RegexParseError,
                _ => string.Empty
            };

        public ICommand Activate { get; }
        
        public ICommand SearchTextCommand { get; set; }

        public ICommand ClearSearchCommand { get; }

        public ICommand CloseDocumentCommand { get; }

        public ICommand AddNewSearchCommand { get; }

        public ICommand SaveSearchCommand { get; }

        public ICommand ShowSaveSearchEditorCommand { get; }

        public ICommand CancelSaveSearchEditorCommand { get; }

        public ICommand ManageSavedSearchesCommand { get; }

        public ICommand CloseSavedSearchesCommand { get; }

        public ICommand ConfirmReplaceSearchCommand { get; }

        public ICommand CancelReplaceSearchCommand { get; }

        public ICommand ApplySavedSearchCommand { get; }

        public ICommand BeginEditSavedSearchCommand { get; }

        public ICommand CommitEditSavedSearchCommand { get; }

        public ICommand CancelEditSavedSearchCommand { get; }

        public ICommand DeleteSavedSearchCommand { get; }

        private void ShowSaveSearchEditor()
        {
            if (!CanSaveCurrentSearch) return;
            _pendingReplacement = null;
            NewSearchName = string.Empty;
            IsSaveSearchEditorOpen = true;
            InvokePropertyChanged(nameof(IsReplaceConfirmationVisible));
        }

        private void ResetSaveSearchEditor()
        {
            _pendingReplacement = null;
            NewSearchName = string.Empty;
            IsSaveSearchEditorOpen = false;
            InvokePropertyChanged(nameof(IsReplaceConfirmationVisible));
        }

        private void ManageSavedSearches()
        {
            IsSavedSearchesOpen = false;
            _dialogs.ShowSavedSearches();
        }

        private void CancelReplaceSearch()
        {
            _pendingReplacement = null;
            InvokePropertyChanged(nameof(IsReplaceConfirmationVisible));
        }

        private void SaveCurrentSearch()
        {
            if (!CanSaveCurrentSearch) return;

            var name = string.IsNullOrWhiteSpace(NewSearchName)
                ? _searchPattern.Pattern
                : NewSearchName.Trim();
            var existing = SavedSearches.FirstOrDefault(
                item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                _pendingReplacement = existing;
                InvokePropertyChanged(nameof(IsReplaceConfirmationVisible));
                return;
            }

            CommitSaveCurrentSearch();
        }

        private void CommitSaveCurrentSearch()
        {
            if (!CanSaveCurrentSearch) return;
            var name = string.IsNullOrWhiteSpace(NewSearchName)
                ? _searchPattern.Pattern
                : NewSearchName.Trim();
            _savedSearchPatternStore.AddOrUpdate(name, _searchPattern);
            ResetSaveSearchEditor();
        }

        private void ApplySavedSearch(SavedSearchPattern savedSearchPattern)
        {
            var searchPattern = savedSearchPattern.ToSearchPattern();
            if (!searchPattern.IsValid) return;

            _textToSearch = searchPattern.Pattern;
            _isCaseSensitive = searchPattern.IsCaseSensitive;
            _useRegex = searchPattern.UseRegex;

            InvokePropertyChanged(nameof(TextToSearch));
            InvokePropertyChanged(nameof(IsCaseSensitive));
            InvokePropertyChanged(nameof(UseRegex));

            CommitSearchPatternImmediately(_textToSearch, _isCaseSensitive, _useRegex);
            IsSavedSearchesOpen = false;
        }

        private void BeginEditSavedSearch(SavedSearchPattern savedSearchPattern) =>
            savedSearchPattern.BeginEdit();

        private void CommitEditSavedSearch(SavedSearchPattern savedSearchPattern)
        {
            var editedPattern = new SearchPattern(savedSearchPattern.EditPattern,
                savedSearchPattern.IsCaseSensitive, savedSearchPattern.UseRegex);
            if (!editedPattern.IsValid) return;

            savedSearchPattern.CommitEdit();
            _savedSearchPatternStore.Save();
        }

        private void CancelEditSavedSearch(SavedSearchPattern savedSearchPattern) =>
            savedSearchPattern.CancelEdit();

        private void DeleteSavedSearch(SavedSearchPattern savedSearchPattern) =>
            _savedSearchPatternStore.Remove(savedSearchPattern);

        private void CommitSearchPattern<T>(ref T field, T newValue, TimeSpan timeSpan, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, newValue)) return;
            SetAndRaiseIfChanged(ref field, newValue, propertyName);
            Throttle(ref _searchPatternThrottleTimer, 
                () =>
                {
                    CommitSearchPatternImmediately(TextToSearch, IsCaseSensitive, UseRegex);
                    Throttle(ref _autocompletionThrottleTimer, () => _searchAutocompleteCache.Add(TextToSearch),
                        _autoCompletionThrottleInterval);
                },
                timeSpan);
        }

        private void Throttle(ref IUiTimer? throttleTimer, Action action, TimeSpan interval)
        {
            throttleTimer?.Dispose();
            IUiTimer? timer = null;
            timer = _dispatcher.CreateTimer(interval, () =>
            {
                timer!.Dispose();
                if (!_disposed) action();
            });
            throttleTimer = timer;
            timer.Start();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _searchPatternThrottleTimer?.Dispose();
            _autocompletionThrottleTimer?.Dispose();
            if (_currentDocument != null) _currentDocument.PropertyChanged -= OnCurrentDocumentPropertyChanged;
            foreach (var document in Documents.ToArray()) document.Dispose();
        }

        private void CommitSearchPatternImmediately(string searchText, in bool isCaseSensitive, in bool useRegex)
        {
            var newSearchPattern = new SearchPattern(searchText, isCaseSensitive, useRegex);
            if (!newSearchPattern.IsValid)
                return;
            
            _searchPattern = newSearchPattern;
            var regex = _searchPattern.GetRegex(RegexOptions.None);
            HighlightRegex = _searchPattern.IsEmpty ? null : regex;
            CurrentSearchChanged?.Invoke(regex);

            InvokePropertyChanged(nameof(IsFilterEnabled));
            InvokePropertyChanged(nameof(IsFilterDisabled));
            InvokePropertyChanged(nameof(CanSaveCurrentSearch));
            InvokePropertyChanged(nameof(CurrentSearchPattern));
            
            if (_searchPattern.IsEmpty)
            {
                if (CurrentDocument != null)
                {
                    CurrentDocument.Dispose();
                    Documents.Remove(CurrentDocument);
                    CurrentDocument = null;
                }
                
                return;
            }

            if (CurrentDocument != null)
            {
                CurrentDocument.SearchPattern = _searchPattern;
            }
            else
            {
                var newDocument = _searchDocumentViewModelFactory(_searchPattern);
                Documents.Add(newDocument);
                CurrentDocument = newDocument;
            }
        }

        private void ClearSearch()
        {
            TextToSearch = string.Empty;
            CommitSearchPatternImmediately(TextToSearch, IsCaseSensitive, UseRegex);
        }

        private void CloseDocument(ISearchDocument searchDocumentViewModel)
        {
            searchDocumentViewModel.Dispose();
            if (Documents.Count != 0) return;
            CommitSearchPatternImmediately(string.Empty, IsCaseSensitive, UseRegex);
            TextToSearch = string.Empty;
            CurrentDocument = null;
        }

        private void AddNewSearch(SearchPattern searchPattern)
        {
            var newDocument = _searchDocumentViewModelFactory(searchPattern);
            Documents.Add(newDocument);
            CurrentDocument = newDocument;
        }
        
        private void SearchText(object obj)
        {
            if (obj is not string text) return;
            var searchPattern = new SearchPattern(text, false, false);
            AddNewSearch(searchPattern);
        }
    }
}