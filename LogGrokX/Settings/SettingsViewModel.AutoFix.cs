using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using LogGrokX.Localization;

namespace LogGrokX.Settings
{
    public sealed partial class SettingsViewModel
    {
        private IReadOnlyList<SettingsProblem> _autoFixes = Array.Empty<SettingsProblem>();
        private bool _validationActive;
        private bool _suspendValidation;

        public IReadOnlyList<SettingsProblem> AutoFixes
        {
            get => _autoFixes;
            private set
            {
                if (_autoFixes.SequenceEqual(value))
                    return;
                _autoFixes = value;
                InvokePropertyChanged();
                InvokePropertyChanged(nameof(HasAutoFixes));
                InvokePropertyChanged(nameof(ManualProblems));
                InvokePropertyChanged(nameof(HasManualProblems));
            }
        }

        public bool HasAutoFixes => _autoFixes.Count > 0;

        public IReadOnlyList<SettingsProblem> ManualProblems =>
            _problems.Where(problem => !_autoFixes.Any(fix => IsSameTarget(problem, fix))).ToArray();

        public bool HasManualProblems => ManualProblems.Count > 0;

        public string FixAllActionText => Strings.Get(HasManualProblems ? "FixAvailable" : "FixAndSave");

        private static bool IsSameTarget(SettingsProblem problem, SettingsProblem fix) =>
            problem.Field == fix.Field &&
            ReferenceEquals(problem.Profile, fix.Profile) &&
            ReferenceEquals(problem.LogFormat, fix.LogFormat) &&
            ReferenceEquals(problem.ColorRule, fix.ColorRule) &&
            ReferenceEquals(problem.ValueMapping, fix.ValueMapping) &&
            (problem.Field == SettingsField.MappingValue || ReferenceEquals(problem.MappingItem, fix.MappingItem));

        public bool FixAndSave()
        {
            _suspendValidation = true;
            try
            {
                foreach (var fix in GetAutoFixes())
                    fix.Apply();
            }
            finally
            {
                _suspendValidation = false;
            }
            return Save();
        }

        public bool ApplyAutoFix(SettingsProblem problem)
        {
            var fix = GetAutoFixes().FirstOrDefault(candidate => candidate.Problem == problem);
            if (fix == null)
            {
                Revalidate();
                return false;
            }
            _suspendValidation = true;
            try
            {
                fix.Apply();
            }
            finally
            {
                _suspendValidation = false;
            }
            Revalidate();
            NavigateTo(problem);
            return true;
        }

        private void Revalidate() => PublishValidation(Validate());

        private void RevalidateIfVisible()
        {
            if (_validationActive && !_suspendValidation)
                Revalidate();
        }

        private void PublishValidation(IReadOnlyList<SettingsProblem> problems)
        {
            _validationActive = problems.Count > 0;
            Problems = problems;
            ValidationMessage = string.Join(Environment.NewLine, problems.Select(item => item.Message));
            AutoFixes = GetAutoFixes().Select(item => item.Problem).ToArray();
            InvokePropertyChanged(nameof(FixAllActionText));
        }

        private sealed record SettingsFix(SettingsProblem Problem, Action Apply);

        private List<SettingsFix> GetAutoFixes()
        {
            var fixes = new List<SettingsFix>();

            if (!int.TryParse(View.BigLineSizeText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var bigLineSize) || bigLineSize <= 0)
            {
                var value = new ViewSettings().BigLineSize.ToString(CultureInfo.InvariantCulture);
                fixes.Add(new(new(Strings.Format("FixBigLineSize", value), SettingsField.BigLineSize), () => View.BigLineSizeText = value));
            }

            if (!int.TryParse(Debug.MaxDumpsCountText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxDumpsCount) || maxDumpsCount <= 0)
            {
                var value = new DebugSettings().MaxDumpsCount.ToString(CultureInfo.InvariantCulture);
                fixes.Add(new(new(Strings.Format("FixMaxDumps", value), SettingsField.MaxDumpsCount), () => Debug.MaxDumpsCountText = value));
            }

            AddProfileNameFixes(fixes);

            foreach (var profile in Profiles)
            {
                for (var index = 0; index < profile.LogFormats.Count; index++)
                    AddLogFormatFixes(fixes, profile, index + 1, profile.LogFormats[index]);

                foreach (var rule in profile.ColorRules)
                    AddColorRuleFixes(fixes, profile, rule);
            }

            foreach (var list in ValueMappings)
                AddValueMappingFixes(fixes, list);

            return fixes;
        }

