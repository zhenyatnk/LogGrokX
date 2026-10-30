using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Input;
using LogGrokX.Data;
using LogGrokX.Platform;
using LogGrokX.Localization;

namespace LogGrokX.Settings
{
    public sealed partial class SettingsViewModel : ViewModelBase
    {
        private readonly IDesktopService _desktop;
        private readonly ApplicationSettings _applicationSettings;
        private readonly TimelinePlacementService _timelinePlacementService;
        private readonly ThreadGroupingService _threadGroupingService;
        private readonly MergedFilesViewService _mergedFilesViewService;
        private readonly TextZoomService _textZoomService;
        private readonly IThemeService _themeService;
        private readonly IColorValidator _colorValidator;
        private string _validationMessage = string.Empty;
        private bool _requiresRestart;
        private ColorRuleViewModel? _selectedColorRule;
        private ValueMappingListViewModel? _selectedValueMapping;
        private string _valueMappingMessage = string.Empty;
        private string _savedValueMappings;
        private ViewSettings.ViewBigLine _savedBigLine;
        private string _savedLanguage = string.Empty;
        private string _savedBigLineSizeText = string.Empty;
        private bool _savedEnableCrashDumps;
        private string _savedMaxDumpsCountText = string.Empty;
        private string[] _savedProfiles;
        private ProfileViewModel _selectedProfile;
        private AccentColorKind _savedAccentColor;

        public SettingsViewModel(ApplicationSettings applicationSettings,
            TimelinePlacementService timelinePlacementService,
            ThreadGroupingService threadGroupingService,
            MergedFilesViewService mergedFilesViewService,
            TextZoomService textZoomService, IThemeService themeService,
            IColorValidator colorValidator, IDesktopService desktop)
        {
            _desktop = desktop ?? throw new ArgumentNullException(nameof(desktop));
            _applicationSettings = applicationSettings;
            _timelinePlacementService = timelinePlacementService;
            _threadGroupingService = threadGroupingService;
            _mergedFilesViewService = mergedFilesViewService;
            _textZoomService = textZoomService;
            _themeService = themeService;
            _colorValidator = colorValidator;

            View = new ViewSettingsViewModel(applicationSettings.ViewSettings)
            {
                MergedFilesView = mergedFilesViewService.IsEnabled
            };
            Debug = new DebugSettingsViewModel(applicationSettings.DebugSettings);
            Symbols = new SymbolSettingsViewModel(applicationSettings.SymbolSettings);

            foreach (var list in applicationSettings.ValueMappings)
                ValueMappings.Add(new ValueMappingListViewModel(list));
            _selectedValueMapping = ValueMappings.FirstOrDefault();

            _savedValueMappings = GetValueMappingsSignature(GetValueMappingsData());

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
            AddValueMappingCommand = new DelegateCommand(() =>
            {
                var list = new ValueMappingListViewModel(new ValueMappingList { Name = GetUniqueValueMappingName(Strings.Get("NewMapping")) });
                ValueMappings.Add(list);
                SelectedValueMapping = list;
            });
            RemoveValueMappingCommand = new DelegateCommand(RemoveSelectedValueMapping);
            AddProfileCommand = new DelegateCommand(() => AddProfile(false));
            DuplicateProfileCommand = new DelegateCommand(() => AddProfile(true));
            RemoveProfileCommand = new DelegateCommand(RemoveProfile);
        }

        public ViewSettingsViewModel View { get; }

        public DebugSettingsViewModel Debug { get; }

        public SymbolSettingsViewModel Symbols { get; }

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

        public ObservableCollection<ValueMappingListViewModel> ValueMappings { get; } = new();

        public ValueMappingListViewModel? SelectedValueMapping
        {
            get => _selectedValueMapping;
            set
            {
                if (ReferenceEquals(_selectedValueMapping, value))
                    return;
                _selectedValueMapping = value;
                InvokePropertyChanged();
                InvokePropertyChanged(nameof(HasSelectedValueMapping));
            }
        }

        public bool HasSelectedValueMapping => _selectedValueMapping != null;

