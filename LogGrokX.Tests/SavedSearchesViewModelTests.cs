#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using LogGrokX.Localization;
using LogGrokX.Search;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Tests;

[TestClass]
[DoNotParallelize]
public sealed class SavedSearchesViewModelTests
{
    private readonly string _storeFile = Path.Combine(Path.GetTempPath(), $"LogGrokX-saved-searches-{Guid.NewGuid():N}.json");

    [TestCleanup]
    public void Cleanup() => File.Delete(_storeFile);

    [TestMethod]
    public void LegacyJsonKeepsSchemaAndSearchParameters()
    {
        const string json = """
            [{"Name":"Errors","Pattern":"error|timeout","IsCaseSensitive":true,"UseRegex":true}]
            """;
        var items = JsonSerializer.Deserialize<SavedSearchPattern[]>(json);
        Assert.IsNotNull(items);
        var item = items.Single();
        Assert.AreEqual("Errors", item.Name);
        Assert.AreEqual("error|timeout", item.Pattern);
        Assert.IsTrue(item.IsCaseSensitive);
        Assert.IsTrue(item.UseRegex);

        item.IsPendingDeletion = true;
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(item));
        CollectionAssert.AreEquivalent(
            new[] { "Name", "Pattern", "IsCaseSensitive", "UseRegex" },
            document.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
    }

    [TestMethod]
    public void ApplyingSavedSearchPreservesParametersAndClosesQuickList()
    {
        var store = CreateStore();
        var saved = Saved("Errors", "error|timeout", true, true);
        store.Items.Add(saved);
        using var search = CreateSearch(store);
        search.IsSavedSearchesOpen = true;

        search.ApplySavedSearchCommand.Execute(saved);

        Assert.AreEqual(saved.Pattern, search.TextToSearch);
        Assert.IsTrue(search.IsCaseSensitive);
        Assert.IsTrue(search.UseRegex);
        Assert.IsFalse(search.IsSavedSearchesOpen);
    }

    [TestMethod]
    public void SavingDuplicateNameRequiresConfirmationBeforeReplacing()
    {
        var store = CreateStore();
        var source = Saved("Source", "new pattern", true, true);
        var target = Saved("Target", "old pattern");
        store.Items.Add(source);
        store.Items.Add(target);
        using var search = CreateSearch(store);
        search.ApplySavedSearchCommand.Execute(source);
        search.ShowSaveSearchEditorCommand.Execute(null);
        search.NewSearchName = target.Name;

        search.SaveSearchCommand.Execute(null);

        Assert.IsTrue(search.IsReplaceConfirmationVisible);
        Assert.AreEqual("old pattern", target.Pattern);
        Assert.AreEqual(2, store.Items.Count);

        search.ConfirmReplaceSearchCommand.Execute(null);

        Assert.IsFalse(search.IsReplaceConfirmationVisible);
        Assert.IsFalse(search.IsSaveSearchEditorOpen);
        Assert.AreEqual(2, store.Items.Count);
        var replaced = store.Items.Single(item => item.Name == target.Name);
        Assert.AreEqual(source.Pattern, replaced.Pattern);
        Assert.IsTrue(replaced.IsCaseSensitive);
        Assert.IsTrue(replaced.UseRegex);
    }

    [TestMethod]
    public void ManagerValidatesRegexAndKeepsStoreUnchangedBeforeGlobalSave()
    {
        var store = CreateStore();
        var original = Saved("Errors", "old");
        store.Items.Add(original);
        using var manager = new SavedSearchesViewModel(store, () => true);
        manager.EditPattern = "[";
        manager.EditUseRegex = true;
        manager.SaveCommand.Execute(null);
        Assert.IsFalse(manager.CanSave);
        Assert.IsTrue(manager.HasValidationMessage);
        Assert.AreEqual("old", original.Pattern);

        manager.EditPattern = "new";
        manager.EditIsCaseSensitive = true;
        manager.SaveCommand.Execute(null);

        var saved = store.Items.Single();
        Assert.AreEqual("new", saved.Pattern);
        Assert.IsTrue(saved.IsCaseSensitive);
        Assert.IsTrue(saved.UseRegex);
        Assert.AreEqual("old", original.Pattern);
        Assert.AreNotSame(saved, manager.SelectedSearch);
        Assert.IsFalse(manager.HasChanges);
    }

