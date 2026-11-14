#nullable enable
using System;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using LogGrokX.Controls;
using LogGrokX.Data;
using LogGrokX.Data.Virtualization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ListViewSelectionTests
{
    [TestMethod]
    public void HugeSelectionDoesNotFetchRowsUntilAnOperationNeedsThem()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var provider = new CountingProvider(16_579_938);
                var list = new VirtualList<int, string>(provider, index => $"line {index}");
                var view = new LogGrokX.Controls.ListControls.ListView { ItemsSource = list };
                provider.FetchCalls = 0;
                var selection = new Selection();
                selection.Set(1000);
                selection.AddRangeToValue(13_662_285);

                view.UpdateReadonlySelectedItems(selection.Snapshot(), selection.Count);

                var selected = view.ReadonlySelectedItems;
                Assert.IsNotNull(selected);
                Assert.AreEqual(0, provider.FetchCalls);
                CollectionAssert.AreEqual(new[] { "line 1000", "line 1001" }, selected.Take(2).ToArray());
                Assert.AreEqual(1, provider.FetchCalls);
            }
            catch (Exception exception)
            {
                error = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null)
            ExceptionDispatchInfo.Capture(error).Throw();
    }

    [TestMethod]
    public void SelectAllDoesNotFetchRowsOrUseWpfSelection()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var provider = new CountingProvider(16_579_938);
                var list = new VirtualList<int, string>(provider, index => $"line {index}");
                var view = new LogGrokX.Controls.ListControls.ListView
                {
                    ItemsSource = list,
                    ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(
                        typeof(LogGrokX.Controls.ListControls.VirtualizingStackPanel.VirtualizingStackPanel)))
                };
                var window = new Window
                {
                    Content = view,
                    Width = 400,
                    Height = 300,
                    Left = -10000,
                    Top = -10000,
                    ShowActivated = false,
                    ShowInTaskbar = false
                };
                try
                {
                    window.Show();
                    var panel = FindPanel(window);
                    Assert.IsNotNull(panel);
                    provider.FetchCalls = 0;

                    panel.SelectAll();

                    Assert.AreEqual(0, provider.FetchCalls);
                    Assert.IsNotNull(view.ReadonlySelectedItems);
                }
                finally
                {
                    window.Close();
                }
            }
            catch (Exception exception)
            {
                error = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null)
            ExceptionDispatchInfo.Capture(error).Throw();
    }

    [TestMethod]
    public void ResetDoesNotMaterializeSelectedRows()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                var originalProvider = new CountingProvider(300);
                var original = new VirtualList<int, ItemViewModel>(originalProvider, index => new TestLogItem(index));
                var collection = new GrowingLogLinesCollection(Array.Empty<ItemViewModel>(), original);
                var view = new LogGrokX.Controls.ListControls.ListView
                {
                    ItemsSource = collection,
                    ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(
                        typeof(LogGrokX.Controls.ListControls.VirtualizingStackPanel.VirtualizingStackPanel)))
                };
                var window = new Window
                {
                    Content = view,
                    Width = 400,
                    Height = 300,
                    Left = -10000,
                    Top = -10000,
                    ShowActivated = false,
                    ShowInTaskbar = false
                };
                try
                {
                    window.Show();
                    view.SelectAll();
                    Assert.AreEqual(300, view.SelectedItems.Count);
                    var replacementProvider = new CountingProvider(300);
                    var replacement = new VirtualList<int, ItemViewModel>(replacementProvider, index => new TestLogItem(index));

                    collection.Reset(Array.Empty<ItemViewModel>(), replacement);

                    Assert.IsTrue(replacementProvider.FetchCalls <= 1,
                        $"Reset materialized {replacementProvider.FetchCalls} selected rows instead of only the current row.");
                    Assert.AreEqual(0, view.SelectedItems.Count);
                }
                finally
                {
                    window.Close();
                    Dispatcher.CurrentDispatcher.InvokeShutdown();
                }
            }
            catch (Exception exception)
            {
                error = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null)
            ExceptionDispatchInfo.Capture(error).Throw();
    }

    [TestMethod]
    public void DataContextOwnerCancelsListOperationWhenDocumentCloses()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var operations = new BulkOperationService(new LogGrokX.Platform.WpfUiDispatcher(Dispatcher.CurrentDispatcher));
                var owner = new object();
                var view = new OperationListView { DataContext = owner, BulkOperations = operations };
                var token = view.BeginOperation();

                operations.Cancel(view);
                Assert.IsTrue(operations.IsActive);
                Assert.IsFalse(token.IsCancellationRequested);

                operations.Cancel(owner);
                Assert.IsFalse(operations.IsActive);
                Assert.IsTrue(token.IsCancellationRequested);
            }
            catch (Exception exception)
            {
                error = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null)
            ExceptionDispatchInfo.Capture(error).Throw();
    }

    [TestMethod]
    public void SelectionChangesAndUnloadingDoNotCancelOperation()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var operations = new BulkOperationService(new LogGrokX.Platform.WpfUiDispatcher(Dispatcher.CurrentDispatcher));
                var view = new LogGrokX.Controls.ListControls.ListView { BulkOperations = operations };
                var token = operations.Begin("Copy", 1000, view);

                view.UpdateReadonlySelectedItems(new[] { 0 }, 1);
                view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));

                Assert.IsTrue(operations.IsActive);
                Assert.IsFalse(token.IsCancellationRequested);
                operations.CancelCommand.Execute(null);
                Assert.IsFalse(operations.IsActive);
                Assert.IsTrue(token.IsCancellationRequested);
            }
            catch (Exception exception)
            {
                error = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null)
            ExceptionDispatchInfo.Capture(error).Throw();
    }

    [TestMethod]
    public void ToggleMarksKeepsProgressActiveAfterFirstBatch()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                SynchronizationContext.SetSynchronizationContext(
                    new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                var operations = new BulkOperationService(new LogGrokX.Platform.WpfUiDispatcher(Dispatcher.CurrentDispatcher));
                var markedLines = new Selection();
                var parser = new RegexBasedLineParser(new LogMetaInformation(new LogFormat { Regex = "^.*$" }));
                var transformation = new TransformationPerformer(Array.Empty<string>());
                var items = Enumerable.Range(0, 300).Select(index =>
                    new LineViewModel(index, $"line {index}", parser, markedLines, transformation,
                        TestServices.DisplaySettings)).ToArray();
                var view = new LogGrokX.Controls.ListControls.ListView
                {
                    ItemsSource = items,
                    BulkOperations = operations,
                    ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(
                        typeof(LogGrokX.Controls.ListControls.VirtualizingStackPanel.VirtualizingStackPanel)))
                };
                var window = new Window
                {
                    Content = view,
                    Width = 400,
                    Height = 300,
                    Left = -10000,
                    Top = -10000,
                    ShowActivated = false,
                    ShowInTaskbar = false
                };
                try
                {
                    window.Show();
                    var panel = FindPanel(window);
                    Assert.IsNotNull(panel);
                    panel.SelectAll();

                    RoutedCommands.ToggleMarks.Execute(null, view);

                    Assert.IsTrue(operations.IsActive);
                    Assert.AreEqual(300, operations.Total);
                    PumpUntil(() => !operations.IsActive, TimeSpan.FromSeconds(5));
                    Assert.AreEqual(300, markedLines.Count);
                }
                finally
                {
                    window.Close();
                }
            }
            catch (Exception exception)
            {
                error = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null)
            ExceptionDispatchInfo.Capture(error).Throw();
    }

    [TestMethod]
    public void CancellingToggleMarksRestoresChangesAndAllowsCancellingRollback()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                SynchronizationContext.SetSynchronizationContext(
                    new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                var scenarios = new[]
                {
                    (InitiallyMarked: false, CancelRollback: false, Expected: 0),
                    (InitiallyMarked: false, CancelRollback: true, Expected: 128),
                    (InitiallyMarked: true, CancelRollback: false, Expected: 300),
                    (InitiallyMarked: true, CancelRollback: true, Expected: 172)
                };
                foreach (var scenario in scenarios)
                {
                    var operations = new BulkOperationService(new LogGrokX.Platform.WpfUiDispatcher(Dispatcher.CurrentDispatcher));
                    var markedLines = new Selection();
                    var documentSelections = scenario.CancelRollback
                        ? Enumerable.Range(0, 300).Select(_ => new Selection()).ToArray()
                        : [markedLines];
                    if (scenario.InitiallyMarked)
                    {
                        if (scenario.CancelRollback)
                            for (var index = 0; index < documentSelections.Length; index++)
                                documentSelections[index].Set(index);
                        else
                            markedLines.SetRange(0, 299);
                    }
                    long MarkedCount() => documentSelections.Sum(selection => selection.Count);
                    var parser = new RegexBasedLineParser(new LogMetaInformation(new LogFormat { Regex = "^.*$" }));
                    var transformation = new TransformationPerformer(Array.Empty<string>());
                    var items = Enumerable.Range(0, 300).Select(index =>
                        new LineViewModel(index, $"line {index}", parser, scenario.CancelRollback ? documentSelections[index] : markedLines, transformation,
                            TestServices.DisplaySettings)).ToArray();
                    var view = new LogGrokX.Controls.ListControls.ListView
                    {
                        ItemsSource = items,
                        BulkOperations = operations,
                        ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(
                            typeof(LogGrokX.Controls.ListControls.VirtualizingStackPanel.VirtualizingStackPanel)))
                    };
                    var window = new Window
                    {
                        Content = view,
                        Width = 400,
                        Height = 300,
                        Left = -10000,
                        Top = -10000,
                        ShowActivated = false,
                        ShowInTaskbar = false
                    };
                    try
                    {
                        window.Show();
                        var panel = FindPanel(window);
                        Assert.IsNotNull(panel);
                        panel.SelectAll();
                        if (scenario.InitiallyMarked)
                        {
                            var unmarked = items.FirstOrDefault(item => !item.IsMarked);
                            Assert.IsNull(unmarked, unmarked is null ? string.Empty :
                                $"index={unmarked.Index}, selectionContains={documentSelections[unmarked.Index].Contains(unmarked.Index)}");
                        }
                        var forwardCancelled = false;
                        operations.PropertyChanged += (_, args) =>
                        {
                            if (args.PropertyName != nameof(BulkOperationService.Processed))
                                return;
                            if (!forwardCancelled && operations.Title == LogGrokX.Localization.Strings.Get("ToggleMarks") &&
                                operations.Processed >= 256 && (!scenario.InitiallyMarked || MarkedCount() <= 44))
                            {
                                forwardCancelled = true;
                                operations.CancelCommand.Execute(null);
                            }
                            if (scenario.CancelRollback && operations.Title == LogGrokX.Localization.Strings.Get("RestoreMarks") &&
                                operations.Processed >= 128)
                                operations.CancelCommand.Execute(null);
                        };
                        RoutedCommands.ToggleMarks.Execute(null, view);
                        PumpUntil(() => forwardCancelled, TimeSpan.FromSeconds(5));

                        if (scenario.CancelRollback)
                        {
                            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
                        }
                        else
                        {
                            PumpUntil(() => !operations.IsActive && MarkedCount() == scenario.Expected,
                                TimeSpan.FromSeconds(5));
                        }

                        Assert.AreEqual(scenario.Expected, MarkedCount(),
                            $"initiallyMarked={scenario.InitiallyMarked}, cancelRollback={scenario.CancelRollback}");
                        Assert.IsFalse(operations.IsActive);
                    }
                    finally
                    {
                        window.Close();
                    }
                }
            }
            catch (Exception exception)
            {
                error = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null)
            ExceptionDispatchInfo.Capture(error).Throw();
    }

    private static void PumpUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                Assert.Fail("Timed out waiting for the bulk operation.");
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Thread.Sleep(1);
        }
    }

    private static LogGrokX.Controls.ListControls.VirtualizingStackPanel.VirtualizingStackPanel? FindPanel(
        DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is LogGrokX.Controls.ListControls.VirtualizingStackPanel.VirtualizingStackPanel panel)
                return panel;
            if (FindPanel(child) is { } nested)
                return nested;
        }
        return null;
    }

    private sealed class TestLogItem(int index) : ItemViewModel
    {
        public override string ToString() => $"line {index}";
    }

    private sealed class OperationListView : LogGrokX.Controls.ListControls.ListView
    {
        public CancellationToken BeginOperation() => BeginSelectionOperation("Copy");
    }

    private sealed class CountingProvider(int count) : IItemProvider<int>
    {
        public int FetchCalls { get; set; }

        public int Count => count;

        public void Fetch(int start, Span<int> values)
        {
            FetchCalls++;
            for (var index = 0; index < values.Length; index++)
                values[index] = start + index;
        }
    }
}
