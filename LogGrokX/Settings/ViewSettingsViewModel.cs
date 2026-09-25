using System;
using System.Collections.Generic;
using System.Globalization;

namespace LogGrokX.Settings
{
    public sealed class ViewSettingsViewModel : ViewModelBase
    {
        private ViewSettings.ViewBigLine _bigLine;
        private string _bigLineSizeText;
        private bool _timelineAtTop;
        private double _logFontSize;
        private bool _groupByThread;
        private bool _mergedFilesView;
        private ViewSettings.UpdateModeKind _updateMode;

        public ViewSettingsViewModel(ViewSettings settings)
        {
            _bigLine = settings.BigLine;
            _bigLineSizeText = settings.BigLineSize.ToString(CultureInfo.InvariantCulture);
            _timelineAtTop = settings.TimelineAtTop;
            _logFontSize = settings.LogFontSize;
            _groupByThread = settings.GroupByThread;
            _mergedFilesView = settings.MergedFilesView;
            _updateMode = settings.UpdateMode;
        }

        public IReadOnlyList<ViewSettings.ViewBigLine> BigLineOptions { get; } =
            new[] { ViewSettings.ViewBigLine.Prune, ViewSettings.ViewBigLine.Break };

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

        public IReadOnlyList<UpdateModeOption> UpdateModeOptions { get; } =
            new[]
            {
                new UpdateModeOption(ViewSettings.UpdateModeKind.Disabled, "Do not check for updates"),
                new UpdateModeOption(ViewSettings.UpdateModeKind.Check, "Check for updates"),
                new UpdateModeOption(ViewSettings.UpdateModeKind.Install, "Install updates automatically")
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
}
