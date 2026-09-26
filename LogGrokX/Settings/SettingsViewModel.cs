using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Input;

namespace LogGrokX.Settings
{
    public sealed class SettingsViewModel : ViewModelBase
    {
        private readonly ApplicationSettings _applicationSettings;
        private readonly TimelinePlacementService _timelinePlacementService;
        private readonly ThreadGroupingService _threadGroupingService;
        private readonly MergedFilesViewService _mergedFilesViewService;
        private readonly TextZoomService _textZoomService;
        private string _validationMessage = string.Empty;
        private ColorRuleViewModel? _selectedColorRule;
        private List<ColorRuleData> _savedColorRules;
        private List<LogFormatData> _savedLogFormats;

        public SettingsViewModel(ApplicationSettings applicationSettings,
            TimelinePlacementService timelinePlacementService,
            ThreadGroupingService threadGroupingService,
            MergedFilesViewService mergedFilesViewService,
            TextZoomService textZoomService)
        {
            _applicationSettings = applicationSettings;
            _timelinePlacementService = timelinePlacementService;
            _threadGroupingService = threadGroupingService;
            _mergedFilesViewService = mergedFilesViewService;
            _textZoomService = textZoomService;

            View = new ViewSettingsViewModel(applicationSettings.ViewSettings);
            Debug = new DebugSettingsViewModel(applicationSettings.DebugSettings);

            foreach (var rule in applicationSettings.ColorSettings.Rules)
                ColorRules.Add(new ColorRuleViewModel(rule));

            foreach (var format in applicationSettings.LogFormats)
                LogFormats.Add(new LogFormatViewModel(format));

            _savedColorRules = ColorRules.Select(rule => rule.ToData()).ToList();
            _savedLogFormats = LogFormats.Select(format => format.ToData()).ToList();

            SaveCommand = new DelegateCommand(() => Save());
            OpenFileCommand = new DelegateCommand(OpenSettingsFile);
            AddColorRuleCommand = new DelegateCommand(() => ColorRules.Add(new ColorRuleViewModel()));
            RemoveColorRuleCommand = new DelegateCommand(RemoveSelectedColorRule);
            AddLogFormatCommand = new DelegateCommand(() => LogFormats.Add(new LogFormatViewModel()));
            RemoveLogFormatCommand = DelegateCommand.Create<LogFormatViewModel>(format => LogFormats.Remove(format));
        }

        public ViewSettingsViewModel View { get; }

        public DebugSettingsViewModel Debug { get; }

        public ObservableCollection<ColorRuleViewModel> ColorRules { get; } = new();

        public ObservableCollection<LogFormatViewModel> LogFormats { get; } = new();

        public ColorRuleViewModel? SelectedColorRule
        {
            get => _selectedColorRule;
            set
            {
                if (ReferenceEquals(_selectedColorRule, value))
                    return;
                _selectedColorRule = value;
                InvokePropertyChanged();
            }
        }

        public string SettingsFileName => ApplicationSettings.SettingsFileName;

        public string ValidationMessage
        {
            get => _validationMessage;
            private set
            {
                if (_validationMessage == value)
                    return;
                _validationMessage = value;
                InvokePropertyChanged();
                InvokePropertyChanged(nameof(HasValidationMessage));
            }
        }

        public bool HasValidationMessage => !string.IsNullOrEmpty(_validationMessage);

        public ICommand SaveCommand { get; }

        public ICommand OpenFileCommand { get; }

        public ICommand AddColorRuleCommand { get; }

        public ICommand RemoveColorRuleCommand { get; }

        public ICommand AddLogFormatCommand { get; }

        public ICommand RemoveLogFormatCommand { get; }

