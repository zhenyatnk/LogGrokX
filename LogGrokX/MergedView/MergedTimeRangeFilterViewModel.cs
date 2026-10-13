using System;
using System.Globalization;
using System.Windows.Input;
using LogGrokX.Data;
using LogGrokX.Localization;

namespace LogGrokX.MergedView
{
    public sealed class MergedTimeRangeFilterViewModel : ViewModelBase
    {
        private TimeIndex _timeIndex = new();
        private bool _suppress;
        private bool _isAvailable;
        private bool _isLineNumberMode;
        private double _minimum;
        private double _maximum;
        private double _lowerValue;
        private double _upperValue;

        public MergedTimeRangeFilterViewModel()
        {
            ResetCommand = new DelegateCommand(Reset);
        }

        public event Action? Changed;

        public TimeIndex TimeIndex => _timeIndex;

        public bool IsAvailable
        {
            get => _isAvailable;
            private set => SetAndRaiseIfChanged(ref _isAvailable, value);
        }

        public bool IsLineNumberMode
        {
            get => _isLineNumberMode;
            private set => SetAndRaiseIfChanged(ref _isLineNumberMode, value);
        }

        public double Minimum
        {
            get => _minimum;
            private set => SetAndRaiseIfChanged(ref _minimum, value);
        }

        public double Maximum
        {
            get => _maximum;
            private set => SetAndRaiseIfChanged(ref _maximum, value);
        }

        public double LowerValue
        {
            get => _lowerValue;
            set
            {
                if (Equals(_lowerValue, value)) return;
                _lowerValue = value;
                InvokePropertyChanged();
                InvokePropertyChanged(nameof(SelectedRangeText));
                InvokePropertyChanged(nameof(DurationText));
                Apply();
            }
        }

        public double UpperValue
        {
            get => _upperValue;
            set
            {
                if (Equals(_upperValue, value)) return;
                _upperValue = value;
                InvokePropertyChanged();
                InvokePropertyChanged(nameof(SelectedRangeText));
                InvokePropertyChanged(nameof(DurationText));
                Apply();
            }
        }

        public string MinText => IsLineNumberMode
            ? ((long)_minimum + 1).ToString(CultureInfo.InvariantCulture)
            : TimestampParser.Format((long)_minimum);

        public string MaxText => IsLineNumberMode
            ? ((long)_maximum).ToString(CultureInfo.InvariantCulture)
            : TimestampParser.Format((long)_maximum);

        public string SelectedRangeText
        {
            get
            {
                if (IsLineNumberMode)
                {
                    var fromLine = (long)_lowerValue + 1;
                    var toLine = (long)_upperValue;
                    var size = Math.Max(0, toLine - fromLine + 1);
                    return $"{fromLine} - {toLine} ({FormatLineCount(size)})";
                }

                var from = TimestampParser.Format((long)_lowerValue);
                var to = TimestampParser.Format((long)_upperValue);
                if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
                    return string.Empty;

                var duration = TimeSpan.FromTicks(Math.Max(0, (long)_upperValue - (long)_lowerValue));
                return $"{from} - {to} ({FormatDuration(duration)})";
            }
        }

        public string DurationText
        {
            get
            {
                if (!IsAvailable)
                    return string.Empty;

                var size = Math.Max(0, (long)_upperValue - (long)_lowerValue);
                return IsLineNumberMode
                    ? FormatLineCount(size)
                    : DurationFormatter.Format(size);
            }
        }

        public ICommand ResetCommand { get; }

        public (long From, long To)? TimeRange =>
            !IsLineNumberMode && HasActiveRange ? ((long)_lowerValue, (long)_upperValue) : null;

        public (int From, int To)? LineRange =>
            IsLineNumberMode && HasActiveRange ? ((int)_lowerValue, (int)_upperValue) : null;

        private bool HasActiveRange =>
            _lowerValue > _minimum || _upperValue < _maximum;

        public void Refresh(TimeIndex timeIndex, int lineCount)
        {
            _timeIndex = timeIndex;

            var hasTime = timeIndex.HasTime && timeIndex.IsMonotonic &&
                          timeIndex.Count > 0 && timeIndex.MaxTicks > timeIndex.MinTicks;
            IsLineNumberMode = !hasTime;

            var previousLower = _lowerValue;
            var previousUpper = _upperValue;
            var hadRange = _isAvailable && HasActiveRange;

            _suppress = true;
            try
            {
                if (hasTime)
                {
                    Minimum = timeIndex.MinTicks;
                    Maximum = timeIndex.MaxTicks;
                }
                else
                {
                    Minimum = 0;
                    Maximum = lineCount;
                }

                if (hadRange)
                {
                    LowerValue = Math.Clamp(previousLower, Minimum, Maximum);
                    UpperValue = Math.Clamp(previousUpper, Minimum, Maximum);
                }
                else
                {
                    LowerValue = Minimum;
                    UpperValue = Maximum;
                }
            }
            finally
            {
                _suppress = false;
            }

            InvokePropertyChanged(nameof(MinText));
            InvokePropertyChanged(nameof(MaxText));
            InvokePropertyChanged(nameof(SelectedRangeText));
            IsAvailable = hasTime || lineCount > 0;
            InvokePropertyChanged(nameof(DurationText));
        }

        public void Reset()
        {
            _suppress = true;
            try
            {
                LowerValue = Minimum;
                UpperValue = Maximum;
            }
            finally
            {
                _suppress = false;
            }

            InvokePropertyChanged(nameof(SelectedRangeText));
            InvokePropertyChanged(nameof(DurationText));
            Changed?.Invoke();
        }

        private void Apply()
        {
            if (_suppress || !IsAvailable) return;
            Changed?.Invoke();
        }

        private static string FormatDuration(TimeSpan duration) =>
            duration.TotalHours >= 1
                ? duration.ToString(@"h\:mm\:ss\.fff", CultureInfo.InvariantCulture)
                : duration.ToString(@"m\:ss\.fff", CultureInfo.InvariantCulture);

        private static string FormatLineCount(long count) =>
            Strings.LineCount(count);
    }
}