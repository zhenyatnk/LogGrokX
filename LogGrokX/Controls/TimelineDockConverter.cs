using System;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;

namespace LogGrokX.Controls;

public sealed class TimelineDockConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Dock.Top : Dock.Bottom;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
