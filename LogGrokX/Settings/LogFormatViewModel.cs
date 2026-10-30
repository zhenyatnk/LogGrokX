using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using LogGrokX.Data;

namespace LogGrokX.Settings
{
    public sealed class LogFormatViewModel : ViewModelBase
    {
        private string _regex = string.Empty;
        private string _indexedFields = string.Empty;
        private string _timeField = string.Empty;
        private string _timeFormat = string.Empty;
        private string _transformations = string.Empty;
        private string _xorMask = string.Empty;

        public LogFormatViewModel()
        {
        }

        public LogFormatViewModel(LogFormat format)
        {
            _regex = format.Regex;
            _indexedFields = string.Join(Environment.NewLine, format.IndexedFields);
            _timeField = format.TimeField;
            _timeFormat = format.TimeFormat;
            _transformations = string.Join(Environment.NewLine, format.Transformations);
            _xorMask = format.XorMask == 0 ? string.Empty : $"0x{format.XorMask:x2}";
        }

        public string Regex
        {
            get => _regex;
            set
            {
                if (_regex == value)
                    return;
                _regex = value;
                InvokePropertyChanged();
                InvokePropertyChanged(nameof(FieldNames));
                NotifyValidationChanged();
            }
        }

        public string IndexedFields
        {
            get => _indexedFields;
            set
            {
                if (_indexedFields == value)
                    return;
                _indexedFields = value;
                InvokePropertyChanged();
                NotifyValidationChanged();
            }
        }

        public string TimeField
        {
            get => _timeField;
            set
            {
                if (_timeField == value)
                    return;
                _timeField = value;
                InvokePropertyChanged();
            }
        }

        public string TimeFormat
        {
            get => _timeFormat;
            set
            {
                if (_timeFormat == value)
                    return;
                _timeFormat = value;
                InvokePropertyChanged();
            }
        }

        public string Transformations
        {
            get => _transformations;
            set
            {
                if (_transformations == value)
                    return;
                _transformations = value;
                InvokePropertyChanged();
                NotifyValidationChanged();
            }
        }

        public string XorMask
        {
            get => _xorMask;
            set
            {
                if (_xorMask == value)
                    return;
                _xorMask = value;
                InvokePropertyChanged();
            }
        }

        public string FieldNames
        {
            get
            {
                try
                {
                    var names = new Regex(_regex).GetGroupNames();
                    return names.Length > 1 ? string.Join(", ", names.Skip(1)) : string.Empty;
                }
                catch (ArgumentException)
                {
                    return string.Empty;
                }
            }
        }

        public bool IsValid => CreateFormat().GetValidationErrors().Count == 0;

        public string ValidationMessage => string.Join(Environment.NewLine,
            LogFormatValidation.GetMessages(CreateFormat()));

        public bool HasValidationMessage => !IsValid;

        internal IEnumerable<(string PropertyName, string Message)> GetValidationErrors() =>
            LogFormatValidation.GetErrors(CreateFormat());

        public IReadOnlyList<string> IndexedFieldsList => SplitLines(_indexedFields);

        public IReadOnlyList<string> TransformationsList => SplitLines(_transformations);

        public bool TryGetXorMask(out byte mask)
        {
            mask = 0;
            var text = _xorMask.Trim();
            if (text.Length == 0)
                return true;

            try
            {
                mask = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                    ? Convert.ToByte(text[2..], 16)
                    : Convert.ToByte(text, CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception e) when (e is FormatException or OverflowException)
            {
                return false;
            }
        }

        public LogFormatData ToData()
        {
            TryGetXorMask(out var mask);
            return new LogFormatData
            {
                Regex = _regex,
                IndexedFields = IndexedFieldsList,
                TimeField = _timeField,
                TimeFormat = _timeFormat,
                Transformations = TransformationsList,
                XorMask = mask == 0 ? string.Empty : $"0x{mask:x2}"
            };
        }

        private LogFormat CreateFormat() => new()
        {
            Regex = _regex,
            IndexedFields = IndexedFieldsList.ToArray(),
            Transformations = TransformationsList.ToArray()
        };

        private void NotifyValidationChanged()
        {
            InvokePropertyChanged(nameof(IsValid));
            InvokePropertyChanged(nameof(ValidationMessage));
            InvokePropertyChanged(nameof(HasValidationMessage));
        }

        private static IReadOnlyList<string> SplitLines(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return Array.Empty<string>();

            return value
                .Split('\n')
                .Select(line => line.TrimEnd('\r').Trim())
                .Where(line => line.Length > 0)
                .ToArray();
        }
    }
}
