#nullable enable

using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using LogGrokX.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using Wpf.Ui.Markup;

namespace LogGrokX.Tests;

[TestClass]
[DoNotParallelize]
public class TitleBarHoverTests
{
    private const int WmNcHitTest = 0x0084;
    private const int WmNcLeftButtonDown = 0x00A1;
    private const int WmNcLeftButtonUp = 0x00A2;
    private const int WmNcMouseLeave = 0x02A2;

    [TestMethod]
    [DataRow(ApplicationTheme.Light)]
    [DataRow(ApplicationTheme.Dark)]
    public void SpuriousLeavePreservesHoverAndPendingClick(ApplicationTheme theme)
    {
        RunOnSta(() => WithTitleBar(theme, (window, titleBar, close) =>
        {
            var position = Center(close);
            titleBar.CursorPosition = position;
            var handle = new WindowInteropHelper(window).Handle;
            var point = Pack(position);
            var clicks = 0;
            var keepOpen = true;
            window.Closing += (_, e) => e.Cancel = keepOpen;
            close.Click += (_, _) => clicks++;
            try
            {
                Assert.AreEqual(new IntPtr(20), SendMessage(handle, WmNcHitTest, IntPtr.Zero, point));
                SendMessage(handle, WmNcLeftButtonDown, new IntPtr(20), point);
                var background = close.Background;
                var foreground = close.RenderButtonsForeground;

                for (var i = 0; i < 10; i++)
                {
                    SendMessage(handle, WmNcMouseLeave, IntPtr.Zero, IntPtr.Zero);
                    Assert.IsTrue(close.IsHovered);
                    Assert.AreSame(background, close.Background);
                    Assert.AreSame(foreground, close.RenderButtonsForeground);
                }

                SendMessage(handle, WmNcLeftButtonUp, new IntPtr(20), point);
                FlushDispatcher();
                Assert.AreEqual(1, clicks);
            }
            finally
            {
                keepOpen = false;
            }
        }));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void RealLeaveClearsHoverAndCancelsPendingClick(bool cursorUnavailable)
    {
        RunOnSta(() => WithTitleBar(ApplicationTheme.Light, (window, titleBar, close) =>
        {
            var position = Center(close);
            var handle = new WindowInteropHelper(window).Handle;
            var point = Pack(position);
            var clicks = 0;
            close.Click += (_, _) => clicks++;
            SendMessage(handle, WmNcHitTest, IntPtr.Zero, point);
            SendMessage(handle, WmNcLeftButtonDown, new IntPtr(20), point);
            titleBar.CursorPosition = cursorUnavailable ? null : position + new Vector(0, 100);

            SendMessage(handle, WmNcMouseLeave, IntPtr.Zero, IntPtr.Zero);

            Assert.IsFalse(close.IsHovered);
            SendMessage(handle, WmNcLeftButtonUp, new IntPtr(20), point);
            FlushDispatcher();
            Assert.AreEqual(0, clicks);
        }));
    }

    [TestMethod]
    public void HoverProtectionSurvivesUnloadAndReload()
    {
        RunOnSta(() => WithTitleBar(ApplicationTheme.Light, (window, titleBar, close) =>
        {
            window.Content = null;
            FlushDispatcher();
            Assert.IsFalse(titleBar.IsLoaded);
            window.Content = titleBar;
            FlushDispatcher();
            Assert.IsTrue(titleBar.IsLoaded);
            close = (TitleBarButton)titleBar.Template.FindName("PART_CloseButton", titleBar);
            titleBar.CursorPosition = Center(close);
            var handle = new WindowInteropHelper(window).Handle;

            SendMessage(handle, WmNcHitTest, IntPtr.Zero, Pack(titleBar.CursorPosition.Value));
            SendMessage(handle, WmNcMouseLeave, IntPtr.Zero, IntPtr.Zero);

            Assert.IsTrue(close.IsHovered);
        }));
    }

    [TestMethod]
    public void LeavingWindowAfterSpuriousLeaveDoesNotLeaveButtonHighlighted()
    {
        RunOnSta(() => WithTitleBar(ApplicationTheme.Light, (window, titleBar, close) =>
        {
            titleBar.CursorPosition = Center(close);
            var handle = new WindowInteropHelper(window).Handle;
            SendMessage(handle, WmNcHitTest, IntPtr.Zero, Pack(titleBar.CursorPosition.Value));
            SendMessage(handle, WmNcMouseLeave, IntPtr.Zero, IntPtr.Zero);
            Assert.IsTrue(close.IsHovered);

            titleBar.CursorPosition += new Vector(0, 100);
            var frame = new DispatcherFrame();
            var timeout = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(150)
            };
            timeout.Tick += (_, _) => frame.Continue = false;
            timeout.Start();
            try { Dispatcher.PushFrame(frame); }
            finally { timeout.Stop(); }

            Assert.IsFalse(close.IsHovered);
        }));
    }

    private static void WithTitleBar(ApplicationTheme theme, Action<Window, TestTitleBar, TitleBarButton> action)
    {
        var titleBar = new TestTitleBar();
        var window = new Window
        {
            Content = titleBar,
            Width = 400,
            Height = 160,
            Left = -10000,
            Top = -10000,
            WindowStyle = WindowStyle.None,
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual
        };
        window.Resources.MergedDictionaries.Add(new ThemesDictionary { Theme = theme });
        window.Resources.MergedDictionaries.Add(new ControlsDictionary());
        try
        {
            window.Show();
            FlushDispatcher();
            var close = (TitleBarButton)titleBar.Template.FindName("PART_CloseButton", titleBar);
            Assert.IsNotNull(close);
            action(window, titleBar, close);
        }
        finally
        {
            window.Close();
        }
    }

    private static Point Center(FrameworkElement element) =>
        element.PointToScreen(new Point(element.ActualWidth / 2, element.ActualHeight / 2));

    private static IntPtr Pack(Point point) =>
        new(unchecked((int)(ushort)(int)point.X | ((int)(ushort)(int)point.Y << 16)));

    private static void FlushDispatcher() =>
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

    private static void RunOnSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { error = exception; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(30)), "Title bar test did not finish.");
        if (error != null)
            throw error;
    }

    private sealed class TestTitleBar : StableTitleBar
    {
        public Point? CursorPosition { get; set; }

        protected override bool TryGetCursorPosition(out Point position)
        {
            position = CursorPosition.GetValueOrDefault();
            return CursorPosition.HasValue;
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
}