        public bool Save()
        {
            var errors = Validate();
            if (errors.Count > 0)
            {
                ValidationMessage = string.Join(Environment.NewLine, errors);
                return false;
            }

            ValidationMessage = string.Empty;

            var bigLineSize = int.Parse(View.BigLineSizeText.Trim(), CultureInfo.InvariantCulture);
            var maxDumpsCount = int.Parse(Debug.MaxDumpsCountText.Trim(), CultureInfo.InvariantCulture);

            _applicationSettings.ViewSettings.BigLine = View.BigLine;
            _applicationSettings.ViewSettings.BigLineSize = bigLineSize;
            _applicationSettings.ViewSettings.UpdateMode = View.UpdateMode;
            _applicationSettings.DebugSettings.EnableCrashDumps = Debug.EnableCrashDumps;
            _applicationSettings.DebugSettings.MaxDumpsCount = maxDumpsCount;

            _timelinePlacementService.SetAtTop(View.TimelineAtTop);
            _threadGroupingService.SetEnabled(View.GroupByThread);
            _mergedFilesViewService.SetEnabled(View.MergedFilesView);
            _textZoomService.SetFontSize(View.LogFontSize);

            var file = new YamlSettingsFile(ApplicationSettings.SettingsFileName);
            file.SetScalar("DebugSettings", "EnableCrashDumps", Debug.EnableCrashDumps ? "true" : "false");
            file.SetScalar("DebugSettings", "MaxDumpsCount", maxDumpsCount.ToString(CultureInfo.InvariantCulture));
            file.SetScalar("ViewSettings", "BigLine", View.BigLine == ViewSettings.ViewBigLine.Prune ? "prune" : "break");
            file.SetScalar("ViewSettings", "BigLineSize", bigLineSize.ToString(CultureInfo.InvariantCulture));
            file.SetScalar("ViewSettings", "TimelineAtTop", View.TimelineAtTop ? "true" : "false");
            file.SetScalar("ViewSettings", "LogFontSize", View.LogFontSize.ToString(CultureInfo.InvariantCulture));
            file.SetScalar("ViewSettings", "GroupByThread", View.GroupByThread ? "true" : "false");
            file.SetScalar("ViewSettings", "MergedFilesView", View.MergedFilesView ? "true" : "false");
            file.SetScalar("ViewSettings", "UpdateMode", View.UpdateMode.ToString().ToLowerInvariant());

            var colorRules = ColorRules.Select(rule => rule.ToData()).ToList();
            if (!AreColorRulesEqual(_savedColorRules, colorRules))
            {
                file.ReplaceSequence("ColorSettings", "Rules",
                    indent => SettingsYamlRenderer.RenderColorRules(colorRules, indent));
                _savedColorRules = colorRules;
            }

            var logFormats = LogFormats.Select(format => format.ToData()).ToList();
            if (!AreLogFormatsEqual(_savedLogFormats, logFormats))
            {
                file.ReplaceSequence("Settings", "LogFormats",
                    indent => SettingsYamlRenderer.RenderLogFormats(logFormats, indent));
                _savedLogFormats = logFormats;
            }

            file.Save();

            return true;
        }

        private static bool AreColorRulesEqual(IReadOnlyList<ColorRuleData> left, IReadOnlyList<ColorRuleData> right)
        {
            if (left.Count != right.Count)
                return false;

            for (var i = 0; i < left.Count; i++)
            {
                if (left[i].RegexString != right[i].RegexString ||
                    left[i].ForegroundColor != right[i].ForegroundColor ||
                    left[i].BackgroundColor != right[i].BackgroundColor)
                    return false;
            }

            return true;
        }

        private static bool AreLogFormatsEqual(IReadOnlyList<LogFormatData> left, IReadOnlyList<LogFormatData> right)
        {
            if (left.Count != right.Count)
                return false;

            for (var i = 0; i < left.Count; i++)
            {
                if (left[i].Regex != right[i].Regex ||
                    left[i].TimeField != right[i].TimeField ||
                    left[i].TimeFormat != right[i].TimeFormat ||
                    left[i].XorMask != right[i].XorMask ||
                    !left[i].IndexedFields.SequenceEqual(right[i].IndexedFields) ||
                    !left[i].Transformations.SequenceEqual(right[i].Transformations))
                    return false;
            }

            return true;
        }

        private void RemoveSelectedColorRule()
        {
            if (_selectedColorRule != null)
                ColorRules.Remove(_selectedColorRule);
        }

        private List<string> Validate()
        {
            var errors = new List<string>();

            if (!int.TryParse(View.BigLineSizeText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var bigLineSize) || bigLineSize <= 0)
                errors.Add("Big line size must be a positive integer.");

            if (!int.TryParse(Debug.MaxDumpsCountText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxDumpsCount) || maxDumpsCount <= 0)
                errors.Add("Max dumps count must be a positive integer.");

            foreach (var format in LogFormats)
            {
                if (!format.IsValid)
                    errors.Add("A log format has an invalid regular expression.");
                if (!format.TryGetXorMask(out _))
                    errors.Add("A log format has an invalid XOR mask.");
            }

            foreach (var rule in ColorRules)
            {
                if (!rule.IsRegexValid)
                    errors.Add("A color rule has an invalid regular expression.");
            }

            return errors;
        }

        private static void OpenSettingsFile()
        {
            var fileName = ApplicationSettings.SettingsFileName;
            if (!File.Exists(fileName))
                return;

            void StartProcess(string verb)
            {
                using var process = new Process
                {
                    StartInfo =
                    {
                        FileName = fileName,
                        UseShellExecute = true,
                        Verb = verb
                    }
                };
                process.Start();
            }

            try
            {
                StartProcess(string.Empty);
            }
            catch (Win32Exception e)
            {
                if (e.NativeErrorCode == 1155)
                    StartProcess("openas");
                else throw;
            }
        }
    }
}