    [TestMethod]
    public void DuplicateIsStagedWithUniqueNameUntilGlobalSave()
    {
        var store = CreateStore();
        var original = Saved("Errors", "error", true, true);
        store.Items.Add(original);
        store.Items.Add(Saved($"Errors ({Strings.Get("SavedSearchCopySuffix")})", "other"));
        using var manager = new SavedSearchesViewModel(store, () => true);

        manager.DuplicateCommand.Execute(null);

        var copy = manager.SelectedSearch;
        Assert.IsNotNull(copy);
        Assert.AreEqual($"Errors ({Strings.Get("SavedSearchCopySuffix")}) 2", copy.Name);
        Assert.AreEqual(original.Pattern, copy.Pattern);
        Assert.IsTrue(copy.IsCaseSensitive);
        Assert.IsTrue(copy.UseRegex);
        Assert.AreEqual(3, manager.SavedSearches.Count);
        Assert.AreEqual(2, store.Items.Count);
        Assert.IsTrue(manager.CanSave);
        manager.SaveCommand.Execute(null);
        Assert.AreEqual(3, store.Items.Count);
        Assert.IsFalse(manager.HasChanges);
    }

    [TestMethod]
    public void DeletedSearchStaysInDraftAndCanBeRestoredAtOriginalPosition()
    {
        var store = CreateStore();
        store.Items.Add(Saved("First", "1"));
        store.Items.Add(Saved("Second", "2", true, true));
        store.Items.Add(Saved("Third", "3"));
        store.Save();
        var savedJson = File.ReadAllText(_storeFile);
        using var manager = new SavedSearchesViewModel(store, () => true);
        var second = manager.SavedSearches[1];
        manager.SelectedSearch = second;

        manager.DeleteCommand.Execute(null);

        Assert.IsTrue(second.IsPendingDeletion);
        Assert.AreSame(second, manager.SavedSearches[1]);
        Assert.AreEqual(3, store.Items.Count);
        Assert.IsFalse(store.Items[1].IsPendingDeletion);
        Assert.AreEqual(savedJson, File.ReadAllText(_storeFile));
        Assert.IsTrue(manager.CanSave);
        manager.SelectedSearch = second;
        Assert.IsTrue(manager.IsSelectedSearchDeleted);
        Assert.IsFalse(manager.CanEdit);
        Assert.IsFalse(manager.CanDuplicate);

        manager.DeleteCommand.Execute(null);

        Assert.IsFalse(second.IsPendingDeletion);
        Assert.AreSame(second, manager.SelectedSearch);
        Assert.AreSame(second, manager.SavedSearches[1]);
        Assert.IsTrue(second.IsCaseSensitive);
        Assert.IsTrue(second.UseRegex);
        Assert.IsFalse(manager.HasChanges);
    }

    [TestMethod]
    public void GlobalSaveAppliesSeveralDeletionsAndCurrentEditTogether()
    {
        var store = CreateStore();
        store.Items.Add(Saved("First", "1"));
        store.Items.Add(Saved("Second", "2"));
        store.Items.Add(Saved("Third", "3"));
        store.Save();
        using var manager = new SavedSearchesViewModel(store, () => true);
        var first = manager.SavedSearches[0];
        var second = manager.SavedSearches[1];
        manager.SelectedSearch = second;
        manager.DeleteCommand.Execute(null);
        manager.SelectedSearch = first;
        manager.DeleteCommand.Execute(null);
        manager.EditPattern = "updated";
        Assert.AreEqual(3, store.Items.Count);

        manager.SaveCommand.Execute(null);

        Assert.AreEqual("Third", store.Items.Single().Name);
        Assert.AreEqual("updated", store.Items.Single().Pattern);
        Assert.AreEqual("updated", SavedSearchPatternStore.LoadFromFile(_storeFile).Items.Single().Pattern);
        Assert.AreEqual(1, manager.SavedSearches.Count);
        Assert.IsFalse(manager.SavedSearches.Contains(first));
        Assert.IsFalse(manager.SavedSearches.Contains(second));
        Assert.IsFalse(manager.HasChanges);
        Assert.IsFalse(manager.CanSave);
    }

