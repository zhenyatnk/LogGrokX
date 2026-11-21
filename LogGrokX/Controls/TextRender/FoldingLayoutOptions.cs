using System;

namespace LogGrokX.Controls.TextRender;

public enum FoldingLayoutMode
{
    FixedGutter,
    RootInPreviousColumn
}

public sealed class FoldingLayoutOptions
{
    private FoldingLayoutMode _current = FoldingLayoutMode.FixedGutter;

    public static FoldingLayoutOptions Instance { get; } = new();

    public FoldingLayoutMode Current
    {
        get => _current;
        set
        {
            if (_current == value) return;
            _current = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? Changed;
}
