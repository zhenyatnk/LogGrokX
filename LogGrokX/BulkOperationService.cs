using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Input;
using LogGrokX.Platform;

namespace LogGrokX
{
    public sealed class BulkOperationService : ViewModelBase
    {
        public static readonly TimeSpan IndicatorDelay = TimeSpan.FromSeconds(1);

        private readonly IUiDispatcher _dispatcher;
        private CancellationTokenSource? _cancellation;
        private IUiTimer? _indicatorTimer;
        private object? _owner;
        private string _title = string.Empty;
        private long _processed;
        private long _total;
        private bool _isActive;
        private bool _isIndicatorVisible;

        public BulkOperationService(IUiDispatcher dispatcher)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            CancelCommand = new DelegateCommand(() => Cancel());
        }

        public ICommand CancelCommand { get; }

        public string Title
        {
            get => _title;
            private set => SetAndRaiseIfChanged(ref _title, value);
        }

        public long Processed
        {
            get => _processed;
            private set
            {
                if (_processed == value)
                    return;
                _processed = value;
                InvokePropertyChanged();
                InvokePropertyChanged(nameof(Progress));
                InvokePropertyChanged(nameof(ProgressText));
            }
        }

        public long Total
        {
            get => _total;
            private set
            {
                if (_total == value)
                    return;
                _total = value;
                InvokePropertyChanged();
                InvokePropertyChanged(nameof(Progress));
                InvokePropertyChanged(nameof(ProgressText));
            }
        }

        public double Progress => Total == 0 ? 0 : Math.Min(100, (double)Processed * 100 / Total);

        public string ProgressText => $"{Processed:N0} / {Total:N0}";

        public bool IsActive
        {
            get => _isActive;
            private set => SetAndRaiseIfChanged(ref _isActive, value);
        }

        public bool IsIndicatorVisible
        {
            get => _isIndicatorVisible;
            private set => SetAndRaiseIfChanged(ref _isIndicatorVisible, value);
        }

        public CancellationToken Begin(string title, long total, object owner)
        {
            Cancel();
            _cancellation?.Dispose();
            _cancellation = new CancellationTokenSource();
            _owner = owner;
            Title = title;
            Processed = 0;
            Total = total;
            IsActive = true;
            var cancellationToken = _cancellation.Token;
            IUiTimer? timer = null;
            timer = _dispatcher.CreateTimer(IndicatorDelay, () =>
            {
                timer!.Dispose();
                if (ReferenceEquals(_indicatorTimer, timer))
                    _indicatorTimer = null;
                if (_cancellation?.Token == cancellationToken && IsActive)
                    IsIndicatorVisible = true;
            }, UiTimerPriority.Background);
            _indicatorTimer = timer;
            timer.Start();
            Trace.TraceInformation($"Bulk operation started: {title}, total={total}, owner={owner.GetType().Name}");
            return cancellationToken;
        }

        public void Report(long processed) => Processed = processed;

        public void Complete(CancellationToken cancellationToken)
        {
            if (_cancellation?.Token != cancellationToken)
                return;
            Processed = Total;
            IsActive = false;
            HideIndicator();
            Trace.TraceInformation($"Bulk operation completed: {Title}");
            _owner = null;
        }

        public void Cancel(object owner)
        {
            if (ReferenceEquals(_owner, owner))
                Cancel();
        }

        public void Cancel()
        {
            Trace.TraceInformation($"Bulk operation cancelled: {Title}");
            _cancellation?.Cancel();
            IsActive = false;
            HideIndicator();
            _owner = null;
        }

        private void HideIndicator()
        {
            _indicatorTimer?.Dispose();
            _indicatorTimer = null;
            IsIndicatorVisible = false;
        }
    }
}