    [TestMethod]
    public void GlobalCancelRestoresDeletionsAndDiscardsCopiesAndEditorChanges()
    {
        var store = CreateStore();
        store.Items.Add(Saved("First", "1"));
        store.Items.Add(Saved("Second", "2"));
        store.Save();
        var savedJson = File.ReadAllText(_storeFile);
        using var manager = new SavedSearchesViewModel(store, () => true);
        manager.DeleteCommand.Execute(null);
        manager.DuplicateCommand.Execute(null);
        manager.EditPattern = "unsaved";

        manager.CancelEditCommand.Execute(null);

        CollectionAssert.AreEqual(new[] { "First", "Second" }, manager.SavedSearches.Select(item => item.Name).ToArray());
        Assert.IsFalse(manager.SavedSearches.Any(item => item.IsPendingDeletion));
        Assert.IsFalse(manager.HasChanges);
        Assert.IsFalse(manager.CanSave);
        Assert.IsFalse(manager.CanCancelEdit);
        Assert.AreEqual(savedJson, File.ReadAllText(_storeFile));
    }

    [TestMethod]
    public void AllDeletedRowsRemainVisibleUntilGlobalSaveThenCannotBeRestored()
    {
        var store = CreateStore();
        store.Items.Add(Saved("First", "1"));
        store.Items.Add(Saved("Second", "2"));
        using var manager = new SavedSearchesViewModel(store, () => true);
        manager.DeleteCommand.Execute(null);
        manager.DeleteCommand.Execute(null);
        Assert.AreEqual(2, manager.SavedSearches.Count);
        Assert.IsTrue(manager.SavedSearches.All(item => item.IsPendingDeletion));
        Assert.IsTrue(manager.IsSelectedSearchDeleted);
        Assert.IsTrue(manager.CanSave);
        Assert.IsTrue(manager.ShowActions);
        Assert.IsFalse(manager.CanEdit);
        Assert.IsFalse(manager.HasValidationMessage);

        manager.SaveCommand.Execute(null);

        Assert.AreEqual(0, store.Items.Count);
        Assert.AreEqual(0, SavedSearchPatternStore.LoadFromFile(_storeFile).Items.Count);
        Assert.AreEqual(0, manager.SavedSearches.Count);
        Assert.IsTrue(manager.HasNoEditor);
        Assert.IsFalse(manager.ShowActions);
        Assert.IsFalse(manager.HasChanges);
        manager.CancelEditCommand.Execute(null);
        Assert.AreEqual(0, manager.SavedSearches.Count);
    }

    [TestMethod]
    public void ManagerRenameConflictRequiresConfirmationBeforeGlobalSave()
    {
        var store = CreateStore();
        store.Items.Add(Saved("First", "old"));
        store.Items.Add(Saved("Second", "other"));
        using var manager = new SavedSearchesViewModel(store, () => true);
        manager.EditName = "Second";
        manager.EditPattern = "updated";
        manager.SaveCommand.Execute(null);
        Assert.IsTrue(manager.IsReplaceConfirmationVisible);
        Assert.AreEqual("First", store.Items[0].Name);
        Assert.AreEqual("other", store.Items[1].Pattern);

        manager.ConfirmReplaceCommand.Execute(null);

        Assert.IsFalse(manager.IsReplaceConfirmationVisible);
        Assert.AreEqual("Second", store.Items.Single().Name);
        Assert.AreEqual("updated", store.Items.Single().Pattern);
        Assert.IsFalse(manager.HasChanges);
    }

    [TestMethod]
    public void SwitchingSearchKeepsDraftUntilDiscardIsConfirmedAndRefreshesButtons()
    {
        var store = CreateStore();
        store.Items.Add(Saved("First", "original"));
        store.Items.Add(Saved("Second", "other"));
        var discard = false;
        var confirmations = 0;
        using var manager = new SavedSearchesViewModel(store, () => { confirmations++; return discard; });
        var first = manager.SavedSearches[0];
        var second = manager.SavedSearches[1];
        var changed = new List<string?>();
        manager.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        manager.EditPattern = "draft";
        manager.SelectedSearch = second;
        Assert.AreEqual(1, confirmations);
        Assert.AreSame(first, manager.SelectedSearch);
        Assert.AreEqual("draft", manager.EditPattern);
        Assert.AreEqual("original", store.Items[0].Pattern);

        discard = true;
        changed.Clear();
        manager.SelectedSearch = second;

        Assert.AreEqual(2, confirmations);
        Assert.AreSame(second, manager.SelectedSearch);
        Assert.AreEqual("other", manager.EditPattern);
        Assert.IsFalse(manager.CanSave);
        Assert.IsFalse(manager.CanCancelEdit);
        foreach (var property in new[] { nameof(manager.HasChanges), nameof(manager.CanSave),
                     nameof(manager.CanCancelEdit), nameof(manager.HasValidationMessage) })
            CollectionAssert.Contains(changed, property);
    }

