using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows.Input;
using LogGrokX.Localization;

namespace LogGrokX.Search;

public sealed class SavedSearchesViewModel : ViewModelBase, IDisposable
{
    private readonly SavedSearchPatternStore _store;
    private SavedSearchPattern? _selectedSearch;
    private SavedSearchPattern? _deletedSearch;
    private SavedSearchPattern? _pendingReplacement;
    private int _deletedSearchIndex = -1;
    private string _savedSearchFilter = string.Empty;
    private string _editName = string.Empty;
    private string _editPattern = string.Empty;
    private bool _editIsCaseSensitive;
    private bool _editUseRegex;
    private bool _disposed;

    public SavedSearchesViewModel(SavedSearchPatternStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        SavedSearches = _store.Items;
        SavedSearches.CollectionChanged += OnSavedSearchesChanged;
        SaveCommand = new DelegateCommand(Save);
        CancelEditCommand = new DelegateCommand(CancelEdit);
        DuplicateCommand = new DelegateCommand(Duplicate);
        DeleteCommand = new DelegateCommand(Delete);
        UndoDeleteCommand = new DelegateCommand(UndoDelete);
        ConfirmReplaceCommand = new DelegateCommand(ConfirmReplace);
        CancelReplaceCommand = new DelegateCommand(CancelReplace);
        SelectedSearch = SavedSearches.FirstOrDefault();
    }

    public ObservableCollection<SavedSearchPattern> SavedSearches { get; }

    public string SavedSearchFilter
    {
        get => _savedSearchFilter;
        set => SetAndRaiseIfChanged(ref _savedSearchFilter, value);
    }

    public SavedSearchPattern? SelectedSearch
    {
        get => _selectedSearch;
        set
        {
            if (ReferenceEquals(_selectedSearch, value)) return;
            _selectedSearch = value;
            _pendingReplacement = null;
            LoadEditor();
            InvokePropertyChanged();
            InvokePropertyChanged(nameof(HasSelectedSearch));
            InvokePropertyChanged(nameof(HasNoSelectedSearch));
            InvokePropertyChanged(nameof(IsReplaceConfirmationVisible));
        }
    }

    public bool HasSelectedSearch => SelectedSearch != null;

    public bool HasNoSelectedSearch => SelectedSearch == null;

    public string EditName
    {
        get => _editName;
        set
        {
            if (_editName == value) return;
            _editName = value;
            InvokePropertyChanged();
            RaiseEditorState();
        }
    }

    public string EditPattern
    {
        get => _editPattern;
        set
        {
            if (_editPattern == value) return;
            _editPattern = value;
            InvokePropertyChanged();
            RaiseEditorState();
        }
    }

    public bool EditIsCaseSensitive
    {
        get => _editIsCaseSensitive;
        set
        {
            if (_editIsCaseSensitive == value) return;
            _editIsCaseSensitive = value;
            InvokePropertyChanged();
            RaiseEditorState();
        }
    }

    public bool EditUseRegex
    {
        get => _editUseRegex;
        set
        {
            if (_editUseRegex == value) return;
            _editUseRegex = value;
            InvokePropertyChanged();
            RaiseEditorState();
        }
    }

    public bool HasChanges => SelectedSearch is { } selected &&
        (EditName != selected.Name || EditPattern != selected.Pattern ||
         EditIsCaseSensitive != selected.IsCaseSensitive || EditUseRegex != selected.UseRegex);

    public bool HasNoChanges => !HasChanges;

    public string ValidationMessage
    {
        get
        {
            if (string.IsNullOrEmpty(EditPattern))
                return Strings.Get("SavedSearchEmptyPattern");
            return new SearchPattern(EditPattern, EditIsCaseSensitive, EditUseRegex).RegexParseError;
        }
    }

    public bool HasValidationMessage => !string.IsNullOrEmpty(ValidationMessage);

    public bool CanSave => HasChanges && !HasValidationMessage && !IsReplaceConfirmationVisible;

    public bool IsReplaceConfirmationVisible => _pendingReplacement != null;

    public bool CanUndoDelete => _deletedSearch != null;

    public string DeletedSearchName => _deletedSearch?.Name ?? string.Empty;

    public ICommand SaveCommand { get; }

    public ICommand CancelEditCommand { get; }

    public ICommand DuplicateCommand { get; }

    public ICommand DeleteCommand { get; }

    public ICommand UndoDeleteCommand { get; }

