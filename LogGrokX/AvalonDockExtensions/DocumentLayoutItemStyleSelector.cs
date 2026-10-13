using System.Windows;
using System.Windows.Controls;

namespace LogGrokX.AvalonDockExtensions;

public sealed class DocumentLayoutItemStyleSelector : StyleSelector
{
    public Style? DocumentStyle { get; set; }

    public Style? DefaultStyle { get; set; }

    public override Style? SelectStyle(object? item, DependencyObject container) =>
        item is ContentControl { Content: DocumentViewModel } ? DocumentStyle : DefaultStyle;
}