    [TestMethod]
    public void SwitchingFromInvalidDraftClearsValidationAndReplacementState()
    {
        var store = CreateStore();
        store.Items.Add(Saved("First", "original"));
        store.Items.Add(Saved("Second", "other"));
        using var manager = new SavedSearchesViewModel(store, () => true);
        manager.EditName = "Second";
        manager.SaveCommand.Execute(null);
        Assert.IsTrue(manager.IsReplaceConfirmationVisible);
        manager.EditPattern = "[";
        manager.EditUseRegex = true;
        Assert.IsTrue(manager.HasValidationMessage);

        manager.SelectedSearch = manager.SavedSearches[1];

        Assert.IsFalse(manager.IsReplaceConfirmationVisible);
        Assert.IsFalse(manager.HasValidationMessage);
        Assert.IsFalse(manager.HasChanges);
    }

    [TestMethod]
    public void MarkingDeletedSelectsNearestActiveVisibleSearchAndRequestsFocus()
    {
        var store = CreateStore();
        store.Items.Add(Saved("Visible first", "1"));
        store.Items.Add(Saved("Hidden", "2"));
        store.Items.Add(Saved("Visible last", "3"));
        using var manager = new SavedSearchesViewModel(store, () => true) { SavedSearchFilter = "Visible" };
        var last = manager.SavedSearches[2];
        var focusRequests = 0;
        manager.FocusListRequested += () => focusRequests++;

        manager.DeleteCommand.Execute(null);

        Assert.AreSame(last, manager.SelectedSearch);
        Assert.IsTrue(manager.CanSave);
        Assert.AreEqual(1, focusRequests);
        manager.DeleteCommand.Execute(null);
        Assert.AreSame(last, manager.SelectedSearch);
        Assert.IsTrue(manager.IsSelectedSearchDeleted);
        Assert.IsFalse(manager.HasValidationMessage);
        Assert.AreEqual(2, focusRequests);
        Assert.IsFalse(manager.SavedSearches[1].IsPendingDeletion);
    }

    [TestMethod]
    [DataRow("Add")]
    [DataRow("Duplicate")]
    [DataRow("Delete")]
    public void ActionsThatLeaveEditorRespectRejectedDiscard(string action)
    {
        var store = CreateStore();
        store.Items.Add(Saved("First", "1"));
        store.Items.Add(Saved("Second", "2"));
        var confirmations = 0;
        using var manager = new SavedSearchesViewModel(store, () => { confirmations++; return false; });
        var selected = manager.SelectedSearch;
        manager.EditPattern = "draft";
        var command = action switch
        {
            "Add" => manager.AddCommand,
            "Duplicate" => manager.DuplicateCommand,
            _ => manager.DeleteCommand
        };

        command.Execute(null);

        Assert.AreEqual(1, confirmations);
        Assert.AreSame(selected, manager.SelectedSearch);
        Assert.AreEqual(2, manager.SavedSearches.Count);
        Assert.IsFalse(manager.SavedSearches.Any(item => item.IsPendingDeletion));
        Assert.AreEqual("draft", manager.EditPattern);
        Assert.IsTrue(manager.HasChanges);
    }

