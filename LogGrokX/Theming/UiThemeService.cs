using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using LogGrokX.Platform;

namespace LogGrokX.Theming
{
    public class UiThemeService : IThemeService
    {
        public const string LightTheme = "Light";
        public const string DarkTheme = "Dark";

        private readonly string _storeFileName =
            HomeDirectoryPathProvider.GetDataFileFullPath("Theme.json");
        private AccentColorKind _accentColor = AccentColorKind.Blue;

        public UiThemeService()
        {
            CurrentTheme = Load();
        }

        public string CurrentTheme { get; private set; }

        public event EventHandler? AccentColorChanged;

        public bool IsDark => CurrentTheme.StartsWith("Dark", StringComparison.OrdinalIgnoreCase);

        public void ApplySavedTheme(AccentColorKind accentColor = AccentColorKind.Blue)
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
            ["CheckBoxCheckBackgroundFillCheckedPressed"] = "AccentFillColorTertiary",
            // WPF UI theme brushes that take their color from the accent through DynamicResource
            // are not refreshed when the accent changes, only when the theme dictionary is reloaded.
            ["CalendarViewSelectedBackground"] = "SystemAccentColorPrimary",
            ["CalendarViewSelectedBorderBrush"] = "SystemAccentColorPrimary",
            ["CalendarViewTodayBackground"] = "SystemAccentColorPrimary",
            ["ComboBoxBorderBrushFocused"] = "SystemAccentColorSecondary",
            ["ComboBoxItemPillFillBrush"] = "SystemAccentColorPrimary",
            ["InfoBarInformationalSeverityIconBackground"] = "SystemAccentColorPrimary",
            ["ListBoxItemSelectedBackgroundThemeBrush"] = "SystemAccentColorPrimary",
            ["ListViewItemPillFillBrush"] = "SystemAccentColorPrimary",
            ["NavigationViewSelectionIndicatorForeground"] = "SystemAccentColorPrimary",
            ["ProgressBarForeground"] = "SystemAccentColorPrimary",
            ["ProgressRingForegroundThemeBrush"] = "SystemAccentColorPrimary",
            ["RadioButtonOuterEllipseCheckedStroke"] = "SystemAccentColorPrimary",
            ["RatingControlSelectedForeground"] = "SystemAccentColorPrimary",
            ["SliderThumbBackground"] = "SystemAccentColorPrimary",
            ["TextControlFocusedBorderBrush"] = "SystemAccentColorPrimary",
            ["ThumbRateForeground"] = "SystemAccentColorPrimary",
            ["ToggleButtonBackgroundChecked"] = "SystemAccentColorPrimary",
            ["ToggleButtonBackgroundCheckedPressed"] = "SystemAccentColorTertiary",
            ["ToggleButtonForegroundCheckedPointerOver"] = "SystemAccentColorSecondary",
            ["ToggleSwitchFillOn"] = "SystemAccentColorPrimary",
            ["ToggleSwitchStrokeOn"] = "SystemAccentColorPrimary",
            ["TreeViewItemSelectionIndicatorForeground"] = "SystemAccentColorPrimary"
        };

        private static readonly System.Collections.Generic.Dictionary<string, (string Light, string Dark)> ThemeAccentBrushColorKeys = new()
        {
            ["HyperlinkButtonForeground"] = ("SystemAccentColorSecondary", "SystemAccentColorTertiary"),
            ["HyperlinkButtonForegroundPointerOver"] = ("SystemAccentColorTertiary", "SystemAccentColorTertiary"),
            ["HyperlinkButtonForegroundPressed"] = ("SystemAccentColorPrimary", "SystemAccentColorSecondary")
        };

        public void ApplyAccentColor(AccentColorKind accentColor)
        {
            _accentColor = AccentColorPalette.Normalize(accentColor);
            try
            {
                var previousBrushes = CaptureAccentBrushes(Application.Current.Resources);
                ApplicationAccentColorManager.Apply(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(AccentColorPalette.GetColor(_accentColor)),
                    ApplicationThemeManager.GetAppTheme());
                ApplyDerivedAccentBrushes(Application.Current.Resources, previousBrushes,
                    ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark);
                foreach (var resources in Application.Current.Resources.MergedDictionaries.OfType<DockThemeResources>())
                    resources.ApplyAccentColor();
                DockThemes.ApplyAccentColor();
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
            foreach (var brushKey in AccentBrushColorKeys.Keys.Concat(ThemeAccentBrushColorKeys.Keys))
            {
                if (resources[brushKey] is System.Windows.Media.SolidColorBrush { IsFrozen: false } brush)
                    brushes[brushKey] = brush;
            }

            return brushes;
        }

        internal static void ApplyDerivedAccentBrushes(ResourceDictionary resources,
            System.Collections.Generic.IReadOnlyDictionary<string, System.Windows.Media.SolidColorBrush>? previousBrushes = null,
            bool isDark = false)
        {
            foreach (var (brushKey, colorKey) in AccentBrushColorKeys)
                SetBrush(resources, brushKey, colorKey, previousBrushes);
            foreach (var (brushKey, (light, dark)) in ThemeAccentBrushColorKeys)
                SetBrush(resources, brushKey, isDark ? dark : light, previousBrushes);
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
