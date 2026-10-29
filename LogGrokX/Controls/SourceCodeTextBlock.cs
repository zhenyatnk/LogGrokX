using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using LogGrokX.Data.Symbols;

namespace LogGrokX.Controls;

public class SourceCodeTextBlock : TextBlock
{
    public static readonly DependencyProperty CodeProperty = DependencyProperty.Register(
        nameof(Code), typeof(string), typeof(SourceCodeTextBlock), new PropertyMetadata(null, OnSourceChanged));

    public static readonly DependencyProperty TokensProperty = DependencyProperty.Register(
        nameof(Tokens), typeof(IReadOnlyList<SourceToken>), typeof(SourceCodeTextBlock), new PropertyMetadata(null, OnSourceChanged));

    private static readonly Brush[] LightPalette = CreatePalette(0x0000FF, 0x2B91AF, 0x808080, 0x008000, 0xA31515, 0x098658);
    private static readonly Brush[] DarkPalette = CreatePalette(0x569CD6, 0x4EC9B0, 0x9B9B9B, 0x57A64A, 0xD69D85, 0xB5CEA8);

    private bool? _isDark;

    public string? Code
    {
        get => (string?)GetValue(CodeProperty);
        set => SetValue(CodeProperty, value);
    }

    public IReadOnlyList<SourceToken>? Tokens
    {
        get => (IReadOnlyList<SourceToken>?)GetValue(TokensProperty);
        set => SetValue(TokensProperty, value);
    }

    static SourceCodeTextBlock()
    {
        ForegroundProperty.OverrideMetadata(typeof(SourceCodeTextBlock), new FrameworkPropertyMetadata(
            SystemColors.ControlTextBrush,
            FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender,
            OnForegroundChanged));
    }

    private static void OnForegroundChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var textBlock = (SourceCodeTextBlock)d;
        if (textBlock._isDark != null && textBlock.IsDark() != textBlock._isDark)
            textBlock.Rebuild();
    }

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((SourceCodeTextBlock)d).Rebuild();

    private void Rebuild()
    {
        Inlines.Clear();
        var code = Code ?? string.Empty;
        var tokens = Tokens;
        if (tokens == null)
        {
            Inlines.Add(new Run(code));
            return;
        }

        _isDark = IsDark();
        var palette = _isDark.Value ? DarkPalette : LightPalette;
        foreach (var token in tokens)
        {
            if (token.Start + token.Length > code.Length)
                break;
            var run = new Run(code.Substring(token.Start, token.Length));
            if (token.Kind != SourceTokenKind.Text)
                run.Foreground = palette[(int)token.Kind - 1];
            Inlines.Add(run);
        }
    }

    private bool IsDark() =>
        Foreground is SolidColorBrush { Color: var color } && 0.299 * color.R + 0.587 * color.G + 0.114 * color.B > 128;

    private static Brush[] CreatePalette(params int[] colors)
    {
        var brushes = new Brush[colors.Length];
        for (var i = 0; i < colors.Length; i++)
        {
            var brush = new SolidColorBrush(Color.FromRgb((byte)(colors[i] >> 16), (byte)(colors[i] >> 8), (byte)colors[i]));
            brush.Freeze();
            brushes[i] = brush;
        }

        return brushes;
    }
}