    [TestMethod]
    public void NewSearchIsDraftUntilSavedAndPersistsAllParameters()
    {
        var store = CreateStore();
        using var manager = new SavedSearchesViewModel(store, () => true) { SavedSearchFilter = "old filter" };
        var editorFocusRequests = 0;
        manager.FocusEditorRequested += () => editorFocusRequests++;
        manager.AddCommand.Execute(null);
        Assert.IsTrue(manager.IsAddingSearch);
        Assert.IsTrue(manager.HasEditor);
        Assert.IsTrue(manager.CanCancelEdit);
        Assert.IsFalse(manager.CanSave);
        Assert.AreEqual(string.Empty, manager.SavedSearchFilter);
        Assert.AreEqual(1, editorFocusRequests);
        Assert.AreEqual(0, store.Items.Count);
        manager.EditName = "  Errors  ";
        manager.EditPattern = "error|timeout";
        manager.EditIsCaseSensitive = true;
        manager.EditUseRegex = true;

        manager.SaveCommand.Execute(null);

        var saved = store.Items.Single();
        Assert.AreEqual("Errors", saved.Name);
        Assert.AreEqual("error|timeout", saved.Pattern);
        Assert.IsTrue(saved.IsCaseSensitive);
        Assert.IsTrue(saved.UseRegex);
        Assert.AreSame(manager.SavedSearches.Single(), manager.SelectedSearch);
        Assert.AreNotSame(saved, manager.SelectedSearch);
        Assert.IsFalse(manager.IsAddingSearch);
        Assert.IsFalse(manager.CanSave);
        Assert.IsFalse(manager.CanCancelEdit);
        var reloaded = SavedSearchPatternStore.LoadFromFile(_storeFile).Items.Single();
        Assert.AreEqual(saved.Name, reloaded.Name);
        Assert.AreEqual(saved.Pattern, reloaded.Pattern);
        Assert.AreEqual(saved.IsCaseSensitive, reloaded.IsCaseSensitive);
        Assert.AreEqual(saved.UseRegex, reloaded.UseRegex);
    }

    [TestMethod]
    public void InvalidCurrentEditPreventsGlobalSaveOfPendingDeletions()
    {
        var store = CreateStore();
        store.Items.Add(Saved("First", "1"));
        store.Items.Add(Saved("Second", "2"));
        using var manager = new SavedSearchesViewModel(store, () => true);
        manager.DeleteCommand.Execute(null);
        manager.EditPattern = "[";
        manager.EditUseRegex = true;

        manager.SaveCommand.Execute(null);

        Assert.IsFalse(manager.CanSave);
        Assert.AreEqual(2, store.Items.Count);
        Assert.IsTrue(manager.SavedSearches[0].IsPendingDeletion);
        manager.EditPattern = "valid";
        manager.SaveCommand.Execute(null);
        Assert.AreEqual("valid", store.Items.Single().Pattern);
    }

    [TestMethod]
    public void NewSearchValidatesRegexAndDefaultsNameToPattern()
    {
        var store = CreateStore();
        using var manager = new SavedSearchesViewModel(store, () => true);
        manager.AddCommand.Execute(null);
        manager.EditPattern = "[";
        manager.EditUseRegex = true;
        manager.SaveCommand.Execute(null);
        Assert.IsTrue(manager.HasValidationMessage);
        Assert.AreEqual(0, store.Items.Count);
        manager.EditPattern = "valid";
        manager.SaveCommand.Execute(null);
        Assert.AreEqual("valid", store.Items.Single().Name);
    }

    [TestMethod]
    public void CancellingNewSearchRestoresPreviousSearchWithoutSavingDraft()
    {
        var store = CreateStore();
        store.Items.Add(Saved("Existing", "original"));
        using var manager = new SavedSearchesViewModel(store, () => false);
        var previous = manager.SelectedSearch;
        manager.AddCommand.Execute(null);
        manager.EditPattern = "draft";
        manager.SelectedSearch = previous;
        Assert.IsTrue(manager.IsAddingSearch);
        Assert.AreEqual("draft", manager.EditPattern);
        manager.CancelEditCommand.Execute(null);
        Assert.AreEqual("Existing", manager.SelectedSearch?.Name);
        Assert.AreEqual("original", manager.EditPattern);
        Assert.AreEqual(1, store.Items.Count);
        Assert.IsFalse(manager.HasChanges);
    }

    [TestMethod]
    public void NewSearchWithExistingNameRequiresConfirmation()
    {
        var store = CreateStore();
        store.Items.Add(Saved("Existing", "original"));
        using var manager = new SavedSearchesViewModel(store, () => true);
        manager.AddCommand.Execute(null);
        manager.EditName = "existing";
        manager.EditPattern = "replacement";
        manager.SaveCommand.Execute(null);
        Assert.IsTrue(manager.IsReplaceConfirmationVisible);
        Assert.AreEqual("original", store.Items.Single().Pattern);
        manager.CancelReplaceCommand.Execute(null);
        Assert.IsTrue(manager.IsAddingSearch);
        Assert.IsTrue(manager.CanSave);
        manager.SaveCommand.Execute(null);
        manager.ConfirmReplaceCommand.Execute(null);
        Assert.AreEqual("replacement", store.Items.Single().Pattern);
        Assert.IsFalse(manager.HasChanges);
    }

