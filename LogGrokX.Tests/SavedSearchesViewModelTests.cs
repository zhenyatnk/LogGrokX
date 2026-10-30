#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
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
    public void ManagerValidatesRegexAndDoesNotChangeOriginalBeforeSave()
    {
        var store = CreateStore();
        var saved = Saved("Errors", "old");
        store.Items.Add(saved);
        using var manager = new SavedSearchesViewModel(store) { SelectedSearch = saved };

        manager.EditPattern = "[";
        manager.EditUseRegex = true;

        Assert.IsFalse(manager.CanSave);
        Assert.IsTrue(manager.HasValidationMessage);
        Assert.AreEqual("old", saved.Pattern);
        Assert.IsFalse(saved.UseRegex);

        manager.EditPattern = "new";
        manager.EditIsCaseSensitive = true;
        manager.SaveCommand.Execute(null);

        Assert.AreEqual("new", saved.Pattern);
        Assert.IsTrue(saved.IsCaseSensitive);
        Assert.IsTrue(saved.UseRegex);
        Assert.IsFalse(manager.HasChanges);
    }

    [TestMethod]
    public void ManagerDuplicateCopiesParametersAndUsesUniqueName()
    {
        var store = CreateStore();
        var saved = Saved("Errors", "error", true, true);
        store.Items.Add(saved);
        store.Items.Add(Saved($"Errors ({Strings.Get("SavedSearchCopySuffix")})", "other"));
        using var manager = new SavedSearchesViewModel(store) { SelectedSearch = saved };

        manager.DuplicateCommand.Execute(null);

        Assert.AreEqual(3, store.Items.Count);
        var copy = manager.SelectedSearch;
        Assert.IsNotNull(copy);
        Assert.AreEqual($"Errors ({Strings.Get("SavedSearchCopySuffix")}) 2", copy.Name);
        Assert.AreEqual(saved.Pattern, copy.Pattern);
        Assert.IsTrue(copy.IsCaseSensitive);
        Assert.IsTrue(copy.UseRegex);
        Assert.AreEqual("error", saved.Pattern);
    }

    [TestMethod]
    public void ManagerDeleteCanBeUndoneAtOriginalPosition()
    {
        var store = CreateStore();
        var first = Saved("First", "1");
        var second = Saved("Second", "2", true, true);
        var third = Saved("Third", "3");
        store.Items.Add(first);
        store.Items.Add(second);
        store.Items.Add(third);
        using var manager = new SavedSearchesViewModel(store) { SelectedSearch = second };

        manager.DeleteCommand.Execute(null);

        Assert.IsTrue(manager.CanUndoDelete);
        Assert.AreEqual(2, store.Items.Count);
        Assert.IsFalse(store.Items.Contains(second));

        manager.UndoDeleteCommand.Execute(null);

        Assert.IsFalse(manager.CanUndoDelete);
        Assert.AreEqual(3, store.Items.Count);
        Assert.AreSame(second, store.Items[1]);
        Assert.AreSame(second, manager.SelectedSearch);
        Assert.IsTrue(second.IsCaseSensitive);
        Assert.IsTrue(second.UseRegex);
    }

    [TestMethod]
    public void ManagerRenameConflictRequiresConfirmation()
    {
        var store = CreateStore();
        var first = Saved("First", "old");
        var second = Saved("Second", "other");
        store.Items.Add(first);
        store.Items.Add(second);
        using var manager = new SavedSearchesViewModel(store) { SelectedSearch = first };
        manager.EditName = second.Name;
        manager.EditPattern = "updated";

        manager.SaveCommand.Execute(null);

        Assert.IsTrue(manager.IsReplaceConfirmationVisible);
        Assert.AreEqual("First", first.Name);
        Assert.AreEqual("other", second.Pattern);

        manager.ConfirmReplaceCommand.Execute(null);

        Assert.IsFalse(manager.IsReplaceConfirmationVisible);
        Assert.AreEqual(1, store.Items.Count);
        Assert.AreEqual("Second", first.Name);
        Assert.AreEqual("updated", first.Pattern);
    }

    private static SavedSearchPatternStore CreateStore()
    {
        var store = new SavedSearchPatternStore();
        store.Items.Clear();
        return store;
    }

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
