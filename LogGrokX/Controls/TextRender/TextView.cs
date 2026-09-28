using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using LogGrokX.Data;
using LogGrokX.Diagnostics;

namespace LogGrokX.Controls.TextRender;

public class TextView : Control, IClippingRectChangesAware
{
    private const int MaxLineLength = 8192;  
    private class OutlineData
    {
        public OutlineData(int lineCount, 
            (int start, int length)[] collapsibleRegions,
            Func<HashSet<int>?> collapsedLineIndicesGetter)
        {
            CollapsibleRegionsMachine = new CollapsibleRegionsMachine(lineCount, collapsibleRegions, collapsedLineIndicesGetter);
            CollapsibleLines = new bool[lineCount];

            var depthDelta = new int[lineCount + 1];
            foreach (var (start, length) in collapsibleRegions)
            {
                var from = Math.Clamp(start, 0, lineCount);
                var to = Math.Clamp(start + length, 0, lineCount);
                if (from >= to) continue;
                depthDelta[from]++;
                depthDelta[to]--;
            }

            var depth = 0;
            for (var i = 0; i < lineCount; i++)
            {
                depth += depthDelta[i];
                CollapsibleLines[i] = depth > 0;
            }
        }
        
        public readonly CollapsibleRegionsMachine CollapsibleRegionsMachine;
        public readonly bool[] CollapsibleLines;
        public bool IsCollapsible(int index) => (uint)index < (uint)CollapsibleLines.Length && CollapsibleLines[index];
        public readonly Dictionary<int, Rect> ChildrenRectangles = new();
        public Dictionary<int, OutlineExpander> ChildrenByPosition = new();
    }

    private OutlineData? _outlineData;

    private GlyphLine?[]? _textLines;
    private bool[]? _textLineCollapsedStates;
    private readonly Lazy<GlyphTypeface> _glyphTypeface;
    private const double ExpanderSize = 8;
    public const double ExpanderMargin = 12;

    private UIElementCollection? _children;

    private static readonly Brush OutlineBrush = Brushes.Gray;
    private readonly TextControl _textControl;
    private GuideLinesControl? _guideLinesControl;
    
    private double _cachedWidth;
    private double _cachedFontSize;
    private TextModel? _cachedTextModel;
    private bool _isCollapsibleStateDirty;
    private TextViewSharedFoldingState? _registeredFoldingState;
    private bool _suppressFoldingNotification;
    private FrameworkElement? _clippingRectProvider;

    private UIElementCollection Children
    {
        get
        {
            if (_children != null) return _children;
            _children = new UIElementCollection(this, this) { _textControl };
            return _children;
        }
    }

    private static readonly Dictionary<(FontFamily, FontStyle, FontWeight, FontStretch), GlyphTypeface>
        TypefaceCache = new();

    #region HiglightRegex property

    public static DependencyProperty HighlightRegex = DependencyProperty.RegisterAttached(
        nameof(HighlightRegex),
        typeof(Regex),
        typeof(TextView),
        new FrameworkPropertyMetadata(null,
            FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender,
            static (t, _) => (t as TextView)?._textControl.InvalidateVisual())
    );

    public static Regex? GetHighlightRegex(DependencyObject? d)
    {
        if (d == null) throw new NullReferenceException(nameof(d));
        return d.GetValue(HighlightRegex) as Regex;
    }

    public static void SetHighlightRegex(DependencyObject? d, Regex value)
    {
        if (d == null) throw new NullReferenceException(nameof(d));
        d.SetValue(HighlightRegex, value);
    }

    #endregion

    #region TextModel property

    public static readonly DependencyProperty TextModelProperty = DependencyProperty.Register(
        "TextModel", typeof(TextModel), typeof(TextView), 
        new FrameworkPropertyMetadata(null,
            FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure,
            OnTextModelChanged));

    public TextModel? TextModel
    {
        get => (TextModel?)GetValue(TextModelProperty);
        set => SetValue(TextModelProperty, value);
    }

