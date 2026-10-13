#nullable enable

using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Tests;

[TestClass]
public sealed class ScrollViewerStyleTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ScrollBarsReserveSpaceAndKeepPageNavigationWorking(bool canContentScroll)
    {
        RunOnSta(() =>
        {
            var content = new Border { Width = 2000, Height = 4000 };
            var viewer = CreateViewer(content, canContentScroll);
            var presenter = (ScrollContentPresenter)viewer.Template.FindName("PART_ScrollContentPresenter", viewer);
            var vertical = (ScrollBar)viewer.Template.FindName("PART_VerticalScrollBar", viewer);
            var horizontal = (ScrollBar)viewer.Template.FindName("PART_HorizontalScrollBar", viewer);

            Assert.AreEqual(16.0, vertical.ActualWidth);
            Assert.AreEqual(16.0, horizontal.ActualHeight);
            Assert.IsTrue(presenter.ActualWidth + vertical.ActualWidth <= viewer.ActualWidth);
            Assert.IsTrue(presenter.ActualHeight + horizontal.ActualHeight <= viewer.ActualHeight);
            Assert.AreEqual(canContentScroll, presenter.CanContentScroll);

            ScrollBar.PageDownCommand.Execute(null, vertical);
            FlushLayout(viewer);
            Assert.IsGreaterThan(0.0, viewer.VerticalOffset);
            ScrollBar.PageRightCommand.Execute(null, horizontal);
            FlushLayout(viewer);
            Assert.IsGreaterThan(0.0, viewer.HorizontalOffset);

            viewer.ScrollToVerticalOffset(viewer.ScrollableHeight);
            viewer.ScrollToHorizontalOffset(viewer.ScrollableWidth);
            FlushLayout(viewer);
            Assert.AreEqual(viewer.ScrollableHeight, viewer.VerticalOffset);
            Assert.AreEqual(viewer.ScrollableWidth, viewer.HorizontalOffset);

            content.Width = 80;
            content.Height = 80;
            FlushLayout(viewer);
            Assert.AreEqual(Visibility.Collapsed, viewer.ComputedVerticalScrollBarVisibility);
            Assert.AreEqual(Visibility.Collapsed, viewer.ComputedHorizontalScrollBarVisibility);
            Assert.AreEqual(viewer.ActualWidth, viewer.ViewportWidth);
            Assert.AreEqual(viewer.ActualHeight, viewer.ViewportHeight);
        });
    }

    [TestMethod]
    public void LargeContentKeepsBothThumbsEasyToGrab()
    {
        RunOnSta(() =>
        {
            var viewer = CreateViewer(new Border { Width = 1000000, Height = 1000000 }, false);
            var vertical = (ScrollBar)viewer.Template.FindName("PART_VerticalScrollBar", viewer);
            var horizontal = (ScrollBar)viewer.Template.FindName("PART_HorizontalScrollBar", viewer);
            var verticalTrack = (Track)vertical.Template.FindName("PART_Track", vertical);
            var horizontalTrack = (Track)horizontal.Template.FindName("PART_Track", horizontal);

            Assert.IsGreaterThanOrEqualTo(32.0, verticalTrack.Thumb.ActualHeight);
            Assert.IsGreaterThanOrEqualTo(32.0, horizontalTrack.Thumb.ActualWidth);
            Assert.IsGreaterThan(0.0, verticalTrack.ValueFromDistance(0, 10));
            Assert.IsGreaterThan(0.0, horizontalTrack.ValueFromDistance(10, 0));
        });
    }

    private static ScrollViewer CreateViewer(UIElement content, bool canContentScroll)
    {
        var resources = new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/LogGrokX;component/Styles/ScrollBarStyles.xaml")
        };
        var viewer = new ScrollViewer
        {
            Content = content,
            Style = (Style)resources["AppScrollViewerStyle"],
            CanContentScroll = canContentScroll,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        viewer.Measure(new Size(300, 240));
        viewer.Arrange(new Rect(0, 0, 300, 240));
        FlushLayout(viewer);
        return viewer;
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
