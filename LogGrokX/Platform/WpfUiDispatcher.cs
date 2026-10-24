using System;
using System.Windows.Threading;

namespace LogGrokX.Platform;

public sealed class WpfUiDispatcher(Dispatcher dispatcher) : IUiDispatcher
{
    public bool CheckAccess() => dispatcher.CheckAccess();

    public IDisposable Post(Action action) => new PendingOperation(dispatcher.BeginInvoke(action));

    public IUiTimer CreateTimer(TimeSpan interval, Action tick, UiTimerPriority priority = UiTimerPriority.Normal) =>
        new UiTimer(dispatcher, interval, tick, priority);

    private sealed class PendingOperation(DispatcherOperation operation) : IDisposable
    {
        public void Dispose() => operation.Abort();
    }

    private sealed class UiTimer : IUiTimer
    {
        private readonly DispatcherTimer _timer;
        private readonly EventHandler _handler;

        public UiTimer(Dispatcher dispatcher, TimeSpan interval, Action tick, UiTimerPriority priority)
        {
            _timer = new DispatcherTimer(priority == UiTimerPriority.Background ? DispatcherPriority.Background : DispatcherPriority.Normal,
                dispatcher) { Interval = interval };
            _handler = (_, _) => tick();
            _timer.Tick += _handler;
        }

        public TimeSpan Interval { get => _timer.Interval; set => _timer.Interval = value; }
        public bool IsEnabled => _timer.IsEnabled;
        public void Start() => _timer.Start();
        public void Stop() => _timer.Stop();
        public void Dispose()
        {
            _timer.Stop();
            _timer.Tick -= _handler;
        }
    }
}
