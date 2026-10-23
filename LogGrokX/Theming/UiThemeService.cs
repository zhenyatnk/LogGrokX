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

        public event EventHandler? AccentColorChanged;

        public bool IsDark => CurrentTheme.StartsWith("Dark", StringComparison.OrdinalIgnoreCase);

        public void ApplySavedTheme(ViewSettings.AccentColorKind accentColor = ViewSettings.AccentColorKind.Blue)
        {
            _accentColor = AccentColorPalette.Normalize(accentColor);
            Apply(CurrentTheme);
        }

        private static readonly System.Collections.Generic.Dictionary<string, string> AccentBrushColorKeys = new()
        {
            ["SystemAccentColorPrimaryBrush"] = "SystemAccentColorPrimary",
            ["SystemAccentColorSecondaryBrush"] = "SystemAccentColorSecondary",
            ["SystemAccentColorTertiaryBrush"] = "SystemAccentColorTertiary",
            ["AccentFillColorDefaultBrush"] = "AccentFillColorDefault",
            ["AccentFillColorSecondaryBrush"] = "AccentFillColorSecondary",
            ["AccentFillColorTertiaryBrush"] = "AccentFillColorTertiary",
            ["AccentButtonBackground"] = "AccentFillColorDefault",
            ["AccentButtonBackgroundPointerOver"] = "AccentFillColorSecondary",
            ["AccentButtonBackgroundPressed"] = "AccentFillColorTertiary",
            ["CheckBoxCheckBackgroundFillChecked"] = "SystemAccentColorPrimary",
            ["CheckBoxCheckBackgroundFillCheckedPointerOver"] = "AccentFillColorSecondary",
            ["CheckBoxCheckBackgroundFillCheckedPressed"] = "AccentFillColorTertiary"
        };

        public void ApplyAccentColor(ViewSettings.AccentColorKind accentColor)
        {
            _accentColor = AccentColorPalette.Normalize(accentColor);
            try
            {
                var previousBrushes = CaptureAccentBrushes(Application.Current.Resources);
                ApplicationAccentColorManager.Apply(
                    AccentColorPalette.GetColor(_accentColor),
                    ApplicationThemeManager.GetAppTheme());
                ApplyDerivedAccentBrushes(Application.Current.Resources, previousBrushes);
                foreach (var resources in Application.Current.Resources.MergedDictionaries.OfType<DockThemeResources>())
                    resources.ApplyAccentColor();
                AccentColorChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception)
            {
                // ignored
            }
        }

        internal static System.Collections.Generic.IReadOnlyDictionary<string, System.Windows.Media.SolidColorBrush>
            CaptureAccentBrushes(ResourceDictionary resources)
        {
            var brushes = new System.Collections.Generic.Dictionary<string, System.Windows.Media.SolidColorBrush>();
            foreach (var brushKey in AccentBrushColorKeys.Keys)
            {
                if (resources[brushKey] is System.Windows.Media.SolidColorBrush { IsFrozen: false } brush)
                    brushes[brushKey] = brush;
            }

            return brushes;
        }

        internal static void ApplyDerivedAccentBrushes(ResourceDictionary resources,
            System.Collections.Generic.IReadOnlyDictionary<string, System.Windows.Media.SolidColorBrush>? previousBrushes = null)
        {
            foreach (var (brushKey, colorKey) in AccentBrushColorKeys)
                SetBrush(resources, brushKey, colorKey, previousBrushes);
        }

        private static void SetBrush(ResourceDictionary resources, string brushKey, string colorKey,
            System.Collections.Generic.IReadOnlyDictionary<string, System.Windows.Media.SolidColorBrush>? previousBrushes)
        {
            var color = (System.Windows.Media.Color)resources[colorKey];
            var brush = previousBrushes != null && previousBrushes.TryGetValue(brushKey, out var previousBrush)
                ? previousBrush
                : resources[brushKey] as System.Windows.Media.SolidColorBrush;
            if (brush is { IsFrozen: false })
            {
                brush.Color = color;
                brush.Opacity = 1;
                resources[brushKey] = brush;
            }
            else
            {
                resources[brushKey] = new System.Windows.Media.SolidColorBrush(color);
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
                foreach (var resources in Application.Current.Resources.MergedDictionaries.OfType<DockThemeResources>())
                    resources.Apply(isDark);

                ApplyAccentColor(_accentColor);
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
