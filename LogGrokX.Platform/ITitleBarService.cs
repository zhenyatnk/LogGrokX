namespace LogGrokX.Platform;

public readonly record struct ScreenPoint(double X, double Y);

public delegate bool NativeMessageFilter(int message, nint wParam, nint lParam);

public interface ITitleBarService
{
    bool TryGetCursorPosition(out ScreenPoint position);
    IDisposable InstallMessageHook(nint window, NativeMessageFilter filter);
}
