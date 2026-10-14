using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using LogGrokX.Controls.ListControls;

namespace LogGrokX.Controls;

public sealed class LogScrollBarViewportSizeConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var viewport = values[0] is double value ? value : 0.0;
        if (values[1] is not double maximum || maximum <= 0
            || values[2] is not double height || height <= 0
            || values[3] is not ListView
            || values[4] is not double fontSize || values[5] is not FontFamily fontFamily)
            return viewport;

        var lineHeight = fontSize * fontFamily.LineSpacing;
        var extent = maximum + viewport;
        var visibleLines = Math.Min(height / lineHeight, Math.Max(0.5, extent - 1));

        // Track sizes the thumb as viewport / (maximum + viewport).
        return maximum * visibleLines / (extent - visibleLines);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
