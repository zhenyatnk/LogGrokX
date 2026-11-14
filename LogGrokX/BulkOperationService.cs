using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Input;

namespace LogGrokX
{
    public sealed class BulkOperationService : ViewModelBase
    {
        private CancellationTokenSource? _cancellation;
        private object? _owner;
        private string _title = string.Empty;
        private long _processed;
        private long _total;
        private bool _isActive;

        public BulkOperationService()
        {
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
            Trace.TraceInformation($"Bulk operation started: {title}, total={total}, owner={owner.GetType().Name}");
            return _cancellation.Token;
        }

        public void Report(long processed) => Processed = processed;

        public void Complete(CancellationToken cancellationToken)
        {
            if (_cancellation?.Token != cancellationToken)
                return;
            Processed = Total;
            IsActive = false;
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
            _owner = null;
        }
    }
}
