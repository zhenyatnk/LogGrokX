using System;
using System.Collections.Generic;
using System.Globalization;
using LogGrokX.Localization;

namespace LogGrokX.Settings
{
    public sealed class ViewSettingsViewModel : ViewModelBase
    {
        private string _language;
        private ViewSettings.ViewBigLine _bigLine;
        private string _bigLineSizeText;
        private bool _timelineAtTop;
        private bool _timelineVisible;
        private double _logFontSize;
        private bool _groupByThread;
        private bool _showThreadGroupSeparators;
        private bool _mergedFilesView;
        private bool _detectBinary;
        private bool _detectPem;
        private bool _detectBase64;
        private bool _detectHex;
        private ViewSettings.UpdateModeKind _updateMode;

        public ViewSettingsViewModel(ViewSettings settings)
        {
            _language = LocalizationService.NormalizeLanguage(settings.Language);
            _bigLine = settings.BigLine;
            _bigLineSizeText = settings.BigLineSize.ToString(CultureInfo.InvariantCulture);
            _timelineAtTop = settings.TimelineAtTop;
            _timelineVisible = settings.TimelineVisible;
            _logFontSize = settings.LogFontSize;
            _groupByThread = settings.GroupByThread;
            _showThreadGroupSeparators = settings.ShowThreadGroupSeparators;
            _mergedFilesView = settings.MergedFilesView;
            _detectBinary = settings.DetectBinary;
            _detectPem = settings.DetectPem;
            _detectBase64 = settings.DetectBase64;
            _detectHex = settings.DetectHex;
            _updateMode = settings.UpdateMode;
        }

        public IReadOnlyList<LanguageOption> LanguageOptions => LocalizationService.Languages;

        public string Language
        {
            get => _language;
            set
            {
                var language = LocalizationService.NormalizeLanguage(value);
                if (_language == language)
                    return;
                _language = language;
                InvokePropertyChanged();
            }
        }

        public IReadOnlyList<BigLineOption> BigLineOptions { get; } =
            new[]
            {
                new BigLineOption(ViewSettings.ViewBigLine.Prune, Strings.Get("BigLinePrune")),
                new BigLineOption(ViewSettings.ViewBigLine.Break, Strings.Get("BigLineBreak"))
            };

        public ViewSettings.ViewBigLine BigLine
        {
            get => _bigLine;
            set
            {
                if (_bigLine == value)
                    return;
                _bigLine = value;
                InvokePropertyChanged();
            }
        }

        public string BigLineSizeText
        {
            get => _bigLineSizeText;
            set
            {
                if (_bigLineSizeText == value)
                    return;
                _bigLineSizeText = value;
                InvokePropertyChanged();
            }
        }

        public bool TimelineAtTop
        {
            get => _timelineAtTop;
            set
            {
                if (_timelineAtTop == value)
                    return;
                _timelineAtTop = value;
                InvokePropertyChanged();
            }
        }

        public bool TimelineVisible
        {
            get => _timelineVisible;
            set
            {
                if (_timelineVisible == value)
                    return;
                _timelineVisible = value;
                InvokePropertyChanged();
            }
        }

        public double LogFontSize
        {
            get => _logFontSize;
            set
            {
                if (Math.Abs(_logFontSize - value) < 0.001)
                    return;
                _logFontSize = value;
                InvokePropertyChanged();
                InvokePropertyChanged(nameof(LogFontSizeText));
            }
        }

        public string LogFontSizeText => _logFontSize.ToString("0.#", CultureInfo.InvariantCulture);

        public bool GroupByThread
        {
            get => _groupByThread;
            set
            {
                if (_groupByThread == value)
                    return;
                _groupByThread = value;
                InvokePropertyChanged();
            }
        }

        public bool ShowThreadGroupSeparators
        {
            get => _showThreadGroupSeparators;
            set
            {
                if (_showThreadGroupSeparators == value)
                    return;
                _showThreadGroupSeparators = value;
                InvokePropertyChanged();
            }
        }

        public bool MergedFilesView
        {
            get => _mergedFilesView;
            set
            {
                if (_mergedFilesView == value)
                    return;
                _mergedFilesView = value;
                InvokePropertyChanged();
            }
        }

        public bool DetectBinary
        {
            get => _detectBinary;
            set
            {
                if (_detectBinary == value)
                    return;
                _detectBinary = value;
                InvokePropertyChanged();
            }
        }

        public bool DetectPem
        {
            get => _detectPem;
            set
            {
                if (_detectPem == value)
                    return;
                _detectPem = value;
                InvokePropertyChanged();
            }
        }

        public bool DetectBase64
        {
            get => _detectBase64;
            set
            {
                if (_detectBase64 == value)
                    return;
                _detectBase64 = value;
                InvokePropertyChanged();
            }
        }

        public bool DetectHex
        {
            get => _detectHex;
            set
            {
                if (_detectHex == value)
                    return;
                _detectHex = value;
                InvokePropertyChanged();
            }
        }

        public IReadOnlyList<UpdateModeOption> UpdateModeOptions { get; } =
            new[]
            {
                new UpdateModeOption(ViewSettings.UpdateModeKind.Disabled, Strings.Get("UpdatesDisabled")),
                new UpdateModeOption(ViewSettings.UpdateModeKind.Check, Strings.Get("CheckForUpdates")),
                new UpdateModeOption(ViewSettings.UpdateModeKind.Install, Strings.Get("InstallUpdatesAutomatically"))
            };

        public ViewSettings.UpdateModeKind UpdateMode
        {
            get => _updateMode;
            set
            {
                if (_updateMode == value)
                    return;
                _updateMode = value;
                InvokePropertyChanged();
            }
        }
    }

    public sealed record UpdateModeOption(ViewSettings.UpdateModeKind Value, string Title);
    public sealed record BigLineOption(ViewSettings.ViewBigLine Value, string Title);
}
