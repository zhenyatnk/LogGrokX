using System;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;

namespace LogGrokX.Colors
{
    public class ColorSettings : ColorRuleSet
    {
        public static readonly DependencyProperty ColorSettingsProperty = DependencyProperty.RegisterAttached(
            "ColorSettings",
            typeof(ColorRuleSet),
            typeof(ColorSettings),
            new FrameworkPropertyMetadata(default(ColorRuleSet), FrameworkPropertyMetadataOptions.Inherits));

        public static void SetColorSettings(DependencyObject element, ColorRuleSet value)
        {
            element.SetValue(ColorSettingsProperty, value);
        }

        public static ColorRuleSet? GetColorSettings(DependencyObject element)
        {
            return element.GetValue(ColorSettingsProperty) as ColorRuleSet;
        }

        public class ColorRule
        {
            public ColorRule(Regex regex, Color? foreground, Color? background)
            {
                Regex = regex;
                ForegroundColor = foreground;
                BackgroundColor = background;
            }

            public Color? ForegroundColor { get; }
            public Color? BackgroundColor { get; }
            public Regex Regex { get; }

            public bool IsMatch(string text)
            {
                return Regex.IsMatch(text);
            }

            public Brush? GetForegroundBrush(bool isDark) => GetBrush(ForegroundColor, isDark, false);

            public Brush? GetBackgroundBrush(bool isDark) => GetBrush(BackgroundColor, isDark, true);
        }

        private static readonly ConcurrentDictionary<string, Color> CachedColors = new(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<(Color, bool, bool), Brush> CachedBrushes = new();

        private static Brush? GetBrush(Color? color, bool isDark, bool isBackground)
        {
            if (color == null) return null;
            return CachedBrushes.GetOrAdd((color.Value, isDark, isBackground),
                key => CreateBrush(key.Item1, key.Item2, key.Item3));
        }

        private static Brush CreateBrush(Color color, bool isDark, bool isBackground)
        {
            var brush = new SolidColorBrush(isDark ? AdjustForDarkTheme(color, isBackground) : color);
            brush.Freeze();
            return brush;
        }

        private static Color AdjustForDarkTheme(Color color, bool isBackground)
        {
            if (color.A == 0) return color;

            var luminance = (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255.0;

            if (isBackground)
            {
                const double target = 0.25;
                if (luminance <= target) return color;
                var scale = target / luminance;
                return Color.FromArgb(color.A,
                    (byte)Math.Round(color.R * scale),
                    (byte)Math.Round(color.G * scale),
                    (byte)Math.Round(color.B * scale));
            }

            const double targetLuminance = 0.5;
            if (luminance >= targetLuminance) return color;
            var factor = (targetLuminance - luminance) / (1.0 - luminance);
            byte Blend(byte c) => (byte)Math.Round(c + (255 - c) * factor);
            return Color.FromArgb(color.A, Blend(color.R), Blend(color.G), Blend(color.B));
        }

        public ColorSettings(Configuration.ColorSettings configuration)
            : base(configuration, new Platform.WpfColorValidator()) { }

        public static Brush? GetForegroundBrush(ColorRuleDefinition? rule, bool isDark) =>
            GetBrush(ParseColor(rule?.ForegroundColor), isDark, false);

        public static Brush? GetBackgroundBrush(ColorRuleDefinition? rule, bool isDark) =>
            GetBrush(ParseColor(rule?.BackgroundColor), isDark, true);

        private static Color? ParseColor(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : CachedColors.GetOrAdd(value, text => (Color)ColorConverter.ConvertFromString(text));
    }
}
