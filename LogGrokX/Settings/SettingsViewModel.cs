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
        private bool _requiresRestart;
        private ColorRuleViewModel? _selectedColorRule;
        private ViewSettings.ViewBigLine _savedBigLine;
        private string _savedBigLineSizeText = string.Empty;
        private ViewSettings.UpdateModeKind _savedUpdateMode;
        private bool _savedDetectBinary;
        private bool _savedDetectPem;
        private bool _savedDetectBase64;
        private bool _savedDetectHex;
        private bool _savedEnableCrashDumps;
        private string _savedMaxDumpsCountText = string.Empty;
        private string[] _savedProfiles;
        private ProfileViewModel _selectedProfile;

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

            foreach (var profile in applicationSettings.GetProfiles())
                Profiles.Add(new ProfileViewModel(profile));
            _selectedProfile = Profiles.First(profile => string.Equals(profile.Name,
                applicationSettings.GetSelectedProfile().Name, StringComparison.OrdinalIgnoreCase));
            _savedProfiles = SettingsYamlRenderer.RenderProfiles(Profiles, 0).ToArray();
            CaptureRestartSettings();

            View.PropertyChanged += OnTrackedPropertyChanged;
            Debug.PropertyChanged += OnTrackedPropertyChanged;

            SaveCommand = new DelegateCommand(() => Save());
            OpenFileCommand = new DelegateCommand(OpenSettingsFile);
            AddColorRuleCommand = new DelegateCommand(() => ColorRules.Add(new ColorRuleViewModel()));
            RemoveColorRuleCommand = new DelegateCommand(RemoveSelectedColorRule);
            AddLogFormatCommand = new DelegateCommand(() => LogFormats.Add(new LogFormatViewModel()));
            RemoveLogFormatCommand = DelegateCommand.Create<LogFormatViewModel>(format => LogFormats.Remove(format));
            AddProfileCommand = new DelegateCommand(() => AddProfile(false));
            DuplicateProfileCommand = new DelegateCommand(() => AddProfile(true));
            RemoveProfileCommand = new DelegateCommand(RemoveProfile);
        }

        public ViewSettingsViewModel View { get; }

        public DebugSettingsViewModel Debug { get; }

        public ObservableCollection<ProfileViewModel> Profiles { get; } = new();

        public ProfileViewModel SelectedProfile
        {
            get => _selectedProfile;
            set
            {
                if (value == null || ReferenceEquals(_selectedProfile, value))
                    return;
                _selectedProfile = value;
                SelectedColorRule = null;
                InvokePropertyChanged();
                InvokePropertyChanged(nameof(ColorRules));
                InvokePropertyChanged(nameof(LogFormats));
            }
        }

        public ObservableCollection<ColorRuleViewModel> ColorRules => SelectedProfile.ColorRules;

        public ObservableCollection<LogFormatViewModel> LogFormats => SelectedProfile.LogFormats;

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

        public string SettingsFileName => _applicationSettings.FileName;

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

        public bool RequiresRestart
        {
            get => _requiresRestart;
            private set
            {
                if (_requiresRestart == value)
                    return;
                _requiresRestart = value;
                InvokePropertyChanged();
            }
        }

        public ICommand SaveCommand { get; }

        public ICommand OpenFileCommand { get; }

        public ICommand AddColorRuleCommand { get; }

        public ICommand RemoveColorRuleCommand { get; }

        public ICommand AddLogFormatCommand { get; }

        public ICommand RemoveLogFormatCommand { get; }
        public ICommand AddProfileCommand { get; }
        public ICommand DuplicateProfileCommand { get; }
        public ICommand RemoveProfileCommand { get; }

        private void AddProfile(bool duplicate)
        {
            var name = duplicate ? SelectedProfile.Name + " copy" : "New profile";
            var uniqueName = name;
            for (var suffix = 2; Profiles.Any(profile => string.Equals(profile.Name, uniqueName,
                     StringComparison.OrdinalIgnoreCase)); suffix++)
                uniqueName = $"{name} {suffix}";
            var settings = duplicate ? SelectedProfile.ToSettings() : new ProfileSettings();
            settings.Name = uniqueName;
            var added = new ProfileViewModel(settings);
            Profiles.Add(added);
            SelectedProfile = added;
        }

        private void RemoveProfile()
        {
            if (Profiles.Count <= 1)
                return;
            var removed = SelectedProfile;
            SelectedProfile = Profiles.First(profile => !ReferenceEquals(profile, removed));
            Profiles.Remove(removed);
        }

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
            _applicationSettings.SetBinaryDetection(View.DetectBinary, View.DetectPem, View.DetectBase64, View.DetectHex);

            var file = new YamlSettingsFile(SettingsFileName);
            file.SetScalar("DebugSettings", "EnableCrashDumps", Debug.EnableCrashDumps ? "true" : "false");
            file.SetScalar("DebugSettings", "MaxDumpsCount", maxDumpsCount.ToString(CultureInfo.InvariantCulture));
            file.SetScalar("ViewSettings", "BigLine", View.BigLine == ViewSettings.ViewBigLine.Prune ? "prune" : "break");
            file.SetScalar("ViewSettings", "BigLineSize", bigLineSize.ToString(CultureInfo.InvariantCulture));
            file.SetScalar("ViewSettings", "TimelineAtTop", View.TimelineAtTop ? "true" : "false");
            file.SetScalar("ViewSettings", "LogFontSize", View.LogFontSize.ToString(CultureInfo.InvariantCulture));
            file.SetScalar("ViewSettings", "GroupByThread", View.GroupByThread ? "true" : "false");
            file.SetScalar("ViewSettings", "MergedFilesView", View.MergedFilesView ? "true" : "false");
            file.SetScalar("ViewSettings", "DetectBinary", View.DetectBinary ? "true" : "false");
            file.SetScalar("ViewSettings", "DetectPem", View.DetectPem ? "true" : "false");
            file.SetScalar("ViewSettings", "DetectBase64", View.DetectBase64 ? "true" : "false");
            file.SetScalar("ViewSettings", "DetectHex", View.DetectHex ? "true" : "false");
            file.SetScalar("ViewSettings", "UpdateMode", View.UpdateMode.ToString().ToLowerInvariant());

            var profiles = SettingsYamlRenderer.RenderProfiles(Profiles, 0).ToArray();
            var profilesChanged = !_savedProfiles.SequenceEqual(profiles);
            if (profilesChanged)
                file.ReplaceSequence("Settings", "Profiles",
                    indent => SettingsYamlRenderer.RenderProfiles(Profiles, indent), force: true);
            file.SetScalar("Settings", "SelectedProfile", SettingsYamlRenderer.FormatScalar(SelectedProfile.Name.Trim()));
            try
            {
                file.Save();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                ValidationMessage = $"Cannot save settings: {e.Message}";
                return false;
            }
            _savedProfiles = profiles;
            if (profilesChanged)
                _applicationSettings.UpdateProfiles(Profiles.Select(profile => profile.ToSettings()).ToArray(),
                    SelectedProfile.Name.Trim());
            else
                _applicationSettings.UpdateProfiles(_applicationSettings.Profiles, SelectedProfile.Name.Trim());

            CaptureRestartSettings();
            UpdateRequiresRestart();

            return true;
        }

        private void CaptureRestartSettings()
        {
            _savedBigLine = View.BigLine;
            _savedBigLineSizeText = View.BigLineSizeText;
            _savedUpdateMode = View.UpdateMode;
            _savedDetectBinary = View.DetectBinary;
            _savedDetectPem = View.DetectPem;
            _savedDetectBase64 = View.DetectBase64;
            _savedDetectHex = View.DetectHex;
            _savedEnableCrashDumps = Debug.EnableCrashDumps;
            _savedMaxDumpsCountText = Debug.MaxDumpsCountText;
        }

        private void OnTrackedPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            UpdateRequiresRestart();
        }

        private void UpdateRequiresRestart()
        {
            RequiresRestart =
                View.BigLine != _savedBigLine ||
                View.BigLineSizeText != _savedBigLineSizeText ||
                View.UpdateMode != _savedUpdateMode ||
                View.DetectBinary != _savedDetectBinary ||
                View.DetectPem != _savedDetectPem ||
                View.DetectBase64 != _savedDetectBase64 ||
                View.DetectHex != _savedDetectHex ||
                Debug.EnableCrashDumps != _savedEnableCrashDumps ||
                Debug.MaxDumpsCountText != _savedMaxDumpsCountText;
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

            if (Profiles.Any(profile => string.IsNullOrWhiteSpace(profile.Name)))
                errors.Add("Every profile must have a name.");
            if (Profiles.Select(profile => profile.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Profiles.Count)
                errors.Add("Profile names must be unique.");

            foreach (var format in Profiles.SelectMany(profile => profile.LogFormats))
            {
                if (!format.IsValid)
                    errors.Add("A log format has an invalid regular expression.");
                if (!format.TryGetXorMask(out _))
                    errors.Add("A log format has an invalid XOR mask.");
            }

            foreach (var rule in Profiles.SelectMany(profile => profile.ColorRules))
            {
                if (!rule.IsRegexValid)
                    errors.Add("A color rule has an invalid regular expression.");
                if ((!string.IsNullOrWhiteSpace(rule.ForegroundColor) && rule.ForegroundBrush == null) ||
                    (!string.IsNullOrWhiteSpace(rule.BackgroundColor) && rule.BackgroundBrush == null))
                    errors.Add("A color rule has an invalid color.");
            }

            return errors;
        }

        private void OpenSettingsFile()
        {
            var fileName = SettingsFileName;
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