        public string ValueMappingMessage
        {
            get => _valueMappingMessage;
            private set
            {
                if (_valueMappingMessage == value)
                    return;
                _valueMappingMessage = value;
                InvokePropertyChanged();
            }
        }

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
            var name = duplicate ? Strings.Format("ProfileCopy", SelectedProfile.Name) : Strings.Get("NewProfile");
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

        public ICommand AddValueMappingCommand { get; }

        public ICommand RemoveValueMappingCommand { get; }

        public bool ImportValueMappings(IEnumerable<string> fileNames)
        {
            var imported = new List<ValueMappingList>();
            foreach (var fileName in fileNames)
            {
                try
                {
                    imported.AddRange(ValueMappingJson.Deserialize(File.ReadAllText(fileName),
                        Path.GetFileNameWithoutExtension(fileName)));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or
                                              System.Text.Json.JsonException or FormatException)
                {
                    ValueMappingMessage = Strings.Format("ImportFailed", Path.GetFileName(fileName), e.Message);
                    return false;
                }
            }

            ImportValueMappings(imported);
            return true;
        }

        public void ImportValueMappings(IReadOnlyList<ValueMappingList> lists)
        {
            ValueMappingListViewModel? last = null;
            var replaced = 0;
            foreach (var list in lists)
            {
                var viewModel = new ValueMappingListViewModel(list);
                var existing = ValueMappings.FirstOrDefault(m =>
                    !string.IsNullOrWhiteSpace(list.Name) &&
                    string.Equals(m.Name.Trim(), list.Name.Trim(), StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    ValueMappings[ValueMappings.IndexOf(existing)] = viewModel;
                    replaced++;
                }
                else
                {
                    ValueMappings.Add(viewModel);
                }

                last = viewModel;
            }

            if (last != null)
                SelectedValueMapping = last;

            var items = lists.Sum(list => list.Items.Length);
            ValueMappingMessage = replaced > 0
                ? Strings.Format("ImportReplaced", lists.Count, items, replaced)
                : Strings.Format("ImportDone", lists.Count, items);
        }

        public bool ExportValueMappings(string fileName, bool all)
        {
            try
            {
                if (all)
                {
                    File.WriteAllText(fileName, ValueMappingJson.Serialize(GetValueMappingsData()));
                    ValueMappingMessage = Strings.Format("ExportDone", ValueMappings.Count, Path.GetFileName(fileName));
                }
                else if (_selectedValueMapping != null)
                {
                    File.WriteAllText(fileName, ValueMappingJson.Serialize(_selectedValueMapping.ToData()));
                    ValueMappingMessage = Strings.Format("ExportListDone", _selectedValueMapping.DisplayName, Path.GetFileName(fileName));
                }

                return true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                ValueMappingMessage = Strings.Format("ExportFailed", e.Message);
                return false;
            }
        }

        private List<ValueMappingList> GetValueMappingsData() =>
            ValueMappings.Select(list => list.ToData()).ToList();

        private static string GetValueMappingsSignature(IEnumerable<ValueMappingList> lists) =>
            ValueMappingJson.Serialize(lists);

        private string GetUniqueValueMappingName(string baseName)
        {
            var name = baseName;
            for (var i = 2; ValueMappings.Any(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)); i++)
                name = $"{baseName} {i}";
            return name;
        }

        private void RemoveSelectedValueMapping()
        {
            if (_selectedValueMapping == null)
                return;

            var index = ValueMappings.IndexOf(_selectedValueMapping);
            ValueMappings.Remove(_selectedValueMapping);
            SelectedValueMapping = ValueMappings.Count == 0
                ? null
                : ValueMappings[Math.Min(index, ValueMappings.Count - 1)];
        }