    public ICommand ConfirmReplaceCommand { get; }

    public ICommand CancelReplaceCommand { get; }

    private void Save()
    {
        if (!CanSave || SelectedSearch == null) return;
        var name = GetEditorName();
        var existing = FindByName(name);
        if (existing != null && !ReferenceEquals(existing, SelectedSearch))
        {
            _pendingReplacement = existing;
            RaiseEditorState();
            InvokePropertyChanged(nameof(IsReplaceConfirmationVisible));
            return;
        }

        CommitSave();
    }

    private void ConfirmReplace()
    {
        if (_pendingReplacement == null || SelectedSearch == null) return;
        _store.Remove(_pendingReplacement);
        CommitSave();
    }

    private void CancelReplace()
    {
        _pendingReplacement = null;
        RaiseEditorState();
        InvokePropertyChanged(nameof(IsReplaceConfirmationVisible));
    }

    private void CommitSave()
    {
        if (SelectedSearch == null) return;
        _store.Update(SelectedSearch, GetEditorName(),
            new SearchPattern(EditPattern, EditIsCaseSensitive, EditUseRegex));
        _pendingReplacement = null;
        LoadEditor();
        RaiseEditorState();
        InvokePropertyChanged(nameof(IsReplaceConfirmationVisible));
    }

    private void CancelEdit()
    {
        _pendingReplacement = null;
        LoadEditor();
        RaiseEditorState();
        InvokePropertyChanged(nameof(IsReplaceConfirmationVisible));
    }

    private void Duplicate()
    {
        if (SelectedSearch == null) return;
        var baseName = $"{SelectedSearch.Name} ({Strings.Get("SavedSearchCopySuffix")})";
        var name = baseName;
        for (var suffix = 2; FindByName(name) != null; suffix++)
            name = $"{baseName} {suffix}";

        _store.AddOrUpdate(name, SelectedSearch.ToSearchPattern());
        SelectedSearch = SavedSearches.First(item => string.Equals(item.Name, name, StringComparison.Ordinal));
    }

    private void Delete()
    {
        if (SelectedSearch == null) return;
        _deletedSearch = SelectedSearch;
        _deletedSearchIndex = SavedSearches.IndexOf(SelectedSearch);
        _store.Remove(SelectedSearch);
        SelectedSearch = null;
        RaiseDeletedState();
    }

    private void UndoDelete()
    {
        if (_deletedSearch == null) return;
        var restored = _deletedSearch;
        _store.Restore(restored, _deletedSearchIndex);
        _deletedSearch = null;
        _deletedSearchIndex = -1;
        SelectedSearch = restored;
        RaiseDeletedState();
    }

    private string GetEditorName() => string.IsNullOrWhiteSpace(EditName) ? EditPattern : EditName.Trim();

    private SavedSearchPattern? FindByName(string name) => SavedSearches.FirstOrDefault(
        item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));

    private void LoadEditor()
    {
        _editName = SelectedSearch?.Name ?? string.Empty;
        _editPattern = SelectedSearch?.Pattern ?? string.Empty;
        _editIsCaseSensitive = SelectedSearch?.IsCaseSensitive ?? false;
        _editUseRegex = SelectedSearch?.UseRegex ?? false;
        InvokePropertyChanged(nameof(EditName));
        InvokePropertyChanged(nameof(EditPattern));
        InvokePropertyChanged(nameof(EditIsCaseSensitive));
        InvokePropertyChanged(nameof(EditUseRegex));
    }

    private void RaiseEditorState()
    {
        InvokePropertyChanged(nameof(HasChanges));
        InvokePropertyChanged(nameof(HasNoChanges));
        InvokePropertyChanged(nameof(ValidationMessage));
        InvokePropertyChanged(nameof(HasValidationMessage));
        InvokePropertyChanged(nameof(CanSave));
    }

    private void RaiseDeletedState()
    {
        InvokePropertyChanged(nameof(CanUndoDelete));
        InvokePropertyChanged(nameof(DeletedSearchName));
    }

    private void OnSavedSearchesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (SelectedSearch != null && !SavedSearches.Contains(SelectedSearch))
            SelectedSearch = null;
        if (_deletedSearch != null && SavedSearches.Contains(_deletedSearch))
        {
            _deletedSearch = null;
            _deletedSearchIndex = -1;
            RaiseDeletedState();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SavedSearches.CollectionChanged -= OnSavedSearchesChanged;
    }
}
