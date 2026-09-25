using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace LogGrokX
{
    /// <summary>
    /// WPF does not route WM_MOUSEHWHEEL (tilt wheel / second wheel, e.g. on MX Master).
    /// This helper listens to the thread message loop and scrolls the element under the cursor horizontally.
    /// </summary>
    public static class HorizontalMouseWheel
    {
        private const int WM_MOUSEHWHEEL = 0x020E;
        private const double WheelDelta = 120.0;
        private const double PixelsPerNotch = 48.0;

        private static bool _isEnabled;

        public static void Enable()
        {
            if (_isEnabled)
                return;

            ComponentDispatcher.ThreadFilterMessage += OnThreadFilterMessage;
            _isEnabled = true;
        }

        public static void Disable()
        {
            if (!_isEnabled)
                return;

            ComponentDispatcher.ThreadFilterMessage -= OnThreadFilterMessage;
            _isEnabled = false;
        }

        private static void OnThreadFilterMessage(ref MSG msg, ref bool handled)
        {
            if (handled || msg.message != WM_MOUSEHWHEEL)
                return;

            var delta = (short)((msg.wParam.ToInt64() >> 16) & 0xFFFF);
            if (delta == 0)
                return;

            if (Mouse.DirectlyOver is not DependencyObject source)
                return;

            // Positive delta = wheel tilted right.
            handled = ScrollHorizontally(source, delta / WheelDelta * PixelsPerNotch);
        }

        private static bool ScrollHorizontally(DependencyObject source, double step)
        {
            for (var current = source; current != null; current = GetParent(current))
            {
                switch (current)
                {
                    case IScrollInfo scrollInfo when scrollInfo.ExtentWidth > scrollInfo.ViewportWidth:
                        scrollInfo.SetHorizontalOffset(Math.Clamp(
                            scrollInfo.HorizontalOffset + step,
                            0,
                            scrollInfo.ExtentWidth - scrollInfo.ViewportWidth));
                        return true;

                    case ScrollViewer scrollViewer when scrollViewer.ScrollableWidth > 0:
                        scrollViewer.ScrollToHorizontalOffset(Math.Clamp(
                            scrollViewer.HorizontalOffset + step, 0, scrollViewer.ScrollableWidth));
                        return true;
                }
            }

            return false;
        }

        private static DependencyObject? GetParent(DependencyObject element) =>
            element is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(element) ?? LogicalTreeHelper.GetParent(element)
                : element is FrameworkContentElement contentElement
                    ? contentElement.Parent
                    : LogicalTreeHelper.GetParent(element);
    }
}
