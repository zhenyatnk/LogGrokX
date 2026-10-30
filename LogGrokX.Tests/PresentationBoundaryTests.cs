#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using AvalonDock.Themes;
using AvalonDock.Themes.VS2013.Themes;
using LogGrokX.Controls;
using LogGrokX.Controls.GridView;
using LogGrokX.Controls.ListControls;
using LogGrokX.Data;
using LogGrokX.MarkedLines;
using LogGrokX.Platform;
using LogGrokX.Search;
using LogGrokX.Theming;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Tests;

[TestClass]
[DoNotParallelize]
public sealed class PresentationBoundaryTests
{
    [TestMethod]
    public void SavedSearchViewsFilterIndependentlyWithoutResettingSelectionOnInsert()
    {
        RunOnSta(() =>
        {
            var first = Saved("first");
            var second = Saved("second");
            var items = new ObservableCollection<SavedSearchPattern> { first, second };
            var firstSource = new SavedSearchCollectionViewSource { Source = items, FilterText = "first" };
            var secondSource = new SavedSearchCollectionViewSource { Source = items, FilterText = "second" };
            Assert.AreSame(first, firstSource.View.Cast<SavedSearchPattern>().Single());
            Assert.AreSame(second, secondSource.View.Cast<SavedSearchPattern>().Single());
            secondSource.View.MoveCurrentTo(second);
            var resets = 0;
            secondSource.View.CollectionChanged += (_, e) => { if (e.Action == NotifyCollectionChangedAction.Reset) resets++; };
            items.Insert(0, Saved("another first"));
            items.Remove(first);

            Assert.AreEqual(0, resets);
            Assert.AreSame(second, secondSource.View.CurrentItem);
            Assert.AreEqual(1, firstSource.View.Cast<object>().Count());
            Assert.AreEqual(1, secondSource.View.Cast<object>().Count());
        });
    }

    [TestMethod]
    public void SavedSearchViewResourceFollowsDataContextAndFilterBinding()
    {
        RunOnSta(() =>
        {
            var store = new SavedSearchPatternStore();
            store.Items.Clear();
            store.Items.Add(Saved("first"));
            store.Items.Add(Saved("second"));
            using var model = new SearchViewModel(_ => throw new InvalidOperationException(),
                new SearchAutocompleteCache(), store, new WpfUiDispatcher(Dispatcher.CurrentDispatcher), TestServices.Dialogs);
            var grid = (Grid)XamlReader.Parse("""
                <Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                      xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                      xmlns:controls="clr-namespace:LogGrokX.Controls;assembly=LogGrokX">
                    <Grid.Resources>
                        <controls:SavedSearchCollectionViewSource x:Key="Filtered"
                            Source="{Binding SavedSearches}" FilterText="{Binding SavedSearchFilter}" />
                    </Grid.Resources>
                    <ListBox ItemsSource="{Binding Source={StaticResource Filtered}}" />
                </Grid>
                """);
            grid.DataContext = model;
            var window = CreateWindow(grid);
            try
            {
                window.Show();
                window.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                var list = (ListBox)grid.Children[0];
                Assert.AreEqual(2, list.Items.Count);
                model.SavedSearchFilter = "second";
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                Assert.AreEqual(1, list.Items.Count);
                Assert.AreEqual("second", ((SavedSearchPattern)list.Items[0]).Name);
            }
            finally { window.Close(); }
        });
    }

    [TestMethod]
    public void SharedSavedSearchStoreDoesNotRetainUndisposedViewModel()
    {
        var store = new SavedSearchPatternStore();
        var reference = CreateWeakSearch(store);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.IsFalse(reference.IsAlive);
        GC.KeepAlive(store);
    }

    [TestMethod]
    public void MergedGridReusesSourceTemplateOnItsUiThread()
    {
        RunOnSta(() =>
        {
            var meta = new LogMetaInformation(new LogFormat { Regex = "^(?<Message>.*)$" });
            using var model = new LogGridModel(meta, new ColumnSettings(), hasSourceColumn: true);
            var converter = new LogGridViewConverter();
            var first = (System.Windows.Controls.GridView)converter.Convert(model, typeof(ViewBase), null!, CultureInfo.InvariantCulture);
            var second = (System.Windows.Controls.GridView)converter.Convert(model, typeof(ViewBase), null!, CultureInfo.InvariantCulture);
            Assert.AreNotSame(first, second);
            Assert.AreSame(first.Columns[1].CellTemplate, second.Columns[1].CellTemplate);
        });
    }

    [TestMethod]
    public void DockAccentUpdatesBothExistingThemesWithoutRecreatingThem()
    {
        RunOnSta(() =>
        {
            var light = (DictionaryTheme)DockThemes.GetTheme(false);
            var dark = (DictionaryTheme)DockThemes.GetTheme(true);
            var originals = new[] { light, dark }.Select(theme => (theme, resources: theme.ThemeResourceDictionary.Keys
                .Cast<object>().ToDictionary(key => key, key => theme.ThemeResourceDictionary[key]))).ToArray();
            try
            {
                var accent = Color.FromRgb(0xd1, 0x34, 0x38);
                DockThemes.ApplyAccentColor(accent);
                var converter = new DockThemeConverter();
                Assert.AreSame(light, converter.Convert(false, typeof(Theme), null!, CultureInfo.InvariantCulture));
                Assert.AreSame(dark, converter.Convert(true, typeof(Theme), null!, CultureInfo.InvariantCulture));
                foreach (var theme in new[] { light, dark })
                    Assert.AreEqual(accent, ((SolidColorBrush)theme.ThemeResourceDictionary[ResourceKeys.DocumentWellTabSelectedActiveBackground]).Color);
            }
            finally
            {
                foreach (var (theme, resources) in originals)
                {
                    foreach (var key in theme.ThemeResourceDictionary.Keys.Cast<object>().Except(resources.Keys).ToArray())
                        theme.ThemeResourceDictionary.Remove(key);
                    foreach (var (key, value) in resources) theme.ThemeResourceDictionary[key] = value;
                }
            }
        });
    }

