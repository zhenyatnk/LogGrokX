using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace LogGrokX.Controls;

public sealed class ColorBrushConverter : IValueConverter
{
    private static readonly ConcurrentDictionary<string, Brush> Brushes = new(StringComparer.Ordinal);

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is string text ? GetBrush(text) : null;
    }

    public static Brush? GetBrush(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            return Brushes.GetOrAdd(text, color =>
            {
                var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
                brush.Freeze();
                return brush;
            });
        }
        catch (FormatException) { return null; }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