        private void AddProfileNameFixes(List<SettingsFix> fixes)
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var profile in Profiles)
            {
                var name = profile.Name.Trim();
                if (name.Length > 0 && used.Add(name))
                    continue;

                var baseName = name.Length > 0 ? name : Strings.Get("NewProfile");
                var unique = baseName;
                for (var suffix = 2; used.Contains(unique) || Profiles.Any(other =>
                         string.Equals(other.Name.Trim(), unique, StringComparison.OrdinalIgnoreCase)); suffix++)
                    unique = $"{baseName} {suffix}";
                used.Add(unique);

                var target = profile;
                fixes.Add(new(new(name.Length == 0
                            ? Strings.Format("FixProfileNameEmpty", unique)
                            : Strings.Format("FixProfileNameDuplicate", name, unique),
                        SettingsField.ProfileName, target),
                    () => target.Name = unique));
            }
        }

        private static void AddLogFormatFixes(List<SettingsFix> fixes, ProfileViewModel profile, int number, LogFormatViewModel format)
        {
            SettingsProblem Problem(string message, SettingsField field) =>
                new(LogFormatValidation.WithContext(profile.Name, number, message), field, profile, LogFormat: format);

            var invalidTransformations = format.TransformationsList.Where(t => !IsRegexValid(t)).ToArray();
            if (invalidTransformations.Length > 0)
            {
                fixes.Add(new(Problem(Strings.Format("FixTransformations", string.Join(", ", invalidTransformations)), SettingsField.Transformations),
                    () => format.Transformations = string.Join(Environment.NewLine,
                        format.TransformationsList.Where(IsRegexValid))));
            }

            if (TryCreateRegex(format.Regex) is { } regex)
            {
                var groups = regex.GetGroupNames().Skip(1).ToArray();
                var replacements = new Dictionary<string, string?>(StringComparer.Ordinal);
                foreach (var field in format.IndexedFieldsList.Where(field => regex.GroupNumberFromName(field) <= 0))
                    replacements[field] = groups.FirstOrDefault(group => string.Equals(group, field, StringComparison.OrdinalIgnoreCase));

                var renamed = replacements.Where(pair => pair.Value != null).Select(pair => $"{pair.Key} → {pair.Value}").ToArray();
                var removed = replacements.Where(pair => pair.Value == null).Select(pair => pair.Key).ToArray();
                var descriptions = new List<string>();
                if (renamed.Length > 0)
                    descriptions.Add(Strings.Format("FixIndexedFieldsCase", string.Join(", ", renamed)));
                if (removed.Length > 0)
                    descriptions.Add(Strings.Format("FixIndexedFieldsRemove", string.Join(", ", removed)));
                if (descriptions.Count > 0)
                {
                    fixes.Add(new(Problem(string.Join(" ", descriptions), SettingsField.IndexedFields),
                        () => format.IndexedFields = string.Join(Environment.NewLine, format.IndexedFieldsList
                            .Select(field => replacements.TryGetValue(field, out var replacement) ? replacement : field)
                            .OfType<string>()
                            .Distinct(StringComparer.Ordinal))));
                }
            }

            if (!format.TryGetXorMask(out _))
                fixes.Add(new(Problem(Strings.Format("FixXorMask", format.XorMask.Trim()), SettingsField.XorMask), () => format.XorMask = string.Empty));
        }

        private void AddColorRuleFixes(List<SettingsFix> fixes, ProfileViewModel profile, ColorRuleViewModel rule)
        {
            if (!rule.IsRegexValid)
            {
                var escaped = Regex.Escape(rule.RegexString);
                fixes.Add(new(new(Strings.Format("FixColorRegex", profile.Name, rule.RegexString, escaped),
                        SettingsField.ColorRegex, profile, ColorRule: rule),
                    () => rule.RegexString = escaped));
            }

            if (!_colorValidator.IsValid(rule.ForegroundColor))
                fixes.Add(new(new(Strings.Format("FixColor", profile.Name, rule.ForegroundColor),
                        SettingsField.ForegroundColor, profile, ColorRule: rule),
                    () => rule.ForegroundColor = string.Empty));
            if (!_colorValidator.IsValid(rule.BackgroundColor))
                fixes.Add(new(new(Strings.Format("FixColor", profile.Name, rule.BackgroundColor),
                        SettingsField.BackgroundColor, profile, ColorRule: rule),
                    () => rule.BackgroundColor = string.Empty));
        }

        private static void AddValueMappingFixes(List<SettingsFix> fixes, ValueMappingListViewModel list)
        {
            var missing = list.Items.FirstOrDefault(item => !item.IsEmpty && string.IsNullOrWhiteSpace(item.Value));
            if (missing != null)
            {
                fixes.Add(new(new(Strings.Format("FixMappingValueMissing", list.DisplayName), SettingsField.MappingValue,
                    ValueMapping: list, MappingItem: missing), () =>
                {
                    foreach (var item in list.Items.Where(item => !item.IsEmpty && string.IsNullOrWhiteSpace(item.Value)).ToArray())
                        list.Items.Remove(item);
                }));
            }

            var comparer = list.IgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            var duplicates = list.Items
                .Where(item => !string.IsNullOrWhiteSpace(item.Value))
                .GroupBy(item => item.Value.Trim(), comparer)
                .Where(group => group.Count() > 1)
                .ToArray();
            if (duplicates.Length > 0)
            {
                fixes.Add(new(new(Strings.Format("FixMappingDuplicates", list.DisplayName,
                        string.Join(", ", duplicates.Select(group => group.Key))), SettingsField.MappingValue,
                    ValueMapping: list, MappingItem: duplicates[0].ElementAt(1)), () =>
                {
                    var seen = new HashSet<string>(list.IgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
                    foreach (var item in list.Items.Where(item => !string.IsNullOrWhiteSpace(item.Value)).ToArray())
                    {
                        if (!seen.Add(item.Value.Trim()))
                            list.Items.Remove(item);
                    }
                }));
            }
        }

        private void InitializeValidationTracking()
        {
            Profiles.CollectionChanged += OnProfilesChanged;
            ValueMappings.CollectionChanged += OnValueMappingsChanged;
            foreach (var profile in Profiles)
                SubscribeProfile(profile);
            foreach (var list in ValueMappings)
                SubscribeValueMapping(list);
        }

        private void SubscribeProfile(ProfileViewModel profile)
        {
            profile.PropertyChanged += OnValidationInputChanged;
            profile.LogFormats.CollectionChanged += OnLogFormatsChanged;
            profile.ColorRules.CollectionChanged += OnColorRulesChanged;
            foreach (var format in profile.LogFormats)
                format.PropertyChanged += OnValidationInputChanged;
            foreach (var rule in profile.ColorRules)
                rule.PropertyChanged += OnValidationInputChanged;
        }

        private void SubscribeValueMapping(ValueMappingListViewModel list)
        {
            list.PropertyChanged += OnValidationInputChanged;
            list.Items.CollectionChanged += OnMappingItemsChanged;
            foreach (var item in list.Items)
                item.PropertyChanged += OnValidationInputChanged;
        }

        private void OnProfilesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            SubscribeNewItems<ProfileViewModel>(e, SubscribeProfile);
            RevalidateIfVisible();
        }

        private void OnLogFormatsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            SubscribeNewItems<LogFormatViewModel>(e, format => format.PropertyChanged += OnValidationInputChanged);
            RevalidateIfVisible();
        }

        private void OnColorRulesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            SubscribeNewItems<ColorRuleViewModel>(e, rule => rule.PropertyChanged += OnValidationInputChanged);
            RevalidateIfVisible();
        }

        private void OnValueMappingsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            SubscribeNewItems<ValueMappingListViewModel>(e, SubscribeValueMapping);
            RevalidateIfVisible();
        }

        private void OnMappingItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            SubscribeNewItems<ValueMappingItemViewModel>(e, item => item.PropertyChanged += OnValidationInputChanged);
            RevalidateIfVisible();
        }

        private static void SubscribeNewItems<T>(NotifyCollectionChangedEventArgs e, Action<T> subscribe)
        {
            if (e.NewItems == null)
                return;
            foreach (T item in e.NewItems)
                subscribe(item);
        }

        private void OnValidationInputChanged(object? sender, PropertyChangedEventArgs e) => RevalidateIfVisible();

        private static bool IsRegexValid(string pattern) => TryCreateRegex(pattern) != null;

        private static Regex? TryCreateRegex(string pattern)
        {
            try
            {
                return new Regex(pattern);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
    }
}
