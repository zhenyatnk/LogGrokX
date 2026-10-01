using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LogGrokX.Colors.Configuration;
using LogGrokX.Controls.ListControls;
using LogGrokX.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;

namespace LogGrokX
{
    public class ApplicationSettings
    {
        private static ApplicationSettings? instance;

        public static string SettingsFileName => PathHelpers.GetLocalFilePath("appsettings.yaml");

        public DebugSettings DebugSettings { get; set; } = new();

        public ColorSettings ColorSettings { get; private set; } = new();

        public ViewSettings ViewSettings { get; private set; } = new();

        public LogFormat[] LogFormats { get; set; } =
            Array.Empty<LogFormat>();

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

        private static void SetViewSetting(bool current, bool value, Action<bool> set, string key)
        {
            if (current == value)
                return;

            set(value);
            SaveViewSettingValue(key, value ? "true" : "false");
        }

        private static void SaveViewSettingValue(string key, string value)
        {
            try
            {
                if (!File.Exists(SettingsFileName))
                    return;

                var lines = File.ReadAllLines(SettingsFileName).ToList();
                var keyRegex = new Regex($@"^(\s*){Regex.Escape(key)}\s*:.*$");
                for (var i = 0; i < lines.Count; i++)
                {
                    var match = keyRegex.Match(lines[i]);
                    if (!match.Success) continue;
                    lines[i] = $"{match.Groups[1].Value}{key}: {value}";
                    File.WriteAllLines(SettingsFileName, lines);
                    return;
                }

                var sectionRegex = new Regex(@"^(\s*)ViewSettings\s*:\s*$");
                for (var i = 0; i < lines.Count; i++)
                {
                    var match = sectionRegex.Match(lines[i]);
                    if (!match.Success) continue;
                    lines.Insert(i + 1, $"{match.Groups[1].Value}  {key}: {value}");
                    File.WriteAllLines(SettingsFileName, lines);
                    return;
                }
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
        {
            var builder = new ConfigurationBuilder()
                .AddYamlFile(SettingsFileName, true, true);

            var settings = new ApplicationSettings();

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
            });

            return settings;
        }

        private ApplicationSettings()
        {
        }
    }
}