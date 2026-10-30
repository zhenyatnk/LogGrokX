using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using LogGrokX.Localization;

namespace LogGrokX.Settings
{
    public sealed partial class SettingsViewModel
    {
        private IReadOnlyList<string> _autoFixes = Array.Empty<string>();

        public IReadOnlyList<string> AutoFixes
        {
            get => _autoFixes;
            private set
            {
                if (_autoFixes.SequenceEqual(value))
                    return;
                _autoFixes = value;
                InvokePropertyChanged();
                InvokePropertyChanged(nameof(HasAutoFixes));
            }
        }

        public bool HasAutoFixes => _autoFixes.Count > 0;

        public bool FixAndSave()
        {
            foreach (var fix in GetAutoFixes())
                fix.Apply();
            return Save();
        }

        private sealed record SettingsFix(string Description, Action Apply);

        private List<SettingsFix> GetAutoFixes()
        {
            var fixes = new List<SettingsFix>();

            if (!int.TryParse(View.BigLineSizeText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var bigLineSize) || bigLineSize <= 0)
            {
                var value = new ViewSettings().BigLineSize.ToString(CultureInfo.InvariantCulture);
                fixes.Add(new(Strings.Format("FixBigLineSize", value), () => View.BigLineSizeText = value));
            }

            if (!int.TryParse(Debug.MaxDumpsCountText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxDumpsCount) || maxDumpsCount <= 0)
            {
                var value = new DebugSettings().MaxDumpsCount.ToString(CultureInfo.InvariantCulture);
                fixes.Add(new(Strings.Format("FixMaxDumps", value), () => Debug.MaxDumpsCountText = value));
            }

            AddProfileNameFixes(fixes);

            foreach (var profile in Profiles)
            {
                for (var index = 0; index < profile.LogFormats.Count; index++)
                    AddLogFormatFixes(fixes, profile.Name, index + 1, profile.LogFormats[index]);

                foreach (var rule in profile.ColorRules)
                    AddColorRuleFixes(fixes, profile.Name, rule);
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
                fixes.Add(new(name.Length == 0
                        ? Strings.Format("FixProfileNameEmpty", unique)
                        : Strings.Format("FixProfileNameDuplicate", name, unique),
                    () => target.Name = unique));
            }
        }

        private static void AddLogFormatFixes(List<SettingsFix> fixes, string profileName, int number, LogFormatViewModel format)
        {
            string WithContext(string message) => LogFormatValidation.WithContext(profileName, number, message);

            var invalidTransformations = format.TransformationsList.Where(t => !IsRegexValid(t)).ToArray();
            if (invalidTransformations.Length > 0)
            {
                fixes.Add(new(WithContext(Strings.Format("FixTransformations", string.Join(", ", invalidTransformations))),
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
                    fixes.Add(new(WithContext(string.Join(" ", descriptions)),
                        () => format.IndexedFields = string.Join(Environment.NewLine, format.IndexedFieldsList
                            .Select(field => replacements.TryGetValue(field, out var replacement) ? replacement : field)
                            .OfType<string>()
                            .Distinct(StringComparer.Ordinal))));
                }
            }

            if (!format.TryGetXorMask(out _))
                fixes.Add(new(WithContext(Strings.Format("FixXorMask", format.XorMask.Trim())), () => format.XorMask = string.Empty));
        }

        private void AddColorRuleFixes(List<SettingsFix> fixes, string profileName, ColorRuleViewModel rule)
        {
            if (!rule.IsRegexValid)
            {
                var escaped = Regex.Escape(rule.RegexString);
                fixes.Add(new(Strings.Format("FixColorRegex", profileName, rule.RegexString, escaped),
                    () => rule.RegexString = escaped));
            }

            if (!_colorValidator.IsValid(rule.ForegroundColor))
                fixes.Add(new(Strings.Format("FixColor", profileName, rule.ForegroundColor),
                    () => rule.ForegroundColor = string.Empty));
            if (!_colorValidator.IsValid(rule.BackgroundColor))
                fixes.Add(new(Strings.Format("FixColor", profileName, rule.BackgroundColor),
                    () => rule.BackgroundColor = string.Empty));
        }

        private static void AddValueMappingFixes(List<SettingsFix> fixes, ValueMappingListViewModel list)
        {
            if (list.Items.Any(item => !item.IsEmpty && string.IsNullOrWhiteSpace(item.Value)))
            {
                fixes.Add(new(Strings.Format("FixMappingValueMissing", list.DisplayName), () =>
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
                .Select(group => group.Key)
                .ToArray();
            if (duplicates.Length > 0)
            {
                fixes.Add(new(Strings.Format("FixMappingDuplicates", list.DisplayName, string.Join(", ", duplicates)), () =>
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
