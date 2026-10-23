using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace LogGrokX.Theming
{
    public class UiThemeService
    {
        public const string LightTheme = "Light";
        public const string DarkTheme = "Dark";

        private readonly string _storeFileName =
            HomeDirectoryPathProvider.GetDataFileFullPath("Theme.json");
        private ViewSettings.AccentColorKind _accentColor = ViewSettings.AccentColorKind.Blue;

        public UiThemeService()
        {
            CurrentTheme = Load();
        }

        public string CurrentTheme { get; private set; }

        public bool IsDark => CurrentTheme.StartsWith("Dark", StringComparison.OrdinalIgnoreCase);

        public void ApplySavedTheme(ViewSettings.AccentColorKind accentColor = ViewSettings.AccentColorKind.Blue)
        {
            _accentColor = AccentColorPalette.Normalize(accentColor);
            Apply(CurrentTheme);
        }

        public void ApplyAccentColor(ViewSettings.AccentColorKind accentColor)
        {
            _accentColor = AccentColorPalette.Normalize(accentColor);
            try
            {
                ApplicationAccentColorManager.Apply(
                    AccentColorPalette.GetColor(_accentColor),
                    ApplicationThemeManager.GetAppTheme());
            }
            catch (Exception)
            {
                // ignored
            }
        }

        public void Apply(string themeName)
        {
            try
            {
                var isDark = themeName.StartsWith("Dark", StringComparison.OrdinalIgnoreCase);
                themeName = isDark ? DarkTheme : LightTheme;

                ApplicationThemeManager.Apply(
                    isDark ? ApplicationTheme.Dark : ApplicationTheme.Light,
                    WindowBackdropType.None,
                    true);
                ApplyAccentColor(_accentColor);

                foreach (var resources in Application.Current.Resources.MergedDictionaries.OfType<DockThemeResources>())
                    resources.Apply(isDark);

                CurrentTheme = themeName;
                Save();
            }
            catch (Exception)
            {
                // ignored
            }
        }

        public string Toggle()
        {
            Apply(IsDark ? LightTheme : DarkTheme);
            return CurrentTheme;
        }

        private void Save()
        {
            try
            {
                using var createStream = File.Create(_storeFileName);
                var options = new JsonSerializerOptions { WriteIndented = true };
                JsonSerializer.Serialize(createStream, CurrentTheme, options);
            }
            catch (Exception)
            {
                // ignored
            }
        }

        private string Load()
        {
            try
            {
                if (!File.Exists(_storeFileName)) return LightTheme;
                using var stream = File.OpenRead(_storeFileName);
                var themeName = JsonSerializer.Deserialize<string>(stream);
                return string.IsNullOrEmpty(themeName) ? LightTheme : themeName;
            }
            catch (Exception)
            {
                return LightTheme;
            }
        }
    }
}