    [TestMethod]
    public void NewSearchCanUseNameOfRowMarkedForDeletion()
    {
        var store = CreateStore();
        store.Items.Add(Saved("Existing", "original"));
        using var manager = new SavedSearchesViewModel(store, () => true);
        manager.DeleteCommand.Execute(null);
        manager.AddCommand.Execute(null);
        manager.EditName = "existing";
        manager.EditPattern = "replacement";

        manager.SaveCommand.Execute(null);

        Assert.AreEqual("replacement", store.Items.Single().Pattern);
        Assert.IsFalse(manager.IsReplaceConfirmationVisible);
        Assert.IsFalse(manager.HasChanges);
    }

    [TestMethod]
    public void PendingDeletionsSurviveNavigationAndRequireConfirmationOnClose()
    {
        var store = CreateStore();
        store.Items.Add(Saved("First", "1"));
        store.Items.Add(Saved("Second", "2"));
        var confirmations = 0;
        using var manager = new SavedSearchesViewModel(store, () => { confirmations++; return false; });
        manager.DeleteCommand.Execute(null);
        manager.SelectedSearch = manager.SavedSearches[0];
        manager.SelectedSearch = manager.SavedSearches[1];
        Assert.AreEqual(0, confirmations);
        Assert.IsTrue(manager.SavedSearches[0].IsPendingDeletion);
        Assert.IsFalse(manager.ConfirmDiscardChanges());
        Assert.AreEqual(1, confirmations);
        manager.CancelEditCommand.Execute(null);
        Assert.IsTrue(manager.ConfirmDiscardChanges());
        Assert.AreEqual(1, confirmations);
    }

    [TestMethod]
    public void DisposingManagerDiscardsPendingChangesWithoutTouchingStore()
    {
        var store = CreateStore();
        store.Items.Add(Saved("First", "1"));
        store.Items.Add(Saved("Second", "2"));
        store.Save();
        var savedJson = File.ReadAllText(_storeFile);
        using (var manager = new SavedSearchesViewModel(store, () => true))
        {
            manager.DeleteCommand.Execute(null);
            manager.DuplicateCommand.Execute(null);
            manager.EditPattern = "unsaved";
        }

        Assert.AreEqual(savedJson, File.ReadAllText(_storeFile));
        Assert.AreEqual(2, store.Items.Count);
        using var reopened = new SavedSearchesViewModel(store, () => true);
        Assert.IsFalse(reopened.HasChanges);
        Assert.IsFalse(reopened.SavedSearches.Any(item => item.IsPendingDeletion));
    }

    [TestMethod]
    public void CancelAfterSaveDoesNotRestorePreviouslyDeletedRows()
    {
        var store = CreateStore();
        store.Items.Add(Saved("First", "1"));
        store.Items.Add(Saved("Second", "2"));
        using var manager = new SavedSearchesViewModel(store, () => true);
        manager.DeleteCommand.Execute(null);
        manager.SaveCommand.Execute(null);
        manager.EditPattern = "unsaved";

        manager.CancelEditCommand.Execute(null);

        Assert.AreEqual("Second", manager.SavedSearches.Single().Name);
        Assert.AreEqual("2", manager.EditPattern);
        Assert.IsFalse(manager.HasChanges);
    }

    private SavedSearchPatternStore CreateStore() => SavedSearchPatternStore.LoadFromFile(_storeFile);

    private static SearchViewModel CreateSearch(SavedSearchPatternStore store) => new(
        pattern => new SearchDocument { SearchPattern = pattern }, new SearchAutocompleteCache(), store,
        TestServices.Inline, TestServices.Dialogs);

    private static SavedSearchPattern Saved(string name, string pattern, bool isCaseSensitive = false, bool useRegex = false) =>
        SavedSearchPattern.FromSearchPattern(name, new SearchPattern(pattern, isCaseSensitive, useRegex));

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
