using System.Windows;

namespace LogGrokX.Platform;

public sealed class WpfTextSizePresenter : ITextSizePresenter
{
    public void ApplyFontSize(double fontSize)
    {
        if (Application.Current is { } app)
            app.Resources[TextZoomService.FontSizeResourceKey] = fontSize;
    }
}
