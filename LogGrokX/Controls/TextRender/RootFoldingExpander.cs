using System;
using System.Windows;
using System.Windows.Media;

namespace LogGrokX.Controls.TextRender;

public class RootFoldingExpander : OutlineExpander
{
    public static readonly DependencyProperty TextModelProperty = DependencyProperty.Register(
        nameof(TextModel), typeof(TextModel), typeof(RootFoldingExpander),
        new FrameworkPropertyMetadata(null, OnTextModelChanged));

    public static readonly DependencyProperty SharedFoldingStateProperty = DependencyProperty.Register(
        nameof(SharedFoldingState), typeof(TextViewSharedFoldingState), typeof(RootFoldingExpander),
        new FrameworkPropertyMetadata(null, OnSharedFoldingStateChanged));

    public TextModel? TextModel
    {
        get => (TextModel?)GetValue(TextModelProperty);
        set => SetValue(TextModelProperty, value);
    }

    public TextViewSharedFoldingState? SharedFoldingState
    {
        get => (TextViewSharedFoldingState?)GetValue(SharedFoldingStateProperty);
        set => SetValue(SharedFoldingStateProperty, value);
    }

    public RootFoldingExpander()
    {
        Width = 8;
        Height = 8;
        Foreground = Brushes.Gray;
        WeakEventManager<FoldingLayoutOptions, EventArgs>.AddHandler(
            FoldingLayoutOptions.Instance, nameof(FoldingLayoutOptions.Changed), OnFoldingLayoutChanged);
        UpdateState();
    }

    internal void ToggleRoot()
    {
        if (TextModel is { } model && SharedFoldingState is { } state)
            state.ToggleRoot(model);
    }

    protected override void OnClick()
    {
        ToggleRoot();
        base.OnClick();
    }

    private static void OnTextModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((RootFoldingExpander)d).UpdateState();
    }

    private static void OnSharedFoldingStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var expander = (RootFoldingExpander)d;
        if (e.OldValue is TextViewSharedFoldingState oldState)
        {
            WeakEventManager<TextViewSharedFoldingState, EventArgs>.RemoveHandler(
                oldState, nameof(TextViewSharedFoldingState.Changed), expander.OnSharedFoldingStateChanged);
        }
        if (e.NewValue is TextViewSharedFoldingState newState)
        {
            WeakEventManager<TextViewSharedFoldingState, EventArgs>.AddHandler(
                newState, nameof(TextViewSharedFoldingState.Changed), expander.OnSharedFoldingStateChanged);
        }
        expander.UpdateState();
    }

    private void OnFoldingLayoutChanged(object? sender, EventArgs e) => UpdateState();

    private void OnSharedFoldingStateChanged(object? sender, EventArgs e) => UpdateState();

    private void UpdateState()
    {
        if (!Dispatcher.CheckAccess()) return;

        var model = TextModel;
        var state = SharedFoldingState;
        var visible = FoldingLayoutOptions.Instance.Current == FoldingLayoutMode.RootInPreviousColumn &&
                      model?.RootFoldingLine != null && state != null;
        Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible) return;

        State = state!.IsRootCollapsed(model!)
            ? OutlineExpanderState.Collapsed
            : OutlineExpanderState.ExpandedUpper;
    }
}
