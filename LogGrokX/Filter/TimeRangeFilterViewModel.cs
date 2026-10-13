using System;
using System.Globalization;
using System.Windows.Input;
using LogGrokX.Data;
using LogGrokX.Localization;

namespace LogGrokX.Filter
{
    public sealed class TimeRangeFilterViewModel : ViewModelBase
    {
        private readonly TimeIndex _timeIndex;
        private readonly FilterSettings _filterSettings;
        private readonly LogModelFacade _logModelFacade;
        private bool _suppress;
        private bool _isAvailable;
        private bool _isLineNumberMode;
        private double _minimum;
        private double _maximum;
        private double _lowerValue;
        private double _upperValue;

        public TimeRangeFilterViewModel(TimeIndex timeIndex, FilterSettings filterSettings,
            LogModelFacade logModelFacade)
        {
            _timeIndex = timeIndex;
            _filterSettings = filterSettings;
            _logModelFacade = logModelFacade;
            ResetCommand = new DelegateCommand(Reset);
        }

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

        public void Refresh()
        {
            var hasTime = _timeIndex.HasTime && _timeIndex.IsMonotonic &&
                          _timeIndex.Count > 0 && _timeIndex.MaxTicks > _timeIndex.MinTicks;
            var lineCount = _logModelFacade.LineCount;
            IsLineNumberMode = !hasTime;

            _suppress = true;
            try
            {
                if (hasTime)
                {
                    Minimum = _timeIndex.MinTicks;
                    Maximum = _timeIndex.MaxTicks;
                }
                else
                {
                    Minimum = 0;
                    Maximum = lineCount;
                }

                LowerValue = Minimum;
                UpperValue = Maximum;
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

        private void Apply()
        {
            if (_suppress || !IsAvailable) return;

            if (IsLineNumberMode)
            {
                var lineCount = _logModelFacade.LineCount;
                var lower = (int)Math.Clamp((long)_lowerValue, 0, lineCount);
                var upper = (int)Math.Clamp((long)_upperValue, 0, lineCount);

                _filterSettings.ClearTimeRange();
                if (lower < upper && (lower > 0 || upper < lineCount))
                    _filterSettings.SetLineRange(lower, upper);
                else
                    _filterSettings.ClearLineRange();

                return;
            }

            var min = _timeIndex.MinTicks;
            var max = _timeIndex.MaxTicks;
            var timeLower = Math.Clamp((long)_lowerValue, min, max);
            var timeUpper = Math.Clamp((long)_upperValue, min, max);

            _filterSettings.ClearLineRange();
            if (timeLower < timeUpper && (timeLower > min || timeUpper < max))
                _filterSettings.SetTimeRange(timeLower, timeUpper);
            else
                _filterSettings.ClearTimeRange();
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
            _filterSettings.ClearTimeRange();
            _filterSettings.ClearLineRange();
        }

        private static string FormatDuration(TimeSpan duration) =>
            duration.TotalHours >= 1
                ? duration.ToString(@"h\:mm\:ss\.fff", CultureInfo.InvariantCulture)
                : duration.ToString(@"m\:ss\.fff", CultureInfo.InvariantCulture);

        private static string FormatLineCount(long count) =>
            Strings.LineCount(count);
    }
}