    private HashSet<int>? GetSharedFoldingState()
    {
        if (SharedFoldingState is not { } sharedState || TextModel is not { } textModel) 
            return null;
        
        if (sharedState[textModel.UniqueId] is { } settings) 
            return settings;
        
        if (textModel is not
            {
                CollapsibleRanges: {} collapsibleRanges,
                Count: var totalLineCount
            })
            return null;

        settings = sharedState.GetDefaultSettings(collapsibleRanges, totalLineCount);
        sharedState[textModel.UniqueId] = settings;
        return settings;
    }
    
    private static void OnTextModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextView textView)
            return;
        
        textView.ResetText();

        textView.SetCollapsibleRanges(textView.TextModel?.CollapsibleRanges);
    }

    void SetCollapsibleRanges(List<(int start, int length)>? collapsibleRanges)
    {
        _isCollapsibleStateDirty = true;

        var sharedFoldingState = SharedFoldingState;
        UpdateFoldingStateRegistration(collapsibleRanges == null ? null : sharedFoldingState);

        if (collapsibleRanges == null)
        {
            _outlineData = null;
            FoldingManager = null;
            return;
        }

        var count = TextModel?.Count ?? 0;
        var collapsibleRangesArray = collapsibleRanges.ToArray();
        _outlineData = new OutlineData(
            count, 
            collapsibleRangesArray,
            GetSharedFoldingState);
        
        _outlineData.CollapsibleRegionsMachine.Changed += () =>
        {
            _isCollapsibleStateDirty = true;
            InvalidateMeasure();
            InvalidateVisual();
            if (!_suppressFoldingNotification)
                SharedFoldingState?.NotifyChanged(this);
        };

        if (sharedFoldingState is not { } state)
        {
            FoldingManager = null;
            return;
        }

        InvalidateMeasure();

        FoldingManager = new FoldingManager(
            _outlineData.CollapsibleRegionsMachine,
            state,
            () => state.GetDefaultFoldingSettings(collapsibleRangesArray, count));
    }

    private void UpdateFoldingStateRegistration(TextViewSharedFoldingState? state)
    {
        if (ReferenceEquals(_registeredFoldingState, state))
            return;

        _registeredFoldingState?.Unregister(this);
        _registeredFoldingState = state;
        state?.Register(this);
    }

    internal void OnSharedFoldingStateChanged()
    {
        if (_outlineData is not { } outlineData || TextModel is not { } textModel)
            return;

        if (SharedFoldingState is not { } state || state[textModel.UniqueId] is not { } collapsedLines)
            return;

        _suppressFoldingNotification = true;
        try
        {
            outlineData.CollapsibleRegionsMachine.UpdateCollapsedLines(collapsedLines);
        }
        finally
        {
            _suppressFoldingNotification = false;
        }

        _isCollapsibleStateDirty = true;
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void OnSharedFoldingStatePropertyChanged()
    {
        if (TextModel is { } textModel)
            SetCollapsibleRanges(textModel.CollapsibleRanges);
    }

    #endregion

    #region SelectionBrush property

    public static readonly DependencyProperty SelectionBrushProperty =
        TextBoxBase.SelectionBrushProperty.AddOwner(typeof(TextView),
            new PropertyMetadata(GetDefaultSelectionTextBrush()));

    public Brush? SelectionBrush
    {
        get => (Brush)GetValue(SelectionBrushProperty);
        set => SetValue(SelectionBrushProperty, value);
    }

    #endregion

    #region HighlightBrush property

    public static readonly DependencyProperty HighlightBrushProperty = DependencyProperty.Register(
        nameof(HighlightBrush), typeof(Brush), typeof(TextView),
        new FrameworkPropertyMetadata(Brushes.Moccasin, FrameworkPropertyMetadataOptions.AffectsRender,
            static (d, _) => (d as TextView)?._textControl.InvalidateVisual()));

    public Brush HighlightBrush
    {
        get => (Brush)GetValue(HighlightBrushProperty);
        set => SetValue(HighlightBrushProperty, value);
    }

    #endregion

    #region SelectedText property

    public static readonly DependencyProperty SelectedTextProperty = DependencyProperty.Register(
        nameof(SelectedText), typeof(string), typeof(TextView),
        new PropertyMetadata(string.Empty));

    public string SelectedText
    {
        get => (string)GetValue(SelectedTextProperty);
        set => SetValue(SelectedTextProperty, value);
    }

    #endregion

    #region TransientSettings property
    
    public static readonly DependencyProperty SharedFoldingStateProperty = DependencyProperty.RegisterAttached(
        "SharedFoldingState", typeof(TextViewSharedFoldingState), typeof(TextView), 
            new FrameworkPropertyMetadata(default(TextViewSharedFoldingState), 
                FrameworkPropertyMetadataOptions.Inherits,
                static (d, _) => (d as TextView)?.OnSharedFoldingStatePropertyChanged()));

    public static void SetSharedFoldingState(DependencyObject element, TextViewSharedFoldingState value)
    {
        element.SetValue(SharedFoldingStateProperty, value);
    }

    public static TextViewSharedFoldingState? GetSharedFoldingState(DependencyObject element)
    {
        return (TextViewSharedFoldingState)element.GetValue(SharedFoldingStateProperty);
    }

    private TextViewSharedFoldingState? SharedFoldingState => GetSharedFoldingState(this);
    
    #endregion

    public static readonly DependencyProperty FoldingManagerProperty = DependencyProperty.Register(
        "FoldingManager", typeof(FoldingManager), typeof(TextView), 
        new FrameworkPropertyMetadata(default(FoldingManager)));

    public FoldingManager? FoldingManager
    {
        get => (FoldingManager?)GetValue(FoldingManagerProperty);
        set => SetValue(FoldingManagerProperty, value);
    }
    
    public TextView()
    {
        _textControl = new TextControl(this);
        _glyphTypeface = new Lazy<GlyphTypeface>(CreateGlyphTypeface);
    }

    static TextView()
    {
        ForegroundProperty.OverrideMetadata(typeof(TextView), new FrameworkPropertyMetadata(null,
            FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.Inherits,
            static (d, _) => (d as TextView)?._textControl.InvalidateVisual()));
        
        void CopySelectedTextHandler(object sender, ExecutedRoutedEventArgs args)
        {
            var text = ((TextView)sender).SelectedText;
            TextCopy.ClipboardService.SetText(text);
            args.Handled = true;
        }

        void CanCopySelectedTextHandler(object sender, CanExecuteRoutedEventArgs args)
        {
            if (sender is not TextView selectableTextBlock) return;
            var haveSelectedText = !string.IsNullOrEmpty(selectableTextBlock.SelectedText);
            args.CanExecute = haveSelectedText;
            args.Handled = haveSelectedText;
        }

        foreach (var command in new RoutedCommand[]
                 {
                     RoutedCommands.CopyToClipboard,
                     RoutedCommands.CopyAsDisplayed
                 })
        {
            CommandManager.RegisterClassCommandBinding(typeof(TextView),
                new CommandBinding(command, CopySelectedTextHandler, CanCopySelectedTextHandler));
        }
    }

    protected override int VisualChildrenCount => _children?.Count ?? 0;

    protected override Visual GetVisualChild(int index)
    {
        if (_children == null)
            throw new InvalidOperationException();
        return _children[index];
    }

    private OutlineExpander AddAndMeasureChild(Outline outline, int index, OutlineData outlineData)
    {
        var expanderState = outline switch
        {
            Collapsed => OutlineExpanderState.Collapsed,
            ExpandedUpper => OutlineExpanderState.ExpandedUpper,
            ExpandedLower => OutlineExpanderState.ExpandedLower,
            _ => throw new NotSupportedException()
        };

        var newChildCreated = false;
        if (!outlineData.ChildrenByPosition.TryGetValue(index, out var expander))
        {
            expander = new OutlineExpander() {Foreground = OutlineBrush};
            newChildCreated = true;
        }

        expander.State = expanderState;
        expander.Expandable = outline as Expandable;

        if (newChildCreated)
            Children.Add(expander);
        
        expander.Measure(new Size(ExpanderSize, double.PositiveInfinity));
        outlineData.ChildrenByPosition[index] = expander;
        return expander;
    }

    private Rect? GetClippingRect()
    {
        _clippingRectProvider ??= ClippingRectProviderBehavior.GetClippingRectProvider(this);
        if (_clippingRectProvider is not {} clippingRectProvider)
            return null;
        
        return ClippingRectProviderBehavior.GetClippingRect(clippingRectProvider, this);
    }

    protected override Size MeasureOverride(Size constraint)
    {
        var perfStart = Stopwatch.GetTimestamp();
        var allocStart = PerfProbe.AllocMark();
        var text = TextModel;
        if (text == null)
        {
            PerfProbe.RecordTextMeasure(perfStart);
            return new Size(0, 0);
        }

        if (_textLines == null || _cachedTextModel != text || _cachedWidth < constraint.Width ||
            _isCollapsibleStateDirty || Math.Abs(_cachedFontSize - FontSize) > 0.001)
        { 
            var canReuseTextLines = _textLines != null && _cachedTextModel == text &&
                                    _cachedWidth.Equals(constraint.Width) &&
                                    Math.Abs(_cachedFontSize - FontSize) <= 0.001 &&
                                    _textLines.Length == text.Count;
            if (!canReuseTextLines)
            {
                ResetText();
                _textLines = new GlyphLine?[text.Count];
                _textLineCollapsedStates = new bool[text.Count];
            }

            _cachedWidth = constraint.Width;
            _cachedFontSize = FontSize;
            _cachedTextModel = text;
            _isCollapsibleStateDirty = false;

            if (text.CollapsibleRanges == null && _guideLinesControl != null)
            {
                Children.Remove(_guideLinesControl);
                _guideLinesControl = null;
            }

            if (text.CollapsibleRanges != null && _guideLinesControl == null)
            {
                _guideLinesControl = new GuideLinesControl();
                Children.Add(_guideLinesControl);
            }

            _textControl.TextLines = CreateVisibleTextLines(text);
        }

        _textControl.Measure(constraint);

        var measuredSize = new Size(_textControl.DesiredSize.Width, _textControl.DesiredSize.Height);
        PerfProbe.RecordAlloc("textMeasure", allocStart, PerfProbe.AllocMark());
        PerfProbe.RecordTextMeasure(perfStart);
        return measuredSize;
    }
    
    protected override Size ArrangeOverride(Size arrangeBounds)
    {
        if (_textLines == null) return arrangeBounds;

        var allocStart = PerfProbe.AllocMark();
        var textControlRect =
            new Rect(0, 0, _textControl.DesiredSize.Width, _textControl.DesiredSize.Height);
        _textControl.Arrange(textControlRect);

        RearrangeOutlineChildren(GetClippingRect(), arrangeBounds);
        PerfProbe.RecordAlloc("textArrange", allocStart, PerfProbe.AllocMark());
        return arrangeBounds;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var background = Background ?? Brushes.Transparent;
        drawingContext.DrawRectangle(background, new Pen(background, 0), new Rect(0, 0, ActualWidth, ActualHeight));
        base.OnRender(drawingContext);
    }

    private void RearrangeOutlineChildren(Rect? clippingRect, Size arrangeBounds)
    {
        if (_outlineData is not { } outlineData)
        {
            var children = Children;
            for (var i = children.Count - 1; i >= 0; i--)
            {
                if (children[i] is OutlineExpander)
                    children.RemoveAt(i);
            }
            return;
        }

        var (newChildren, newChildrenByPosition) =
            UpdateChildren(clippingRect, _outlineData);

        outlineData.ChildrenByPosition = newChildrenByPosition;

        var toDelete = Children.OfType<OutlineExpander>().Except(newChildren).ToList();
        var toAdd = newChildren.Except(Children.OfType<OutlineExpander>()).ToList();
        foreach (var outlineExpander in toDelete)
        {
            _children?.Remove(outlineExpander);
        }

        foreach (var outlineExpander in toAdd)
        {
            _children?.Add(outlineExpander);
        }

        foreach (var (index, expander) in outlineData.ChildrenByPosition)
        {
            expander.Arrange(outlineData.ChildrenRectangles[index]);
        }

        var childrenRectangles = _outlineData?.ChildrenRectangles;

        if (childrenRectangles is not { } || _guideLinesControl is not { } guideLinesControl)
        {
            return;
        }

        var sortedRectangles =
            childrenRectangles.OrderBy(kv => kv.Key)
                .Select(kv => kv.Value).ToList();

        guideLinesControl.Arrange(new 
            Rect(0, 0, ExpanderSize, arrangeBounds.Height));

        var newLines = new List<(double, double)>();
        for (var i = 1; i < sortedRectangles.Count; i++)
        {
            var prev = sortedRectangles[i - 1];
            var current = sortedRectangles[i];
            newLines.Add((prev.Bottom, current.Top));
        }

        if (!(guideLinesControl.Lines?.SequenceEqual(newLines) ?? false))
        {
            guideLinesControl.Lines = newLines;
        }
    }

    private (HashSet<OutlineExpander> newChildren, 
        Dictionary<int, OutlineExpander> newChildrenByPosition) UpdateChildren(Rect? clippingRect, OutlineData outlineData)
    {
        if (_textLines == null || TextModel is not { } textModel)
            throw new InvalidOperationException();
        
        double verticalPosition = 0;
        var pixelsPerDip = (float)VisualTreeHelper.GetDpi(this).PixelsPerDip;

        outlineData.ChildrenRectangles.Clear();
        HashSet<OutlineExpander> newChildren = new(); 
        Dictionary<int, OutlineExpander> newChildrenByPosition = new();
        
        for (var i = 0; i < outlineData.CollapsibleRegionsMachine.LineCount; i++)
        {
            var (outline, index) = outlineData.CollapsibleRegionsMachine[i];
            if ((uint)index >= (uint)_textLines.Length)
                break;
            var textLine = _textLines[index] ?? GetOrCreateTextLine(textModel, index, pixelsPerDip);
            var yCenter = Math.Round((verticalPosition + textLine.Size.Height / 2) * pixelsPerDip,
                MidpointRounding.ToEven) / pixelsPerDip;

            if (outline is not None)
            {
                var ySize = ExpanderSize;
                var xSize = ExpanderSize;
                var rect = new Rect(0, yCenter - ySize / 2, xSize, ySize);

                if (clippingRect == null ||
                    clippingRect is {} clip 
                        && (rect.IntersectsWith(clip) || clip.Contains(rect) || rect.Contains(clip)))
                {
                    var newChild = AddAndMeasureChild(outline, index, outlineData);
                    newChildren.Add(newChild);
                    newChildrenByPosition[index] = newChild;
                    var desiredSize = newChild.DesiredSize;
                    xSize = desiredSize.Width;
                    ySize = desiredSize.Height;
                }
                
                rect = new Rect(0, 
                    yCenter - ySize / 2, 
                    xSize, ySize);
                outlineData.ChildrenRectangles[index] = rect;
            }

            verticalPosition += textLine.AdvanceHeight;
        }

        return (newChildren, newChildrenByPosition);
    }
    
    private void ResetText()
    {
        if (_textLines != null)
        {
            foreach (var textLine in _textLines)
            {
                textLine?.Dispose();
            }
        }

        _textLines = null;
        _textLineCollapsedStates = null;
    }

    private List<(GlyphLine glyphLine, bool isCollapsible)> CreateVisibleTextLines(TextModel text)
    {
        var pixelsPerDip = (float)VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var outlineData = _outlineData;

        if (outlineData == null)
        {
            var allLines = new List<(GlyphLine glyphLine, bool isCollapsible)>(text.Count);
            for (var i = 0; i < text.Count; i++)
                allLines.Add((GetOrCreateTextLine(text, i, pixelsPerDip), false));
            return allLines;
        }

        var machine = outlineData.CollapsibleRegionsMachine;
        var visibleLines = new List<(GlyphLine glyphLine, bool isCollapsible)>(machine.LineCount);
        for (var i = 0; i < machine.LineCount; i++)
        {
            var (_, index) = machine[i];
            visibleLines.Add((GetOrCreateTextLine(text, index, pixelsPerDip), outlineData.IsCollapsible(index)));
        }

        return visibleLines;
    }

    private GlyphLine GetOrCreateTextLine(TextModel text, int index, float pixelsPerDip)
    {
        if (_textLines is not { } textLines || _textLineCollapsedStates is not { } collapsedStates)
            throw new InvalidOperationException();

        var isCollapsed = _outlineData?.CollapsibleRegionsMachine.IsCollapsed(index) ?? false;
        if (textLines[index] is { } cached && collapsedStates[index] == isCollapsed)
            return cached;

        textLines[index]?.Dispose();

        var lineText = TrimVeryLongLine(isCollapsed ? GetCollapsedLineText(text, index) : text[index]);
        var glyphLine = new GlyphLine(lineText, _glyphTypeface.Value, FontSize, pixelsPerDip, _cachedWidth);
        textLines[index] = glyphLine;
        collapsedStates[index] = isCollapsed;
        return glyphLine;
    }

    private static StringRange GetCollapsedLineText(TextModel text, int index)
    {
        if (text.GetCollapsedTextSubstitution(index) is { IsEmpty: false } substitution)
            return substitution;

        return StringRange.FromString(text[index].ToString().TrimEnd().TrimEnd('{') + "{...}");
    }

    private static StringRange TrimVeryLongLine(StringRange stringRange)
    {
        return stringRange.Length <= MaxLineLength ? stringRange : 
            StringRange.FromString(stringRange.Span[..MaxLineLength].ToString() + "...");
    }

    private GlyphTypeface CreateGlyphTypeface()
    {
        var key = (FontFamily, FontStyle, FontWeight, FontStretch);
        if (TypefaceCache.TryGetValue(key, out var glyphTypeface))
            return glyphTypeface;

        var typeface = new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);
        if (!typeface.TryGetGlyphTypeface(out glyphTypeface))
            throw new NotSupportedException();
        TypefaceCache[key] = glyphTypeface;
        return glyphTypeface;
    }

    private static Brush GetDefaultSelectionTextBrush()
    {
        SolidColorBrush solidColorBrush = new(SystemColors.HighlightColor);
        solidColorBrush.Freeze();
        return solidColorBrush;
    }

    public void OnChildRectChanged(Rect rect)
    {
        if (_outlineData == null)
            return;
        
        InvalidateArrange();
    }

    public void ResetFoldingToDefault()
    {
        if (TextModel is not
            {
                CollapsibleRanges: {} collapsibleRanges,
                Count: var totalLineCount
            } ||
            SharedFoldingState is not {} sharedFoldingState)
            return;
        var defaultSettings = sharedFoldingState.GetDefaultSettings(collapsibleRanges, totalLineCount);
        _suppressFoldingNotification = true;
        try
        {
            _outlineData?.CollapsibleRegionsMachine.UpdateCollapsedLines(defaultSettings);
        }
        finally
        {
            _suppressFoldingNotification = false;
        }
    }

    public override string ToString()
    {
        return TextModel?.ToString() ?? "<null>";
    }
}