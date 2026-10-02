using System;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Linq;
using LogGrokX.Colors.Configuration;
using LogGrokX.Controls.ListControls;
using LogGrokX.Data;
using LogGrokX.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;

namespace LogGrokX
{
    public class ApplicationSettings
    {
        public const string LegacyProfileName = "Default";
        private static ApplicationSettings? instance;
        private readonly string _fileName;

        public static string SettingsFileName => PathHelpers.GetLocalFilePath("appsettings.yaml");

        public DebugSettings DebugSettings { get; set; } = new();

        public ColorSettings ColorSettings { get; private set; } = new();

        public ViewSettings ViewSettings { get; private set; } = new();

        public LogFormat[] LogFormats { get; set; } =
            Array.Empty<LogFormat>();

        public ProfileSettings[] Profiles { get; set; } = Array.Empty<ProfileSettings>();
        public string SelectedProfile { get; set; } = LegacyProfileName;
        public event Action? ProfilesChanged;
        internal string FileName => _fileName;

        public IReadOnlyList<ProfileSettings> GetProfiles()
        {
            var configured = Profiles
                .Where(profile => !string.IsNullOrWhiteSpace(profile.Name))
                .GroupBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .Select(profile => new ProfileSettings
                {
                    Name = profile.Name,
                    InheritLegacySettings = profile.InheritLegacySettings,
                    ColorSettings = profile.ColorSettings ??
                        (profile.InheritLegacySettings == false ? new ColorSettings() : ColorSettings),
                    LogFormats = profile.LogFormats ??
                        (profile.InheritLegacySettings == false ? Array.Empty<LogFormat>() : LogFormats)
                }).ToArray();
            return configured.Length > 0 ? configured :
                [new ProfileSettings { Name = LegacyProfileName, ColorSettings = ColorSettings, LogFormats = LogFormats }];
        }

        public ProfileSettings GetSelectedProfile()
        {
            var profiles = GetProfiles();
            return profiles.FirstOrDefault(profile =>
                string.Equals(profile.Name, SelectedProfile, StringComparison.OrdinalIgnoreCase)) ?? profiles[0];
        }

        public void SetSelectedProfile(string name)
        {
            SelectedProfile = name;
            try
            {
                var file = new YamlSettingsFile(_fileName);
                file.SetScalar("Settings", "SelectedProfile", SettingsYamlRenderer.FormatScalar(name));
                file.Save();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Trace.TraceError($"Cannot save selected profile: {e}");
            }
        }

        internal void UpdateProfiles(ProfileSettings[] profiles, string selectedProfile)
        {
            Profiles = profiles;
            SelectedProfile = selectedProfile;
            ProfilesChanged?.Invoke();
        }

        private readonly Dictionary<string, ColumnSettings> _columnSettingsMap = new();
        public ColumnSettings GetColumnSettings(string logFormat)
        {
            if (!_columnSettingsMap.TryGetValue(logFormat, out var columnSettings))
            {
                columnSettings = new ColumnSettings();
                _columnSettingsMap[logFormat] = columnSettings;
            }

            return columnSettings;
        }

        public void SetTimelineAtTop(bool isAtTop)
        {
            if (ViewSettings.TimelineAtTop == isAtTop)
                return;

            ViewSettings.TimelineAtTop = isAtTop;
            SaveViewSettingValue("TimelineAtTop", isAtTop ? "true" : "false");
        }

        public void SetLogFontSize(double fontSize)
        {
            if (Math.Abs(ViewSettings.LogFontSize - fontSize) < 0.001)
                return;

            ViewSettings.LogFontSize = fontSize;
            SaveViewSettingValue("LogFontSize", fontSize.ToString(CultureInfo.InvariantCulture));
        }

        public void SetGroupByThread(bool groupByThread)
        {
            if (ViewSettings.GroupByThread == groupByThread)
                return;

            ViewSettings.GroupByThread = groupByThread;
            SaveViewSettingValue("GroupByThread", groupByThread ? "true" : "false");
        }

        public void SetMergedFilesView(bool mergedFilesView)
        {
            if (ViewSettings.MergedFilesView == mergedFilesView)
                return;

            ViewSettings.MergedFilesView = mergedFilesView;
            SaveViewSettingValue("MergedFilesView", mergedFilesView ? "true" : "false");
        }

        public void SetBinaryDetection(bool detectBinary, bool detectPem, bool detectBase64, bool detectHex)
        {
            SetViewSetting(ViewSettings.DetectBinary, detectBinary, v => ViewSettings.DetectBinary = v, nameof(ViewSettings.DetectBinary));
            SetViewSetting(ViewSettings.DetectPem, detectPem, v => ViewSettings.DetectPem = v, nameof(ViewSettings.DetectPem));
            SetViewSetting(ViewSettings.DetectBase64, detectBase64, v => ViewSettings.DetectBase64 = v, nameof(ViewSettings.DetectBase64));
            SetViewSetting(ViewSettings.DetectHex, detectHex, v => ViewSettings.DetectHex = v, nameof(ViewSettings.DetectHex));
            BinaryDetectionOptions.Current = BinaryDetectionOptions.FromSettings(ViewSettings);
        }

        private void SetViewSetting(bool current, bool value, Action<bool> set, string key)
        {
            if (current == value)
                return;

            set(value);
            SaveViewSettingValue(key, value ? "true" : "false");
        }

        private void SaveViewSettingValue(string key, string value)
        {
            try
            {
                var file = new YamlSettingsFile(_fileName);
                file.SetScalar("ViewSettings", key, value);
                file.Save();
            }
            catch (Exception)
            {
                // ignored
            }
        }

        public static ApplicationSettings Instance()
        {
            if (instance == null)
                instance = Load();
            return instance;
        }

        private static ApplicationSettings Load()
            => LoadFromFile(SettingsFileName, true);

        internal static ApplicationSettings LoadFromFile(string fileName, bool reloadOnChange = false)
        {
            var builder = new ConfigurationBuilder()
                .AddYamlFile(fileName, true, reloadOnChange);

            var settings = new ApplicationSettings(fileName);

            var configuration = builder.Build();
            configuration.GetSection("Settings").Bind(settings);
            BinaryDetectionOptions.Current = BinaryDetectionOptions.FromSettings(settings.ViewSettings);

            ChangeToken.OnChange(() => configuration.GetReloadToken(), () =>
            {
                var newSettings = new ApplicationSettings();
                configuration.GetSection("Settings").Bind(newSettings);
                settings.ColorSettings = newSettings.ColorSettings;
                settings.LogFormats = newSettings.LogFormats;
                settings.ViewSettings.DetectBinary = newSettings.ViewSettings.DetectBinary;
                settings.ViewSettings.DetectPem = newSettings.ViewSettings.DetectPem;
                settings.ViewSettings.DetectBase64 = newSettings.ViewSettings.DetectBase64;
                settings.ViewSettings.DetectHex = newSettings.ViewSettings.DetectHex;
                BinaryDetectionOptions.Current = BinaryDetectionOptions.FromSettings(settings.ViewSettings);
                settings.UpdateProfiles(newSettings.Profiles, newSettings.SelectedProfile);
            });

            return settings;
        }

        internal ApplicationSettings(string? fileName = null)
        {
            _fileName = fileName ?? SettingsFileName;
        }
    }
}