        public bool Save()
        {
            var errors = Validate();
            if (errors.Count > 0)
            {
                ValidationMessage = string.Join(Environment.NewLine, errors);
                AutoFixes = GetAutoFixes().Select(fix => fix.Description).ToArray();
                return false;
            }

            ValidationMessage = string.Empty;
            AutoFixes = Array.Empty<string>();

            var bigLineSize = int.Parse(View.BigLineSizeText.Trim(), CultureInfo.InvariantCulture);
            var maxDumpsCount = int.Parse(Debug.MaxDumpsCountText.Trim(), CultureInfo.InvariantCulture);

            _applicationSettings.ViewSettings.BigLine = View.BigLine;
            _applicationSettings.ViewSettings.Language = View.Language;
            _applicationSettings.ViewSettings.BigLineSize = bigLineSize;
            _applicationSettings.ViewSettings.UpdateMode = View.UpdateMode;
            _applicationSettings.ViewSettings.AccentColor = View.AccentColor;
            _themeService.ApplyAccentColor(View.AccentColor);
            _applicationSettings.DebugSettings.EnableCrashDumps = Debug.EnableCrashDumps;
            _applicationSettings.DebugSettings.MaxDumpsCount = maxDumpsCount;

            _timelinePlacementService.SetPlacement(View.TimelineAtTop, View.TimelineVisible);
            _threadGroupingService.SetEnabled(View.GroupByThread);
            _threadGroupingService.SetShowSeparators(View.ShowThreadGroupSeparators);
            _mergedFilesViewService.SetEnabled(View.MergedFilesView);
            _textZoomService.SetFontSize(View.LogFontSize);
            _applicationSettings.SetBinaryDetection(View.DetectBinary, View.DetectPem, View.DetectBase64, View.DetectHex);

            var valueMappings = GetValueMappingsData();
            var valueMappingsSignature = GetValueMappingsSignature(valueMappings);
            if (valueMappingsSignature != _savedValueMappings)
                _applicationSettings.SetValueMappings(valueMappings.ToArray());
            var symbols = Symbols.ToSettings();
            _applicationSettings.SetSymbolSettings(symbols);

            var file = new YamlSettingsFile(SettingsFileName);
            file.SetScalar("DebugSettings", "EnableCrashDumps", Debug.EnableCrashDumps ? "true" : "false");
            file.SetScalar("DebugSettings", "MaxDumpsCount", maxDumpsCount.ToString(CultureInfo.InvariantCulture));
            file.SetScalar("ViewSettings", "BigLine", View.BigLine == ViewSettings.ViewBigLine.Prune ? "prune" : "break");
            file.SetScalar("ViewSettings", "Language", View.Language);
            file.SetScalar("ViewSettings", "BigLineSize", bigLineSize.ToString(CultureInfo.InvariantCulture));
            file.SetScalar("ViewSettings", "AccentColor", View.AccentColor.ToString().ToLowerInvariant());
            file.SetScalar("ViewSettings", "TimelineAtTop", View.TimelineAtTop ? "true" : "false");
            file.SetScalar("ViewSettings", "TimelineVisible", View.TimelineVisible ? "true" : "false");
            file.SetScalar("ViewSettings", "LogFontSize", View.LogFontSize.ToString(CultureInfo.InvariantCulture));
            file.SetScalar("ViewSettings", "GroupByThread", View.GroupByThread ? "true" : "false");
            file.SetScalar("ViewSettings", "ShowThreadGroupSeparators", View.ShowThreadGroupSeparators ? "true" : "false");
            file.SetScalar("ViewSettings", "DetectBinary", View.DetectBinary ? "true" : "false");
            file.SetScalar("ViewSettings", "DetectPem", View.DetectPem ? "true" : "false");
            file.SetScalar("ViewSettings", "DetectBase64", View.DetectBase64 ? "true" : "false");
            file.SetScalar("ViewSettings", "DetectHex", View.DetectHex ? "true" : "false");
            file.SetScalar("ViewSettings", "UpdateMode", View.UpdateMode.ToString().ToLowerInvariant());
            file.SetScalar("SymbolSettings", "Enabled", symbols.Enabled ? "true" : "false");
            file.SetScalar("SymbolSettings", "SymbolPath", QuoteScalar(symbols.SymbolPath));
            file.SetScalar("SymbolSettings", "SourcePath", QuoteScalar(symbols.SourcePath));
            file.SetScalar("SymbolSettings", "CachePath", QuoteScalar(symbols.CachePath));
            file.SetScalar("SymbolSettings", "AllowSourceServerCommands", symbols.AllowSourceServerCommands ? "true" : "false");

            var profiles = SettingsYamlRenderer.RenderProfiles(Profiles, 0).ToArray();
            var profilesChanged = !_savedProfiles.SequenceEqual(profiles);
            if (profilesChanged)
                file.ReplaceSequence("Settings", "Profiles",
                    indent => SettingsYamlRenderer.RenderProfiles(Profiles, indent), force: true);
            file.SetScalar("Settings", "SelectedProfile", SettingsYamlRenderer.FormatScalar(SelectedProfile.Name.Trim()));
            if (valueMappingsSignature != _savedValueMappings)
            {
                file.EnsureKey("Settings", "ValueMappings");
                file.ReplaceSequence("Settings", "ValueMappings",
                    indent => SettingsYamlRenderer.RenderValueMappings(valueMappings, indent));
            }
            try
            {
                file.Save();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                ValidationMessage = Strings.Format("SaveSettingsFailed", e.Message);
                return false;
            }
            _savedProfiles = profiles;
            if (profilesChanged)
                _applicationSettings.UpdateProfiles(Profiles.Select(profile => profile.ToSettings()).ToArray(),
                    SelectedProfile.Name.Trim());
            else
                _applicationSettings.UpdateProfiles(_applicationSettings.Profiles, SelectedProfile.Name.Trim());

            _savedValueMappings = valueMappingsSignature;
            CaptureRestartSettings();
            UpdateRequiresRestart();

            return true;
        }

