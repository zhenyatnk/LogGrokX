using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Wpf.Ui.Controls;

namespace LogGrokX.Controls;

public class StableTitleBar : TitleBar
{
    private const int WmNcMouseLeave = 0x02A2;
    private readonly TitleBarButton?[] _buttons = new TitleBarButton?[4];
    private readonly DispatcherTimer _hoverCheck;
    private Window? _window;
    private HwndSource? _source;
    private long _nextDiagnosticAt;

    public StableTitleBar()
    {
        SetResourceReference(StyleProperty, typeof(TitleBar));
        _hoverCheck = new DispatcherTimer(DispatcherPriority.Input, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _hoverCheck.Tick += CheckHover;
        Loaded += OnTitleBarLoaded;
        Unloaded += OnTitleBarUnloaded;
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _buttons[0] = GetTemplateChild("PART_CloseButton") as TitleBarButton;
        _buttons[1] = GetTemplateChild("PART_MaximizeButton") as TitleBarButton;
        _buttons[2] = GetTemplateChild("PART_MinimizeButton") as TitleBarButton;
        _buttons[3] = GetTemplateChild("PART_HelpButton") as TitleBarButton;
    }

    private void OnTitleBarLoaded(object sender, RoutedEventArgs e)
    {
        _window = Window.GetWindow(this);
        if (_window == null)
            return;

        _window.ContentRendered += OnContentRendered;
        Dispatcher.BeginInvoke(new Action(InstallHook), DispatcherPriority.Loaded);
    }

    private void OnContentRendered(object? sender, EventArgs e) => InstallHook();

    private void InstallHook()
    {
        if (!IsLoaded || _window == null || PresentationSource.FromVisual(this) is not HwndSource source)
            return;

        // WPF-UI installs its hook at ContentRendered; the last added hook runs first.
        _source?.RemoveHook(FilterMouseLeave);
        _source = source;
        _source.AddHook(FilterMouseLeave);
    }

    private void OnTitleBarUnloaded(object sender, RoutedEventArgs e)
    {
        _hoverCheck.Stop();
        if (_window != null)
            _window.ContentRendered -= OnContentRendered;
        _source?.RemoveHook(FilterMouseLeave);
        _source = null;
        _window = null;
    }

    private IntPtr FilterMouseLeave(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmNcMouseLeave || !TryGetCursorPosition(out var position))
            return IntPtr.Zero;

        foreach (var button in _buttons)
        {
            if (button is not { IsHovered: true } || !IsOverButton(button, position))
                continue;

            // WPF-UI 4.3.0 otherwise clears both hover and the pending click on a spurious leave.
            // https://github.com/lepoco/wpfui/issues/1647
            handled = true;
            _hoverCheck.Start();

            var now = Environment.TickCount64;
            if (now >= _nextDiagnosticAt)
            {
                _nextDiagnosticAt = now + 60000;
                var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(button);
                Trace.TraceWarning($"TitleBar: ignored WM_NCMOUSELEAVE over {button.ButtonType}; " +
                                   $"cursor={position}, dpi={dpi.DpiScaleX}x{dpi.DpiScaleY}, " +
                                   $"window={_window?.GetType().Name}.");
            }
            break;
        }
        return IntPtr.Zero;
    }

    private void CheckHover(object? sender, EventArgs e)
    {
        // Re-arming native leave tracking here can immediately generate another false leave.
        // Check only after an incident, including exits that produce no further window messages.
        var hasPosition = TryGetCursorPosition(out var position);
        var isHovered = false;
        foreach (var button in _buttons)
        {
            if (button is not { IsHovered: true })
                continue;
            if (hasPosition && IsOverButton(button, position))
                isHovered = true;
            else
                button.RemoveHover();
        }
        if (!isHovered)
            _hoverCheck.Stop();
    }

    private static bool IsOverButton(TitleBarButton button, Point position) =>
        button is { IsVisible: true, IsEnabled: true, IsHitTestVisible: true } &&
        PresentationSource.FromVisual(button) != null &&
        new Rect(default, button.RenderSize).Contains(button.PointFromScreen(position));

    protected virtual bool TryGetCursorPosition(out Point position)
    {
        var result = GetCursorPos(out var nativePosition);
        position = new Point(nativePosition.X, nativePosition.Y);
        return result;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);
}
