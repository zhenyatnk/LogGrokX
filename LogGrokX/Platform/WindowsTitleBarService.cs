using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace LogGrokX.Platform;

public sealed class WindowsTitleBarService : ITitleBarService
{
    public bool TryGetCursorPosition(out ScreenPoint position)
    {
        var result = GetCursorPos(out var native);
        position = new ScreenPoint(native.X, native.Y);
        return result;
    }

    public IDisposable InstallMessageHook(nint window, NativeMessageFilter filter)
    {
        var source = HwndSource.FromHwnd(window) ?? throw new InvalidOperationException("Window handle is unavailable.");
        return new MessageHook(source, filter);
    }

    private sealed class MessageHook : IDisposable
    {
        private readonly HwndSource _source;
        private readonly HwndSourceHook _hook;

        public MessageHook(HwndSource source, NativeMessageFilter filter)
        {
            _source = source;
            _hook = (nint hwnd, int message, nint wParam, nint lParam, ref bool handled) =>
            {
                if (filter(message, wParam, lParam)) handled = true;
                return nint.Zero;
            };
            source.AddHook(_hook);
        }

        public void Dispose() => _source.RemoveHook(_hook);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);
}
