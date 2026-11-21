using System;

namespace LogGrokX.Controls.TextRender;

public sealed class FoldingLayoutOptions
{
    private bool _useMessageGutter = true;

    public static FoldingLayoutOptions Instance { get; } = new();

    public bool UseMessageGutter
    {
        get => _useMessageGutter;
        set
        {
            if (_useMessageGutter == value) return;
            _useMessageGutter = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? Changed;
}
