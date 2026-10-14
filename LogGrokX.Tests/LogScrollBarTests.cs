#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using LogListView = LogGrokX.Controls.ListControls.ListView;
using LogPanel = LogGrokX.Controls.ListControls.VirtualizingStackPanel.VirtualizingStackPanel;

namespace LogGrokX.Tests;

[TestClass]
public sealed class LogScrollBarTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ThumbSizeStaysStableAcrossMultilineRecords(bool useDefaultTemplate)
    {
        RunOnSta(() =>
        {
            var rows = CreateRows(90);
            foreach (var row in rows.Skip(30).Take(30))
                row.Height = 200;

            WithList(rows, (window, _, viewer, panel, track) =>
            {
                var thumbHeight = track.Thumb.ActualHeight;
                var viewport = viewer.ViewportHeight;
                Assert.IsGreaterThan(32.0, thumbHeight);

                panel.SetVerticalOffset(35);
                FlushLayout(window);
                Assert.IsLessThan(viewport, viewer.ViewportHeight);
                Assert.AreEqual(thumbHeight, track.Thumb.ActualHeight, 0.001);

                var offset = panel.VerticalOffset;
                ScrollBar.PageDownCommand.Execute(null, (ScrollBar)track.TemplatedParent);
                FlushLayout(window);
                Assert.IsGreaterThan(offset, panel.VerticalOffset);
                Assert.AreEqual(thumbHeight, track.Thumb.ActualHeight, 0.001);
                panel.PageUp();
                FlushLayout(window);
                Assert.AreEqual(offset, panel.VerticalOffset, 0.001);

                panel.MouseWheelDown();
                FlushLayout(window);
                Assert.IsGreaterThan(offset, panel.VerticalOffset);
                Assert.AreEqual(thumbHeight, track.Thumb.ActualHeight, 0.001);

                viewer.ScrollToTop();
                FlushLayout(window);
                Assert.AreEqual(0.0, viewer.VerticalOffset);
                Assert.AreEqual(thumbHeight, track.Thumb.ActualHeight, 0.001);

                viewer.ScrollToEnd();
                FlushLayout(window);
                Assert.AreEqual(viewer.ScrollableHeight, viewer.VerticalOffset, 0.001);
                Assert.IsTrue(Descendants<ListViewItem>(panel)
                    .Any(item => item.Visibility == Visibility.Visible && ReferenceEquals(item.Content, rows[^1])));
                Assert.AreEqual(thumbHeight, track.Thumb.ActualHeight, 0.001);
            }, useDefaultTemplate);
        });
    }

    [TestMethod]
    public void ExpandingAndCollapsingVisibleRecordsKeepsThumbSizeStable()
    {
        RunOnSta(() =>
        {
            var rows = CreateRows(90);
            WithList(rows, (window, _, viewer, _, track) =>
            {
                var thumbHeight = track.Thumb.ActualHeight;
                var viewport = viewer.ViewportHeight;
                rows[0].Height = 400;
                FlushLayout(window);
                Assert.IsLessThan(viewport, viewer.ViewportHeight);
                Assert.AreEqual(thumbHeight, track.Thumb.ActualHeight, 0.001);

                rows[0].Height = 20;
                FlushLayout(window);
                Assert.AreEqual(viewport, viewer.ViewportHeight, 0.001);
                Assert.AreEqual(thumbHeight, track.Thumb.ActualHeight, 0.001);
            });
        });
    }

    [TestMethod]
    public void ThumbSizeRespondsToWindowFontAndRecordCountChanges()
    {
        RunOnSta(() =>
        {
            var rows = CreateRows(90);
            WithList(rows, (window, list, _, _, track) =>
            {
                var thumbHeight = track.Thumb.ActualHeight;
                window.Height = 640;
                FlushLayout(window);
                Assert.IsGreaterThan(thumbHeight, track.Thumb.ActualHeight);

                thumbHeight = track.Thumb.ActualHeight;
                list.FontSize = 24;
                FlushLayout(window);
                Assert.IsLessThan(thumbHeight, track.Thumb.ActualHeight);

                thumbHeight = track.Thumb.ActualHeight;
                foreach (var row in CreateRows(60))
                    rows.Add(row);
                FlushLayout(window);
                Assert.IsLessThan(thumbHeight, track.Thumb.ActualHeight);
            });
        });
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(10)]
    public void SmallLogsWithTallRecordsKeepThumbAndEndNavigationUsable(int count)
    {
        RunOnSta(() =>
        {
            var rows = CreateRows(count);
            foreach (var row in rows)
                row.Height = 2000;
            WithList(rows, (window, _, viewer, panel, track) =>
            {
                var thumbHeight = track.Thumb.ActualHeight;
                Assert.IsGreaterThanOrEqualTo(32.0, thumbHeight);
                Assert.IsLessThan(track.ActualHeight, thumbHeight);
                Assert.IsGreaterThan(0.0, track.ValueFromDistance(0, 10));

                viewer.ScrollToEnd();
                FlushLayout(window);
                Assert.AreEqual(viewer.ScrollableHeight, viewer.VerticalOffset, 0.001);
                var last = Descendants<ListViewItem>(panel)
                    .Single(item => item.Visibility == Visibility.Visible && ReferenceEquals(item.Content, rows[^1]));
                Assert.AreEqual(panel.ActualHeight,
                    last.TranslatePoint(new Point(0, last.ActualHeight), panel).Y, 1.001);
                Assert.AreEqual(thumbHeight, track.Thumb.ActualHeight, 0.001);
            });
        });
    }

    private static ObservableCollection<Border> CreateRows(int count) =>
        new(Enumerable.Range(0, count).Select(_ => new Border { Height = 20 }));

    private static void WithList(ObservableCollection<Border> rows,
        Action<Window, LogListView, ScrollViewer, LogPanel, Track> action, bool useDefaultTemplate = false)
    {
        var resources = new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/LogGrokX;component/Styles/ScrollBarStyles.xaml")
        };
        var presenter = new FrameworkElementFactory(typeof(ItemsPresenter));
        var scrollViewer = new FrameworkElementFactory(typeof(ScrollViewer));
        scrollViewer.SetValue(FrameworkElement.StyleProperty, resources["AppScrollViewerStyle"]);
        scrollViewer.SetValue(ScrollViewer.CanContentScrollProperty, true);
        scrollViewer.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        scrollViewer.AppendChild(presenter);
        var itemStyle = new Style(typeof(ListViewItem));
        itemStyle.Setters.Add(new Setter(FrameworkElement.HeightProperty,
            new Binding("Content.Height") { RelativeSource = new RelativeSource(RelativeSourceMode.Self) }));
        itemStyle.Setters.Add(new Setter(Control.TemplateProperty,
            new ControlTemplate(typeof(ListViewItem)) { VisualTree = new FrameworkElementFactory(typeof(Border)) }));
        var list = new LogListView
        {
            ItemsSource = rows,
            FontSize = 12,
            View = new GridView(),
            ItemContainerStyle = itemStyle,
            ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(LogPanel))),
            Template = new ControlTemplate(typeof(LogListView)) { VisualTree = scrollViewer }
        };
        if (useDefaultTemplate)
        {
            list.ClearValue(Control.TemplateProperty);
            list.Resources[typeof(ScrollBar)] = resources["AppScrollBarStyle"];
        }
        var window = new Window
        {
            Content = list, Width = 320, Height = 480, Left = -10000, Top = -10000,
            ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None,
            WindowStartupLocation = WindowStartupLocation.Manual
        };
        try
        {
            window.Show();
            FlushLayout(window);
            var panel = Descendants<LogPanel>(list).Single();
            var viewer = panel.ScrollOwner!;
            var bar = (ScrollBar)viewer.Template.FindName("PART_VerticalScrollBar", viewer);
            var track = (Track)bar.Template.FindName("PART_Track", bar);
            action(window, list, viewer, panel, track);
        }
        finally
        {
            window.Close();
        }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
                yield return match;
            foreach (var nested in Descendants<T>(child))
                yield return nested;
        }
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