    [TestMethod]
    public void MarkedLinesTemplateGroupsRowsByDocumentAndUpdatesGroups()
    {
        RunOnSta(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "loggrokx-grouping-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var settingsPath = Path.Combine(directory, "settings.yaml");
                File.WriteAllText(settingsPath, "Settings:\n  LogFormats:\n    - Regex: '^(?<Message>.*)$'\n");
                using var model = TestServices.CreateMain(ApplicationSettings.LoadFromFile(settingsPath));
                for (var i = 0; i < 2; i++)
                {
                    var path = Path.Combine(directory, $"source-{i}.log");
                    File.WriteAllText(path, "message\n");
                    model.AddDocument(path);
                }
                WaitFor(() => model.Documents.All(document => !document.LogViewModel.IsLoading));
                foreach (var document in model.Documents) document.MarkedLines.Add(0);
                Assert.AreEqual(2, model.MarkedLinesViewModel.MarkedLines.Count);

                var resources = LoadMarkedResources();
                resources.MergedDictionaries.Insert(0, ProfileColumnSizingTests.LoadAppResources());
                var host = new ContentControl
                {
                    Content = model.MarkedLinesViewModel,
                    ContentTemplate = (DataTemplate)resources[new DataTemplateKey(typeof(MarkedLinesViewModel))]
                };
                var window = CreateWindow(host);
                window.Resources.MergedDictionaries.Add(resources);
                try
                {
                    window.Show();
                    window.UpdateLayout();
                    var list = host.GetVisualChildren<MarkedLinesListView>().Single();
                    Assert.AreEqual(2, list.Items.Count);
                    Assert.AreEqual(2, list.Items.Groups!.Count);
                    CollectionAssert.AreEquivalent(model.Documents.ToArray(), list.Items.Groups.Cast<CollectionViewGroup>().Select(group => group.Name).ToArray());
                    model.Documents[0].MarkedLines.Remove(0);
                    window.UpdateLayout();
                    Assert.AreEqual(1, list.Items.Count);
                    Assert.AreEqual(1, list.Items.Groups.Count);
                }
                finally { window.Close(); }
            }
            finally
            {
                foreach (var path in Directory.GetFiles(directory)) File.Delete(path);
                Directory.Delete(directory);
            }
        });
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateWeakSearch(SavedSearchPatternStore store) =>
        new(new SearchViewModel(_ => throw new InvalidOperationException(), new SearchAutocompleteCache(), store,
            TestServices.Inline, TestServices.Dialogs));

    private static SavedSearchPattern Saved(string name) => SavedSearchPattern.FromSearchPattern(name, new SearchPattern(name, false, false));

    private static ResourceDictionary LoadMarkedResources()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
            "LogGrokX", "Styles", "MarkedLinesViewTemplate.xaml"));
        var root = XDocument.Load(path).Root!;
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        root.Elements(presentation + "DataTemplate").First().AddBeforeSelf(
            new XElement(presentation + "FontFamily", new XAttribute(x + "Key", "JetBrainsMono"), "Consolas"));
        foreach (var element in root.Descendants().Where(element => element.Name.NamespaceName.StartsWith("clr-namespace:LogGrokX", StringComparison.Ordinal)))
            element.Name = XName.Get(element.Name.LocalName, element.Name.NamespaceName + ";assembly=LogGrokX");
        foreach (var attribute in root.Descendants().Attributes().Where(attribute => attribute.Name.NamespaceName.StartsWith("clr-namespace:LogGrokX", StringComparison.Ordinal)).ToArray())
        {
            attribute.Parent!.SetAttributeValue(XName.Get(attribute.Name.LocalName, attribute.Name.NamespaceName + ";assembly=LogGrokX"), attribute.Value);
            attribute.Remove();
        }
        foreach (var attribute in root.Attributes().Where(attribute => attribute.IsNamespaceDeclaration &&
                     attribute.Value.StartsWith("clr-namespace:LogGrokX", StringComparison.Ordinal)))
            attribute.Value += ";assembly=LogGrokX";
        var context = new ParserContext { BaseUri = new Uri("pack://application:,,,/LogGrokX;component/Styles/MarkedLinesViewTemplate.xaml") };
        return (ResourceDictionary)XamlReader.Parse(root.ToString(), context);
    }

    private static Window CreateWindow(object content) => new()
    {
        Content = content, Width = 640, Height = 320, Left = -10000, Top = -10000,
        ShowActivated = false, ShowInTaskbar = false
    };

    private static void WaitFor(Func<bool> ready)
    {
        var started = DateTime.UtcNow;
        while (!ready())
        {
            if (DateTime.UtcNow - started > TimeSpan.FromSeconds(10)) Assert.Fail("Loading did not finish.");
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(10), DispatcherPriority.Background,
                (_, _) => frame.Continue = false, Dispatcher.CurrentDispatcher);
            Dispatcher.PushFrame(frame);
            timer.Stop();
        }
    }

    private static void RunOnSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception e) { error = e; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}
