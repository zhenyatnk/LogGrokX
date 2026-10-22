#nullable enable

using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using AvalonDock;
using AvalonDock.Layout;
using LogGrokX.AvalonDockExtensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Tests;

[TestClass]
public class DocumentReplacementLayoutTests
{
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void ReplacingDocumentsPreservesTabOrderAndActiveContent(bool mergedActive)
    {
        RunOnSta(() =>
        {
            var sources = new ObservableCollection<object> { new(), new(), new() };
            var selection = new TestSelection { Current = sources[2] };
            var pane = new LayoutDocumentPane();
            var manager = new DockingManager { Layout = new LayoutRoot { RootPanel = new LayoutPanel(pane) } };
            var text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetBinding(TextBlock.TextProperty, new Binding("."));
            BindingBehavior.SetDocumentViewTemplate(manager, new DataTemplate { VisualTree = text });
            BindingBehavior.SetDocumentsSource(manager, sources);
            manager.SetBinding(BindingBehavior.CurrentDocumentProperty,
                new Binding(nameof(TestSelection.Current)) { Source = selection, Mode = BindingMode.TwoWay });
            var merged = new LayoutDocument { Content = new ContentControl(), Title = "Merged" };
            pane.Children.Add(merged);
            pane.Children.Move(0, 1);
            var active = mergedActive ? merged.Content : pane.Children.Single(document =>
                document.Content is ContentControl view && ReferenceEquals(view.Content, sources[2])).Content;
            manager.ActiveContent = active;
            var originalTabs = pane.Children.ToArray();

            for (var i = 0; i < sources.Count; i++)
                sources[i] = new object();
            selection.Current = sources[2];
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

            CollectionAssert.AreEqual(originalTabs, pane.Children.ToArray());
            Assert.AreSame(active, manager.ActiveContent);
            Assert.AreSame(sources[2], manager.GetValue(BindingBehavior.CurrentDocumentProperty));
            Assert.AreSame(sources[2], selection.Current);
            foreach (var source in sources)
                Assert.AreEqual(1, pane.Children.Count(document => document.Content is ContentControl view && ReferenceEquals(view.Content, source)));
        });
    }

    private sealed class TestSelection : ViewModelBase
    {
        private object? _current;
        public object? Current
        {
            get => _current;
            set => SetAndRaiseIfChanged(ref _current, value);
        }
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
