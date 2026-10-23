#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LogGrokX.Controls.GridView;
using LogGrokX.Data;
using LogGrokX.Data.Index;
using LogGrokX.Filter;
using LogGrokX.Search;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Tests;

[TestClass]
public class FilterViewModelTests
{
    [TestMethod]
    public void ValuesReportedBySeveralSourcesAppearOnlyOnce()
    {
        RunOnSta(() =>
        {
            var indexer = new TestIndexer(["ERRO", "ERRO"]);
            using var filter = CreateFilter(indexer);
            Parallel.For(0, 30, i => indexer.Publish(i % 2 == 0 ? "ERRO" : "INFO"));
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

            CollectionAssert.AreEquivalent(new[] { "ERRO", "INFO" }, filter.Elements.Select(item => item.Name).ToArray());
            filter.Elements.Single(item => item.Name == "ERRO").IsActive = false;
            Assert.IsTrue(filter.IsFilterApplied);
        });
    }

    [TestMethod]
    public void ValuesAddedWhileReadingSnapshotAreNotLostOrDuplicated()
    {
        RunOnSta(() =>
        {
            var indexer = new TestIndexer(["INFO", "info"]);
            indexer.ReadingSnapshot = () =>
            {
                indexer.Publish("INFO");
                indexer.Publish("WARN");
            };
            using var filter = CreateFilter(indexer);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

            CollectionAssert.AreEquivalent(new[] { "INFO", "info", "WARN" }, filter.Elements.Select(item => item.Name).ToArray());
        });
    }

    private static FilterViewModel CreateFilter(TestIndexer indexer)
    {
        var meta = new LogMetaInformation(new LogFormat { Regex = "^(?<Level>.*)", IndexedFields = ["Level"] });
        return new FilterViewModel("Level", new FilterSettings(indexer, meta), indexer, meta, Dispatcher.CurrentDispatcher);
    }

    [TestMethod]
    public void DisposingFilterRemovesSubscriptionsAndCancelsPendingUpdates()
    {
        RunOnSta(() =>
        {
            var indexer = new TestIndexer(["INFO"]);
            var meta = new LogMetaInformation(new LogFormat { Regex = "^(?<Level>.*)", IndexedFields = ["Level"] });
            var settings = new FilterSettings(indexer, meta);
            var filter = new FilterViewModel("Level", settings, indexer, meta, Dispatcher.CurrentDispatcher);
            var filterChanges = 0;
            var elementChanges = 0;
            filter.PropertyChanged += (_, _) => filterChanges++;
            filter.Elements[0].PropertyChanged += (_, _) => elementChanges++;
            indexer.Publish("WARN");
            filter.Dispose();
            filter.Dispose();
            Parallel.For(0, 30, _ => indexer.Publish("ERRO"));
            settings.AddExclusions(0, ["INFO"]);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

            Assert.AreEqual(0, indexer.SubscriberCount);
            CollectionAssert.AreEqual(new[] { "INFO" }, filter.Elements.Select(item => item.Name).ToArray());
            Assert.AreEqual(0, filterChanges);
            Assert.AreEqual(0, elementChanges);
        });
    }

    [TestMethod]
    public void RecreatedViewsReuseFiltersAndFactoryDisposalReleasesThem()
    {
        RunOnSta(() =>
        {
            var indexer = new TestIndexer(["INFO"]);
            var meta = new LogMetaInformation(new LogFormat { Regex = "^(?<Level>.*)", IndexedFields = ["Level"] });
            var settings = new FilterSettings(indexer, meta);
            using var factory = new GridViewFactory(meta, true,
                (field, dispatcher) => new FilterViewModel(field, settings, indexer, meta, dispatcher));
            FilterViewModel GetFilter()
            {
                var view = (GridView)factory.CreateView(null);
                var template = view.Columns[^1].HeaderTemplate;
                template.Seal();
                return ((HeaderViewModel)((FrameworkElement)template.LoadContent()).DataContext).FilterViewModel!;
            }

            var first = GetFilter();
            for (var i = 0; i < 20; i++)
                Assert.AreSame(first, GetFilter());
            Assert.AreEqual(1, indexer.SubscriberCount);
            factory.Dispose();
            Assert.AreEqual(0, indexer.SubscriberCount);
        });
    }

    [TestMethod]
    public void FilterRequiresItsProvidedDispatcherThread()
    {
        RunOnSta(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            var indexer = new TestIndexer(["INFO"]);
            var meta = new LogMetaInformation(new LogFormat { Regex = "^(?<Level>.*)", IndexedFields = ["Level"] });
            var settings = new FilterSettings(indexer, meta);
            Task.Run(() =>
            {
                Assert.IsNull(Dispatcher.FromThread(Thread.CurrentThread));
                Assert.ThrowsExactly<InvalidOperationException>(() =>
                    new FilterViewModel("Level", settings, indexer, meta, dispatcher));
                Assert.IsNull(Dispatcher.FromThread(Thread.CurrentThread));
            }).GetAwaiter().GetResult();
            Assert.AreEqual(0, indexer.SubscriberCount);
        });
    }

    [TestMethod]
    public void ClosingDocumentDisposesItsHeaderFilters()
    {
        RunOnSta(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), $"loggrokx-filter-lifetime-{Guid.NewGuid():N}.log");
            File.WriteAllText(path, "INFO\n");
            DocumentViewModel? document = null;
            try
            {
                var settings = new ApplicationSettings();
                var profile = new ProfileSettings
                {
                    Name = "test", InheritLegacySettings = false,
                    LogFormats = [new LogFormat { Regex = "^(?<Level>.*)", IndexedFields = ["Level"] }]
                };
                using var container = new DocumentContainer(path, profile, settings, new SearchAutocompleteCache(),
                    new SavedSearchPatternStore(), new TimelinePlacementService(settings), new ThreadGroupingService(settings));
                document = container.GetDocumentViewModel();
                var view = (GridView)document.LogViewModel.CustomView;
                var template = view.Columns[^1].HeaderTemplate;
                template.Seal();
                var filter = ((HeaderViewModel)((FrameworkElement)template.LoadContent()).DataContext).FilterViewModel!;
                var changes = 0;
                filter.PropertyChanged += (_, _) => changes++;
                filter.SelectAllCommand.Execute(null);
                Assert.AreEqual(1, changes);
                container.Dispose();
                filter.SelectAllCommand.Execute(null);
                Assert.AreEqual(1, changes);
            }
            finally
            {
                document?.CloseFile();
                File.Delete(path);
            }
        });
    }

    private sealed class TestIndexer(string[] values) : IComponentIndexer
    {
        public Action? ReadingSnapshot { get; set; }
        public event Action<int, string>? ComponentAdded;
        public int SubscriberCount => ComponentAdded?.GetInvocationList().Length ?? 0;
        public void Publish(string value) => ComponentAdded?.Invoke(0, value);
        public IEnumerable<string> GetAllComponents(int componentNumber)
        {
            ReadingSnapshot?.Invoke();
            return values;
        }
        public int GetIndexCountForComponent(int componentIndex, string componentValue) => 1;
    }

    private static void RunOnSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { error = exception; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null)
            throw error;
    }
}
