namespace LogGrokX.Platform;

public sealed class SynchronizationContextDispatcher : IUiDispatcher
{
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;
    private readonly int _threadId = Environment.CurrentManagedThreadId;

    public bool CheckAccess() => _context == null || Environment.CurrentManagedThreadId == _threadId;

    public IDisposable Post(Action action)
    {
        var work = new PendingAction(action);
        if (_context != null)
            _context.Post(_ => work.Run(), null);
        else
            ThreadPool.QueueUserWorkItem(_ => work.Run());
        return work;
    }

    public IUiTimer CreateTimer(TimeSpan interval, Action tick, UiTimerPriority priority = UiTimerPriority.Normal) =>
        new ContextTimer(this, interval, tick);

    private sealed class PendingAction(Action action) : IDisposable
    {
        private Action? _action = action;
        public void Run() => Interlocked.Exchange(ref _action, null)?.Invoke();
        public void Dispose() => Interlocked.Exchange(ref _action, null);
    }

    private sealed class ContextTimer(IUiDispatcher dispatcher, TimeSpan interval, Action tick) : IUiTimer
    {
        private readonly object _sync = new();
        private Timer? _timer;
        private IDisposable? _pending;
        private int _version;

        public TimeSpan Interval { get; set; } = interval;
        public bool IsEnabled { get { lock (_sync) return _timer != null; } }

        public void Start()
        {
            lock (_sync)
            {
                if (_timer != null) return;
                var version = ++_version;
                _timer = new Timer(_ =>
                {
                    lock (_sync)
                    {
                        if (_timer == null || version != _version) return;
                        _pending?.Dispose();
                        _pending = dispatcher.Post(() =>
                        {
                            lock (_sync)
                            {
                                if (_timer == null || version != _version) return;
                                tick();
                            }
                        });
                    }
                }, null, Interval, Interval == TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : Interval);
            }
        }

        public void Stop()
        {
            lock (_sync)
            {
                _version++;
                _timer?.Dispose();
                _timer = null;
                _pending?.Dispose();
                _pending = null;
            }
        }

        public void Dispose() => Stop();
    }
}
