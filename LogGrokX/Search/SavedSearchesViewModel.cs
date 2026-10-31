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
    private readonly Func<bool> _confirmDiscardChanges;
    private SavedSearchPattern? _selectedSearch;
    private SavedSearchPattern? _selectionBeforeAdd;
    private SavedSearchPattern? _pendingReplacement;
    private string _savedSearchFilter = string.Empty;
    private string _editName = string.Empty;
    private string _editPattern = string.Empty;
    private bool _editIsCaseSensitive;
    private bool _editUseRegex;
    private bool _isAddingSearch;
    private bool _updatingList;
    private bool _disposed;

    public SavedSearchesViewModel(SavedSearchPatternStore store, Func<bool> confirmDiscardChanges)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _confirmDiscardChanges = confirmDiscardChanges ?? throw new ArgumentNullException(nameof(confirmDiscardChanges));
        SavedSearches = new ObservableCollection<SavedSearchPattern>(_store.Items.Select(Clone));
        SavedSearches.CollectionChanged += OnSavedSearchesChanged;
        SaveCommand = new DelegateCommand(Save);
        CancelEditCommand = new DelegateCommand(CancelEdit);
        AddCommand = new DelegateCommand(Add);
        DuplicateCommand = new DelegateCommand(Duplicate);
        DeleteCommand = new DelegateCommand(Delete);
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
            if (_updatingList || ReferenceEquals(_selectedSearch, value)) return;
            if (value != null && !SavedSearches.Contains(value)) return;
            if (!ConfirmDiscardEditorChanges()) return;
            SelectSearch(value);
        }
    }

    private void SelectSearch(SavedSearchPattern? search)
    {
        _selectedSearch = search;
        _isAddingSearch = false;
        _selectionBeforeAdd = null;
        _pendingReplacement = null;
        LoadEditor();
        RaiseSelectionState();
    }

    public bool HasSelectedSearch => SelectedSearch != null;

    public bool IsAddingSearch => _isAddingSearch;

    public bool HasEditor => HasSelectedSearch || IsAddingSearch;

    public bool HasNoEditor => !HasEditor;

    public bool IsSelectedSearchDeleted => SelectedSearch?.IsPendingDeletion == true;

    public bool CanEdit => HasEditor && !IsSelectedSearchDeleted;

    public bool CanDuplicate => HasSelectedSearch && !IsSelectedSearchDeleted;

    public bool ShowActions => HasEditor || HasChanges;

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

    public bool HasEditorChanges => CanEdit &&
        (EditName != (SelectedSearch?.Name ?? string.Empty) ||
         EditPattern != (SelectedSearch?.Pattern ?? string.Empty) ||
         EditIsCaseSensitive != (SelectedSearch?.IsCaseSensitive ?? false) ||
         EditUseRegex != (SelectedSearch?.UseRegex ?? false));

    public bool HasChanges => HasEditorChanges || HasListChanges;

    private bool HasListChanges => SavedSearches.Count != _store.Items.Count ||
        SavedSearches.Where((item, index) => item.IsPendingDeletion || !HaveSameValues(item, _store.Items[index])).Any();

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

    public bool HasValidationMessage => CanEdit && !string.IsNullOrEmpty(ValidationMessage);

    public bool CanSave => HasChanges && (!HasEditorChanges || !HasValidationMessage) && !IsReplaceConfirmationVisible;

    public bool CanCancelEdit => HasChanges || IsAddingSearch;

    public bool IsReplaceConfirmationVisible => _pendingReplacement != null;

    public ICommand SaveCommand { get; }

    public ICommand CancelEditCommand { get; }

    public ICommand AddCommand { get; }

    public ICommand DuplicateCommand { get; }

    public ICommand DeleteCommand { get; }

    public ICommand ConfirmReplaceCommand { get; }

    public ICommand CancelReplaceCommand { get; }

    public event Action? FocusListRequested;

    public event Action? FocusEditorRequested;

    public bool ConfirmDiscardChanges() => !HasChanges || _confirmDiscardChanges();

    private bool ConfirmDiscardEditorChanges() => !HasEditorChanges || _confirmDiscardChanges();

    private void Add()
    {
        if (!ConfirmDiscardEditorChanges()) return;
        _selectionBeforeAdd = SelectedSearch;
        _selectedSearch = null;
        _isAddingSearch = true;
        _pendingReplacement = null;
        SavedSearchFilter = string.Empty;
        LoadEditor();
        RaiseSelectionState();
        FocusEditorRequested?.Invoke();
    }

    private void Save()
    {
        if (!CanSave) return;
        if (!HasEditorChanges)
        {
            CommitSave();
            return;
        }
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
        if (_pendingReplacement == null || !CanEdit || HasValidationMessage) return;
        SavedSearches.Remove(_pendingReplacement);
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
        var selection = SelectedSearch;
        var selectedIndex = selection == null ? 0 : SavedSearches.IndexOf(selection);
        _updatingList = true;
        try
        {
            if (HasEditorChanges)
            {
                var name = GetEditorName();
                var pattern = new SearchPattern(EditPattern, EditIsCaseSensitive, EditUseRegex);
                if (IsAddingSearch)
                {
                    selection = SavedSearchPattern.FromSearchPattern(name, pattern);
                    SavedSearches.Insert(0, selection);
                }
                else if (selection != null)
                {
                    selection.Name = name;
                    selection.Pattern = pattern.Pattern;
                    selection.IsCaseSensitive = pattern.IsCaseSensitive;
                    selection.UseRegex = pattern.UseRegex;
                }
            }

            foreach (var deleted in SavedSearches.Where(item => item.IsPendingDeletion).ToArray())
                SavedSearches.Remove(deleted);
            _store.ReplaceAll(SavedSearches.Select(Clone));
        }
        finally
        {
            _updatingList = false;
        }

        SelectSearch(selection != null && SavedSearches.Contains(selection) ? selection :
            SavedSearches.ElementAtOrDefault(Math.Clamp(selectedIndex, 0, Math.Max(0, SavedSearches.Count - 1))));
        FocusListRequested?.Invoke();
    }

    private void CancelEdit()
    {
        var selectedName = (IsAddingSearch ? _selectionBeforeAdd : SelectedSearch)?.Name;
        _updatingList = true;
        try
        {
            SavedSearches.Clear();
            foreach (var search in _store.Items)
                SavedSearches.Add(Clone(search));
        }
        finally
        {
            _updatingList = false;
        }
        SelectSearch(SavedSearches.FirstOrDefault(item => item.Name == selectedName) ??
            SavedSearches.FirstOrDefault(item => item.MatchesFilter(SavedSearchFilter)));
        FocusListRequested?.Invoke();
    }

    private void Duplicate()
    {
        if (!CanDuplicate || SelectedSearch == null || !ConfirmDiscardEditorChanges()) return;
        var baseName = $"{SelectedSearch.Name} ({Strings.Get("SavedSearchCopySuffix")})";
        var name = baseName;
        for (var suffix = 2; FindByName(name) != null; suffix++)
            name = $"{baseName} {suffix}";

        var copy = SavedSearchPattern.FromSearchPattern(name, SelectedSearch.ToSearchPattern());
        SavedSearches.Insert(0, copy);
        SavedSearchFilter = string.Empty;
        SelectSearch(copy);
        FocusListRequested?.Invoke();
    }

    private void Delete()
    {
        if (SelectedSearch == null || !ConfirmDiscardEditorChanges()) return;
        var selected = SelectedSearch;
        var visible = SavedSearches.Where(item => !item.IsPendingDeletion && item.MatchesFilter(SavedSearchFilter)).ToArray();
        var index = Array.IndexOf(visible, selected);
        selected.IsPendingDeletion = !selected.IsPendingDeletion;
        var remaining = visible.Where(item => !item.IsPendingDeletion).ToArray();
        SelectSearch(!selected.IsPendingDeletion || remaining.Length == 0 ? selected :
            remaining[Math.Clamp(index, 0, remaining.Length - 1)]);
        FocusListRequested?.Invoke();
    }

    private string GetEditorName() => string.IsNullOrWhiteSpace(EditName) ? EditPattern : EditName.Trim();

    private SavedSearchPattern? FindByName(string name) => SavedSearches.FirstOrDefault(
        item => !item.IsPendingDeletion && string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));

    private static SavedSearchPattern Clone(SavedSearchPattern search) =>
        SavedSearchPattern.FromSearchPattern(search.Name, search.ToSearchPattern());

    private static bool HaveSameValues(SavedSearchPattern first, SavedSearchPattern second) =>
        first.Name == second.Name && first.Pattern == second.Pattern &&
        first.IsCaseSensitive == second.IsCaseSensitive && first.UseRegex == second.UseRegex;

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
        RaiseEditorState();
    }

    private void RaiseSelectionState()
    {
        InvokePropertyChanged(nameof(SelectedSearch));
        InvokePropertyChanged(nameof(HasSelectedSearch));
        InvokePropertyChanged(nameof(IsAddingSearch));
        InvokePropertyChanged(nameof(HasEditor));
        InvokePropertyChanged(nameof(HasNoEditor));
        InvokePropertyChanged(nameof(IsSelectedSearchDeleted));
        InvokePropertyChanged(nameof(CanEdit));
        InvokePropertyChanged(nameof(CanDuplicate));
        InvokePropertyChanged(nameof(IsReplaceConfirmationVisible));
    }

    private void RaiseEditorState()
    {
        InvokePropertyChanged(nameof(HasChanges));
        InvokePropertyChanged(nameof(HasEditorChanges));
        InvokePropertyChanged(nameof(HasNoChanges));
        InvokePropertyChanged(nameof(ValidationMessage));
        InvokePropertyChanged(nameof(HasValidationMessage));
        InvokePropertyChanged(nameof(CanSave));
        InvokePropertyChanged(nameof(CanCancelEdit));
        InvokePropertyChanged(nameof(ShowActions));
    }

    private void OnSavedSearchesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_updatingList) return;
        if (SelectedSearch != null && !SavedSearches.Contains(SelectedSearch))
            SelectSearch(null);
        RaiseEditorState();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SavedSearches.CollectionChanged -= OnSavedSearchesChanged;
    }
}
