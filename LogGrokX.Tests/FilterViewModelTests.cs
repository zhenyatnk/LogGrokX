#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using LogGrokX.Data;
using LogGrokX.Data.Index;
using LogGrokX.Filter;
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
            var filter = CreateFilter(indexer);
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
            var filter = CreateFilter(indexer);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

            CollectionAssert.AreEquivalent(new[] { "INFO", "info", "WARN" }, filter.Elements.Select(item => item.Name).ToArray());
        });
    }

    private static FilterViewModel CreateFilter(TestIndexer indexer)
    {
        var meta = new LogMetaInformation(new LogFormat { Regex = "^(?<Level>.*)", IndexedFields = ["Level"] });
        return new FilterViewModel("Level", new FilterSettings(indexer, meta), indexer, meta);
    }

    private sealed class TestIndexer(string[] values) : IComponentIndexer
    {
        public Action? ReadingSnapshot { get; set; }
        public event Action<int, string>? ComponentAdded;
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
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null)
            throw error;
    }
}
