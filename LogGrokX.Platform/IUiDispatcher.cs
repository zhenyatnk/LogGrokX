namespace LogGrokX.Platform;

public enum UiTimerPriority { Normal, Background }

public interface IUiDispatcher
{
    bool CheckAccess();
    IDisposable Post(Action action);
    IUiTimer CreateTimer(TimeSpan interval, Action tick, UiTimerPriority priority = UiTimerPriority.Normal);
}

public interface IUiTimer : IDisposable
{
    TimeSpan Interval { get; set; }
    bool IsEnabled { get; }
    void Start();
    void Stop();
}

public static class UiDispatcher
{
    public static Func<IUiDispatcher> Factory { get; set; } = () => new SynchronizationContextDispatcher();
    public static IUiDispatcher Current { get => Factory(); set => Factory = () => value; }
}
