using System;
using System.Text.RegularExpressions;
using LogGrokX.Colors.Configuration;

namespace LogGrokX.Settings
{
    public sealed class ColorRuleViewModel : ViewModelBase
    {
        private string _regexString = string.Empty;
        private string _foregroundColor = string.Empty;
        private string _backgroundColor = string.Empty;

        public ColorRuleViewModel()
        {
        }

        public ColorRuleViewModel(ColorRule rule)
        {
            _regexString = rule.RegexString;
            _foregroundColor = rule.ForegroundColor;
            _backgroundColor = rule.BackgroundColor;
        }

        public string RegexString
        {
            get => _regexString;
            set
            {
                if (_regexString == value)
                    return;
                _regexString = value;
                InvokePropertyChanged();
                InvokePropertyChanged(nameof(IsRegexValid));
            }
        }

        public string ForegroundColor
        {
            get => _foregroundColor;
            set
            {
                if (_foregroundColor == value)
                    return;
                _foregroundColor = value;
                InvokePropertyChanged();
            }
        }

        public string BackgroundColor
        {
            get => _backgroundColor;
            set
            {
                if (_backgroundColor == value)
                    return;
                _backgroundColor = value;
                InvokePropertyChanged();
            }
        }

        public bool IsRegexValid
        {
            get
            {
                try
                {
                    _ = new Regex(_regexString);
                    return true;
                }
                catch (ArgumentException)
                {
                    return false;
                }
            }
        }

        public ColorRuleData ToData() => new()
        {
            RegexString = _regexString,
            ForegroundColor = _foregroundColor,
            BackgroundColor = _backgroundColor
        };

    }
}