        private static string QuoteScalar(string value) => $"'{value.Replace("'", "''")}'";

        private void CaptureRestartSettings()
        {
            _savedLanguage = View.Language;
            _savedBigLine = View.BigLine;
            _savedBigLineSizeText = View.BigLineSizeText;
            _savedEnableCrashDumps = Debug.EnableCrashDumps;
            _savedMaxDumpsCountText = Debug.MaxDumpsCountText;
            _savedAccentColor = View.AccentColor;
        }

        private void OnTrackedPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (ReferenceEquals(sender, View) &&
                e.PropertyName == nameof(ViewSettingsViewModel.AccentColor))
                _themeService.ApplyAccentColor(View.AccentColor);
            UpdateRequiresRestart();
        }

        public void Cancel()
        {
            View.AccentColor = _savedAccentColor;
        }

        private void UpdateRequiresRestart()
        {
            RequiresRestart =
                View.Language != _savedLanguage ||
                View.BigLine != _savedBigLine ||
                View.BigLineSizeText != _savedBigLineSizeText ||
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
                errors.Add(Strings.Get("BigLineSizeInvalid"));

            if (!int.TryParse(Debug.MaxDumpsCountText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxDumpsCount) || maxDumpsCount <= 0)
                errors.Add(Strings.Get("MaxDumpsInvalid"));

            if (Profiles.Any(profile => string.IsNullOrWhiteSpace(profile.Name)))
                errors.Add(Strings.Get("ProfileNameRequired"));
            if (Profiles.Select(profile => profile.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Profiles.Count)
                errors.Add(Strings.Get("ProfileNamesUnique"));

            foreach (var profile in Profiles)
            {
                for (var index = 0; index < profile.LogFormats.Count; index++)
                {
                    var format = profile.LogFormats[index];
                    if (!format.IsValid)
                        errors.Add(LogFormatValidation.WithContext(profile.Name, index + 1, format.ValidationMessage));
                    if (!format.TryGetXorMask(out _))
                        errors.Add(Strings.Get("XorMaskInvalid"));
                }
            }

            foreach (var rule in Profiles.SelectMany(profile => profile.ColorRules))
            {
                if (!rule.IsRegexValid)
                    errors.Add(Strings.Get("ColorRegexInvalid"));
                if (!_colorValidator.IsValid(rule.ForegroundColor) ||
                    !_colorValidator.IsValid(rule.BackgroundColor))
                    errors.Add(Strings.Get("ColorInvalid"));
            }

            foreach (var list in ValueMappings)
                errors.AddRange(list.GetValidationErrors());

            return errors;
        }

        private void OpenSettingsFile()
        {
            if (File.Exists(SettingsFileName)) _desktop.OpenPath(SettingsFileName);
        }

    }
}
