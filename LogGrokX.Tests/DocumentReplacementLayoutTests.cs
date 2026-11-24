#nullable enable

using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using AvalonDock;
using AvalonDock.Layout;
using LogGrokX.AvalonDockExtensions;
using LogGrokX.Colors;
using LogGrokX.Controls.ListControls;
using LogGrokX.Platform.Colors;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MediaColors = System.Windows.Media.Colors;

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

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DetachedDocumentViewsKeepColorRulesAndGrayRows(bool useTemplateSelector)
    {
        RunOnSta(() =>
        {
            var sources = new ObservableCollection<object> { new() };
            var manager = new DockingManager();
            var template = new DataTemplate();
            if (useTemplateSelector)
                BindingBehavior.SetDocumentViewTemplateSelector(manager, new TestTemplateSelector(template));
            else
                BindingBehavior.SetDocumentViewTemplate(manager, template);
            BindingBehavior.SetDocumentsSource(manager, sources);
            var view = manager.DocumentsSource.Cast<ContentControl>().Single();
            var row = new BaseLogListViewItem(new LogGrokX.Controls.ListControls.ListView())
            {
                Content = "10:00:00.000\t0x01\tDBG\tComponent\tMessage"
            };
            view.Content = row;
            view.ContentTemplate = null;
            var window = new Window
            {
                Content = view, Width = 400, Height = 100, Left = -10000, Top = -10000,
                ShowInTaskbar = false, ShowActivated = false
            };
            try
            {
                var rules = CreateDebugColorRules(nameof(MediaColors.Gray));
                ColorSettings.SetColorSettings(manager, rules);
                window.Show();
                PumpBindings();

                Assert.AreSame(rules, ColorSettings.GetColorSettings(view));
                Assert.AreEqual(MediaColors.Gray, ((SolidColorBrush)row.Foreground).Color);

                var updatedRules = CreateDebugColorRules(nameof(MediaColors.DarkGray));
                ColorSettings.SetColorSettings(manager, updatedRules);
                PumpBindings();

                Assert.AreSame(updatedRules, ColorSettings.GetColorSettings(view));
                Assert.AreEqual(MediaColors.DarkGray, ((SolidColorBrush)row.Foreground).Color);

                var restoredRules = CreateDebugColorRules(nameof(MediaColors.Gray));
                ColorSettings.SetColorSettings(manager, restoredRules);
                PumpBindings();

                Assert.AreSame(restoredRules, ColorSettings.GetColorSettings(view));
                Assert.AreEqual(MediaColors.Gray, ((SolidColorBrush)row.Foreground).Color);
            }
            finally { window.Close(); }
        });
    }

    private static ColorRuleSet CreateDebugColorRules(string foregroundColor) => new(
        new LogGrokX.Platform.Colors.Configuration.ColorSettings
        {
            Rules = [new LogGrokX.Platform.Colors.Configuration.ColorRule
            {
                RegexString = @"\tDBG\t", ForegroundColor = foregroundColor
            }]
        }, new LogGrokX.Platform.WpfColorValidator());

    private sealed class TestTemplateSelector(DataTemplate template) : DataTemplateSelector
    {
        public override DataTemplate SelectTemplate(object item, DependencyObject container) => template;
    }

    private static void PumpBindings()
    {
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
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
