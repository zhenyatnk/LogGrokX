#nullable enable

using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Tests;

[TestClass]
public class GrowingLogLinesCollectionTests
{
    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(0)]
    public void ResetUpdatesRealizedRows(int remainingCount)
    {
        RunOnSta(() =>
        {
            ItemViewModel[] original = [new TestItem("excluded"), new TestItem("kept")];
            var collection = new GrowingLogLinesCollection(Array.Empty<ItemViewModel>(), original);
            var text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetBinding(TextBlock.TextProperty, new Binding("."));
            var list = new ListBox
            {
                ItemsSource = collection,
                ItemTemplate = new DataTemplate { VisualTree = text },
                ItemsPanel = new ItemsPanelTemplate { VisualTree = new FrameworkElementFactory(typeof(StackPanel)) }
            };
            var window = new Window
            {
                Content = list,
                Width = 400,
                Height = 200,
                Left = -10000,
                Top = -10000,
                ShowActivated = false,
                ShowInTaskbar = false
            };
            try
            {
                window.Show();
                FlushLayout(window);
                Assert.AreSame(original[0], ((ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0)).Content);
                var replacement = Enumerable.Range(0, remainingCount)
                    .Select(i => (ItemViewModel)new TestItem($"replacement {i}")).ToArray();

                collection.Reset(Array.Empty<ItemViewModel>(), replacement);
                FlushLayout(window);

                Assert.AreEqual(remainingCount, list.Items.Count);
                for (var i = 0; i < remainingCount; i++)
                    Assert.AreSame(replacement[i], ((ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(i)).Content);
                Assert.IsNull(list.ItemContainerGenerator.ContainerFromIndex(remainingCount));
            }
            finally
            {
                window.Close();
            }
        });
    }

    private sealed class TestItem(string text) : ItemViewModel
    {
        public override string ToString() => text;
    }

    private static void FlushLayout(FrameworkElement element)
    {
        element.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        element.UpdateLayout();
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
