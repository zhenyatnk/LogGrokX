using System;
using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using LogGrokX.Data;

namespace LogGrokX.Controls
{
    public class LogMinimapControl : FrameworkElement
    {
        private enum DragHandle
        {
            None,
            Lower,
            Upper,
            Move
        }

        public static readonly DependencyProperty MarkersProperty = DependencyProperty.Register(
            nameof(Markers), typeof(IEnumerable), typeof(LogMinimapControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnMarkersChanged));

        public static readonly DependencyProperty MatchBucketsProperty = DependencyProperty.Register(
            nameof(MatchBuckets), typeof(IEnumerable), typeof(LogMinimapControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnMatchBucketsChanged));

        public static readonly DependencyProperty TimelineSegmentsProperty = DependencyProperty.Register(
            nameof(TimelineSegments), typeof(IEnumerable), typeof(LogMinimapControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ItemCountProperty = DependencyProperty.Register(
            nameof(ItemCount), typeof(int), typeof(LogMinimapControl),
            new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty DurationTextProperty = DependencyProperty.Register(
            nameof(DurationText), typeof(string), typeof(LogMinimapControl),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty MinTextProperty = DependencyProperty.Register(
            nameof(MinText), typeof(string), typeof(LogMinimapControl),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty MaxTextProperty = DependencyProperty.Register(
            nameof(MaxText), typeof(string), typeof(LogMinimapControl),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
            nameof(Minimum), typeof(double), typeof(LogMinimapControl),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
            nameof(Maximum), typeof(double), typeof(LogMinimapControl),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty LowerValueProperty = DependencyProperty.Register(
            nameof(LowerValue), typeof(double), typeof(LogMinimapControl),
            new FrameworkPropertyMetadata(0.0,
                FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public static readonly DependencyProperty UpperValueProperty = DependencyProperty.Register(
            nameof(UpperValue), typeof(double), typeof(LogMinimapControl),
            new FrameworkPropertyMetadata(0.0,
                FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public static readonly DependencyProperty IsRangeEnabledProperty = DependencyProperty.Register(
            nameof(IsRangeEnabled), typeof(bool), typeof(LogMinimapControl),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty TimeIndexProperty = DependencyProperty.Register(
            nameof(TimeIndex), typeof(TimeIndex), typeof(LogMinimapControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty MarkerBrushProperty = DependencyProperty.Register(
            nameof(MarkerBrush), typeof(Brush), typeof(LogMinimapControl),
            new FrameworkPropertyMetadata(Brushes.OrangeRed, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty MatchBrushProperty = DependencyProperty.Register(
            nameof(MatchBrush), typeof(Brush), typeof(LogMinimapControl),
            new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty DayBoundaryBrushProperty = DependencyProperty.Register(
            nameof(DayBoundaryBrush), typeof(Brush), typeof(LogMinimapControl),
            new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty MinimapBackgroundProperty = DependencyProperty.Register(
            nameof(MinimapBackground), typeof(Brush), typeof(LogMinimapControl),
            new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty SelectionBrushProperty = DependencyProperty.Register(
            nameof(SelectionBrush), typeof(Brush), typeof(LogMinimapControl),
            new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty DimBrushProperty = DependencyProperty.Register(
            nameof(DimBrush), typeof(Brush), typeof(LogMinimapControl),
            new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty HandleBrushProperty = DependencyProperty.Register(
            nameof(HandleBrush), typeof(Brush), typeof(LogMinimapControl),
            new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty DurationForegroundProperty = DependencyProperty.Register(
            nameof(DurationForeground), typeof(Brush), typeof(LogMinimapControl),
            new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty DurationBackgroundProperty = DependencyProperty.Register(
            nameof(DurationBackground), typeof(Brush), typeof(LogMinimapControl),
            new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty UseLineNumbersProperty = DependencyProperty.Register(
            nameof(UseLineNumbers), typeof(bool), typeof(LogMinimapControl),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ScrollPositionProperty = DependencyProperty.Register(
            nameof(ScrollPosition), typeof(int), typeof(LogMinimapControl),
            new FrameworkPropertyMetadata(-1, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ScrollIndicatorBrushProperty = DependencyProperty.Register(
            nameof(ScrollIndicatorBrush), typeof(Brush), typeof(LogMinimapControl),
            new FrameworkPropertyMetadata(Brushes.LimeGreen, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty MatchLineProperty = DependencyProperty.Register(
            nameof(MatchLine), typeof(int), typeof(LogMinimapControl),
            new FrameworkPropertyMetadata(-1, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty MatchLineBrushProperty = DependencyProperty.Register(
            nameof(MatchLineBrush), typeof(Brush), typeof(LogMinimapControl),
            new FrameworkPropertyMetadata(Brushes.Magenta, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty NavigateCommandProperty = DependencyProperty.Register(
            nameof(NavigateCommand), typeof(ICommand), typeof(LogMinimapControl),
            new PropertyMetadata(null));

        private static readonly Typeface LabelTypeface = new("Segoe UI");
        private const double LabelFontSize = 11.0;
        private const double HandleHitTolerance = 6.0;
        private const double HandleWidth = 5.0;
        private const double EdgeLabelPadding = 8.0;

        private INotifyCollectionChanged? _observableMarkers;
        private INotifyCollectionChanged? _observableMatchBuckets;

        private DragHandle _dragHandle;
        private double _dragLowerValue;
        private double _dragUpperValue;
        private int _dragStartLine;
        private int _dragEndLine;
        private double _dragStartX;
        private double _hoverX = -1;

        public IEnumerable? Markers
        {
            get => (IEnumerable?)GetValue(MarkersProperty);
            set => SetValue(MarkersProperty, value);
        }

        public IEnumerable? MatchBuckets
        {
            get => (IEnumerable?)GetValue(MatchBucketsProperty);
            set => SetValue(MatchBucketsProperty, value);
        }

        public IEnumerable? TimelineSegments
        {
            get => (IEnumerable?)GetValue(TimelineSegmentsProperty);
            set => SetValue(TimelineSegmentsProperty, value);
        }

        public int ItemCount
        {
            get => (int)GetValue(ItemCountProperty);
            set => SetValue(ItemCountProperty, value);
        }

        public string DurationText
        {
            get => (string)GetValue(DurationTextProperty);
            set => SetValue(DurationTextProperty, value);
        }

        public string MinText
        {
            get => (string)GetValue(MinTextProperty);
            set => SetValue(MinTextProperty, value);
        }

        public string MaxText
        {
            get => (string)GetValue(MaxTextProperty);
            set => SetValue(MaxTextProperty, value);
        }

        public double Minimum
        {
            get => (double)GetValue(MinimumProperty);
            set => SetValue(MinimumProperty, value);
        }

        public double Maximum
        {
            get => (double)GetValue(MaximumProperty);
            set => SetValue(MaximumProperty, value);
        }

        public double LowerValue
        {
            get => (double)GetValue(LowerValueProperty);
            set => SetValue(LowerValueProperty, value);
        }

        public double UpperValue
        {
            get => (double)GetValue(UpperValueProperty);
            set => SetValue(UpperValueProperty, value);
        }

        public bool IsRangeEnabled
        {
            get => (bool)GetValue(IsRangeEnabledProperty);
            set => SetValue(IsRangeEnabledProperty, value);
        }

        public TimeIndex? TimeIndex
        {
            get => (TimeIndex?)GetValue(TimeIndexProperty);
            set => SetValue(TimeIndexProperty, value);
        }

        public Brush MarkerBrush
        {
            get => (Brush)GetValue(MarkerBrushProperty);
            set => SetValue(MarkerBrushProperty, value);
        }

        public Brush MatchBrush
        {
            get => (Brush)GetValue(MatchBrushProperty);
            set => SetValue(MatchBrushProperty, value);
        }

        public Brush DayBoundaryBrush
        {
            get => (Brush)GetValue(DayBoundaryBrushProperty);
            set => SetValue(DayBoundaryBrushProperty, value);
        }

        public Brush MinimapBackground
        {
            get => (Brush)GetValue(MinimapBackgroundProperty);
            set => SetValue(MinimapBackgroundProperty, value);
        }

        public Brush SelectionBrush
        {
            get => (Brush)GetValue(SelectionBrushProperty);
            set => SetValue(SelectionBrushProperty, value);
        }

        public Brush DimBrush
        {
            get => (Brush)GetValue(DimBrushProperty);
            set => SetValue(DimBrushProperty, value);
        }

        public Brush HandleBrush
        {
            get => (Brush)GetValue(HandleBrushProperty);
            set => SetValue(HandleBrushProperty, value);
        }

        public Brush DurationForeground
        {
            get => (Brush)GetValue(DurationForegroundProperty);
            set => SetValue(DurationForegroundProperty, value);
        }

        public Brush DurationBackground
        {
            get => (Brush)GetValue(DurationBackgroundProperty);
            set => SetValue(DurationBackgroundProperty, value);
        }

        public bool UseLineNumbers
        {
            get => (bool)GetValue(UseLineNumbersProperty);
            set => SetValue(UseLineNumbersProperty, value);
        }

        public int ScrollPosition
        {
            get => (int)GetValue(ScrollPositionProperty);
            set => SetValue(ScrollPositionProperty, value);
        }

        public Brush ScrollIndicatorBrush
        {
            get => (Brush)GetValue(ScrollIndicatorBrushProperty);
            set => SetValue(ScrollIndicatorBrushProperty, value);
        }

        public int MatchLine
        {
            get => (int)GetValue(MatchLineProperty);
            set => SetValue(MatchLineProperty, value);
        }

        public Brush MatchLineBrush
        {
            get => (Brush)GetValue(MatchLineBrushProperty);
            set => SetValue(MatchLineBrushProperty, value);
        }

        public ICommand? NavigateCommand
        {
            get => (ICommand?)GetValue(NavigateCommandProperty);
            set => SetValue(NavigateCommandProperty, value);
        }

        protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters)
        {
            var point = hitTestParameters.HitPoint;
            return point.X >= 0 && point.X <= ActualWidth && point.Y >= 0 && point.Y <= ActualHeight
                ? new PointHitTestResult(this, point)
                : null;
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);

            var width = ActualWidth;
            var height = ActualHeight;
            if (width <= 0 || height <= 0)
                return;

            var background = MinimapBackground;
            if (background != null)
                drawingContext.DrawRectangle(background, null, new Rect(0, 0, width, height));

            DrawTimelineSegments(drawingContext, width, height);
            DrawMatchBuckets(drawingContext, width, height);
            DrawDayBoundaries(drawingContext, width, height);
            DrawMarkers(drawingContext, width, height);

            var rangeActive = IsRangeActive();
            var lowerX = 0.0;
            var upperX = width;
            if (rangeActive)
            {
                var (startLine, endLine) = GetRangeLines(GetEffectiveLower(), GetEffectiveUpper());
                lowerX = CenterOf(startLine, ItemCount, width);
                upperX = CenterOf(endLine, ItemCount, width);
                DrawSelection(drawingContext, width, height, lowerX, upperX);
            }

            DrawScrollIndicator(drawingContext, width, height);
            DrawMatchLine(drawingContext, width, height);
            DrawRangeLabels(drawingContext, width, height, rangeActive, lowerX, upperX);
            DrawHoverTime(drawingContext, width, height);
        }

        private void DrawHoverTime(DrawingContext drawingContext, double width, double height)
        {
            var count = ItemCount;
            if (_hoverX < 0 || _hoverX > width || count <= 0)
                return;

            var line = Math.Clamp(XToValueIndex(_hoverX), 0, count - 1);
            var text = GetHoverText(line);
            if (string.IsNullOrEmpty(text))
                return;

            var foreground = DurationForeground;
            var guideBrush = foreground ?? Brushes.Gray;
            var guideLeft = Math.Clamp(_hoverX - 0.5, 0, Math.Max(0, width - 1));
            drawingContext.PushOpacity(0.6);
            drawingContext.DrawRectangle(guideBrush, null, new Rect(guideLeft, 0, 1, height));
            drawingContext.Pop();

            var formatted = CreateText(text, foreground ?? Brushes.White);
            if (formatted == null)
                return;

            const double gap = 6.0;
            var left = _hoverX + gap;
            if (left + formatted.Width + 2 > width)
                left = _hoverX - gap - formatted.Width;
            left = Math.Clamp(left, 2, Math.Max(2, width - formatted.Width - 2));

            var top = Math.Max(0, (height - formatted.Height) / 2);
            var background = MinimapBackground ?? Brushes.Black;
            var rect = new Rect(left - 3, top - 1, formatted.Width + 6, formatted.Height + 2);
            drawingContext.DrawRoundedRectangle(background, new Pen(guideBrush, 1), rect, 2, 2);
            drawingContext.DrawText(formatted, new Point(left, top));
        }

        private string GetHoverText(int line)
        {
            var timeIndex = TimeIndex;
            if (!UseLineNumbers && timeIndex is { HasTime: true } && timeIndex.Count > 0)
            {
                var ticks = timeIndex.GetTicksAt(Math.Clamp(line, 0, timeIndex.Count - 1));
                return TimestampParser.Format(ticks);
            }

            return $"Line {(line + 1).ToString(CultureInfo.InvariantCulture)}";
        }

        private void DrawMatchLine(DrawingContext drawingContext, double width, double height)
        {
            var count = ItemCount;
            var line = MatchLine;
            if (count <= 0 || line < 0 || line >= count)
                return;

            var brush = MatchLineBrush;
            if (brush == null)
                return;

            var center = CenterOf(line, count, width);
            var indicatorWidth = 2.0;
            var left = Math.Clamp(center - indicatorWidth / 2, 0, Math.Max(0, width - indicatorWidth));
            drawingContext.DrawRectangle(brush, null, new Rect(left, 0, indicatorWidth, height));

            var half = 5.0;
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(new Point(center - half, 0), true, true);
                context.LineTo(new Point(center + half, 0), true, false);
                context.LineTo(new Point(center, 9), true, false);
            }

            geometry.Freeze();
            drawingContext.DrawGeometry(brush, null, geometry);
        }

        private void DrawScrollIndicator(DrawingContext drawingContext, double width, double height)
        {
            var count = ItemCount;
            var position = ScrollPosition;
            if (count <= 0 || position < 0 || position >= count)
                return;

            var brush = ScrollIndicatorBrush;
            if (brush == null)
                return;

            var indicatorWidth = 2.0;
            var center = CenterOf(position, count, width);
            var left = Math.Clamp(center - indicatorWidth / 2, 0, Math.Max(0, width - indicatorWidth));
            drawingContext.DrawRectangle(brush, null, new Rect(left, 0, indicatorWidth, height));
        }

        private void DrawTimelineSegments(DrawingContext drawingContext, double width, double height)
        {
            var segments = TimelineSegments;
            var count = ItemCount;
            if (segments == null || count <= 0)
                return;

            foreach (var item in segments)
            {
                if (item is not TimelineSegment segment)
                    continue;

                var start = Math.Clamp(segment.StartLine, 0, count);
                var end = Math.Clamp(segment.EndLine, start, count);
                if (end <= start)
                    continue;

                var left = CenterOf(start, count, width);
                var right = CenterOf(end, count, width);
                drawingContext.DrawRectangle(segment.Brush, null, new Rect(left, 0, Math.Max(1, right - left), height));
            }
        }

        private void DrawMatchBuckets(DrawingContext drawingContext, double width, double height)
        {
            var buckets = MatchBuckets;
            if (buckets == null)
                return;

            var count = 0;
            foreach (var _ in buckets)
                count++;

            if (count <= 0)
                return;

            var barWidth = Math.Max(1.0, width / count);
            var i = 0;
            foreach (var bucket in buckets)
            {
                if (bucket is true)
                {
                    var left = i * width / count;
                    drawingContext.DrawRectangle(MatchBrush, null, new Rect(left, 0, barWidth, height));
                }

                i++;
            }
        }

        private void DrawDayBoundaries(DrawingContext drawingContext, double width, double height)
        {
            if (UseLineNumbers)
                return;

            var timeIndex = TimeIndex;
            var count = ItemCount;
            if (timeIndex == null || count <= 0)
                return;

            var brush = DayBoundaryBrush;
            if (brush == null)
                return;

            var lineWidth = 1.0;
            foreach (var index in timeIndex.DayBoundaries)
            {
                if (index < 0 || index >= count)
                    continue;

                var center = CenterOf(index, count, width);
                var left = Math.Clamp(center - lineWidth / 2, 0, Math.Max(0, width - lineWidth));
                drawingContext.DrawRectangle(brush, null, new Rect(left, 0, lineWidth, height));

                var half = 4.0;
                var geometry = new StreamGeometry();
                using (var context = geometry.Open())
                {
                    context.BeginFigure(new Point(center - half, 0), true, true);
                    context.LineTo(new Point(center + half, 0), true, false);
                    context.LineTo(new Point(center, 8), true, false);
                }

                geometry.Freeze();
                drawingContext.DrawGeometry(brush, null, geometry);
            }
        }

        private void DrawMarkers(DrawingContext drawingContext, double width, double height)
        {
            var markers = Markers;
            var count = ItemCount;
            if (markers == null || count <= 0)
                return;

            var markerWidth = Math.Min(2.0, width);
            foreach (var item in markers)
            {
                if (item is not int index || index < 0 || index >= count)
                    continue;

                var center = CenterOf(index, count, width);
                var left = Math.Clamp(center - markerWidth / 2, 0, Math.Max(0, width - markerWidth));
                drawingContext.DrawRectangle(MarkerBrush, null, new Rect(left, 0, markerWidth, height));
            }
        }
        private void DrawSelection(DrawingContext drawingContext, double width, double height, double lowerX, double upperX)
        {
            var dim = DimBrush;
            if (dim != null)
            {
                if (lowerX > 0)
                    drawingContext.DrawRectangle(dim, null, new Rect(0, 0, lowerX, height));
                if (upperX < width)
                    drawingContext.DrawRectangle(dim, null, new Rect(upperX, 0, width - upperX, height));
            }

            var selection = SelectionBrush;
            if (selection != null && upperX > lowerX)
                drawingContext.DrawRectangle(selection, null, new Rect(lowerX, 0, upperX - lowerX, height));

            var handleBrush = HandleBrush;
            var handleHeight = Math.Max(1.0, height - 2);
            var top = (height - handleHeight) / 2;
            DrawHandle(drawingContext, handleBrush, lowerX, top, handleHeight, width);
            DrawHandle(drawingContext, handleBrush, upperX, top, handleHeight, width);
        }

        private void DrawHandle(DrawingContext drawingContext, Brush brush, double centerX, double top, double handleHeight, double width)
        {
            var left = Math.Clamp(centerX - HandleWidth / 2, 0, Math.Max(0, width - HandleWidth));
            drawingContext.DrawRoundedRectangle(brush, null, new Rect(left, top, HandleWidth, handleHeight), 1.5, 1.5);
        }

        private void DrawRangeLabels(
            DrawingContext drawingContext,
            double width,
            double height,
            bool rangeActive,
            double lowerX,
            double upperX)
        {
            if (!rangeActive)
                return;

            var foreground = DurationForeground;
            var background = DurationBackground;

            DrawLabel(drawingContext, MinText, foreground, background, EdgeLabelPadding, height, FlowDirection.LeftToRight);

            var maxText = CreateText(MaxText, foreground);
            if (maxText != null)
            {
                DrawLabel(drawingContext, maxText, background,
                    Math.Max(EdgeLabelPadding, width - maxText.Width - EdgeLabelPadding), height);
            }

            var duration = GetDurationText();
            if (!string.IsNullOrEmpty(duration))
            {
                var text = CreateText(duration, foreground);
                if (text != null)
                {
                    var center = (lowerX + upperX) / 2;
                    var left = Math.Clamp(center - text.Width / 2, EdgeLabelPadding, Math.Max(EdgeLabelPadding, width - text.Width - EdgeLabelPadding));
                    DrawLabel(drawingContext, text, background, left, height);
                }
            }
        }

        private void DrawLabel(
            DrawingContext drawingContext,
            string text,
            Brush foreground,
            Brush background,
            double left,
            double height,
            FlowDirection flowDirection = FlowDirection.LeftToRight)
        {
            if (string.IsNullOrEmpty(text))
                return;

            var formatted = CreateText(text, foreground, flowDirection);
            if (formatted == null)
                return;

            DrawLabel(drawingContext, formatted, background, left, height);
        }

        private void DrawLabel(DrawingContext drawingContext, FormattedText formatted, Brush background, double left, double height)
        {
            var top = Math.Max(0, (height - formatted.Height) / 2);

            if (background != null)
            {
                var rect = new Rect(
                    Math.Max(0, left - 2),
                    top,
                    Math.Min(ActualWidth, formatted.Width + 4),
                    formatted.Height);
                drawingContext.DrawRectangle(background, null, rect);
            }

            drawingContext.DrawText(formatted, new Point(left, top));
        }

        private static FormattedText? CreateText(string text, Brush brush, FlowDirection flowDirection = FlowDirection.LeftToRight)
        {
            if (string.IsNullOrEmpty(text))
                return null;

            return new FormattedText(
                text,
                CultureInfo.InvariantCulture,
                flowDirection,
                LabelTypeface,
                LabelFontSize,
                brush,
                1.0);
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);

            if (IsRangeActive())
            {
                if (e.ClickCount == 2)
                {
                    ResetRange();
                    e.Handled = true;
                    return;
                }

                var positionX = e.GetPosition(this).X;
                var (startLine, endLine) = GetRangeLines(GetEffectiveLower(), GetEffectiveUpper());
                var lowerX = CenterOf(startLine, ItemCount, ActualWidth);
                var upperX = CenterOf(endLine, ItemCount, ActualWidth);

                if (IsControlPressed() && positionX > lowerX && positionX < upperX)
                {
                    _dragStartX = positionX;
                    _dragStartLine = startLine;
                    _dragEndLine = endLine;
                    _dragLowerValue = LowerValue;
                    _dragUpperValue = UpperValue;
                    _dragHandle = DragHandle.Move;
                    CaptureMouse();
                    e.Handled = true;
                    return;
                }

                if (Math.Abs(positionX - lowerX) <= HandleHitTolerance || Math.Abs(positionX - upperX) <= HandleHitTolerance)
                {
                    _dragHandle = Math.Abs(positionX - lowerX) <= Math.Abs(positionX - upperX)
                        ? DragHandle.Lower
                        : DragHandle.Upper;
                    _dragLowerValue = LowerValue;
                    _dragUpperValue = UpperValue;
                    CaptureMouse();
                    e.Handled = true;
                    return;
                }
            }

            NavigateAt(e.GetPosition(this).X);
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            var width = ActualWidth;
            if (width <= 0)
                return;

            var positionX = e.GetPosition(this).X;

            if (Math.Abs(_hoverX - positionX) >= 0.5)
            {
                _hoverX = positionX;
                InvalidateVisual();
            }

            if (_dragHandle == DragHandle.None)
            {
                if (IsRangeActive())
                {
                    var (startLine, endLine) = GetRangeLines(GetEffectiveLower(), GetEffectiveUpper());
                    var lowerX = CenterOf(startLine, ItemCount, width);
                    var upperX = CenterOf(endLine, ItemCount, width);
                    if (IsControlPressed() && positionX > lowerX && positionX < upperX)
                        Cursor = Cursors.SizeAll;
                    else if (Math.Abs(positionX - lowerX) <= HandleHitTolerance || Math.Abs(positionX - upperX) <= HandleHitTolerance)
                        Cursor = Cursors.SizeWE;
                    else
                        Cursor = Cursors.Arrow;
                }

                return;
            }

            var count = ItemCount;
            if (count <= 0)
                return;

            if (_dragHandle == DragHandle.Move)
            {
                var deltaLines = (positionX - _dragStartX) / ActualWidth * count;
                var selectionLines = _dragEndLine - _dragStartLine;
                var newLowerLine = (int)Math.Clamp(
                    Math.Round(_dragStartLine + deltaLines), 0, Math.Max(0, count - selectionLines));
                var newUpperLine = newLowerLine + selectionLines;
                _dragLowerValue = LineToValue(newLowerLine);
                _dragUpperValue = LineToValue(newUpperLine);
                InvalidateVisual();
                return;
            }

            var value = LineToValue(XToValueIndex(positionX));
            if (_dragHandle == DragHandle.Lower)
                _dragLowerValue = Math.Clamp(value, Minimum, Math.Max(Minimum, _dragUpperValue - 1));
            else
                _dragUpperValue = Math.Clamp(value, Math.Min(Maximum, _dragLowerValue + 1), Maximum);

            InvalidateVisual();
        }

        private (int startLine, int endLine) GetRangeLines(double lower, double upper)
        {
            var count = ItemCount;
            if (count <= 0)
                return (0, 0);

            var timeIndex = TimeIndex;
            if (!UseLineNumbers &&
                timeIndex is { HasTime: true } &&
                timeIndex.FindLineRange((long)lower, (long)upper) is { } range)
            {
                var startLine = Math.Clamp(range.StartLine, 0, count);
                var endLine = Math.Clamp(range.EndLine, startLine, count);
                return (startLine, endLine);
            }

            var rangeTicks = Maximum - Minimum;
            if (rangeTicks <= 0)
                return (0, count);

            var fallbackStart = (int)Math.Clamp((lower - Minimum) / rangeTicks * count, 0, count);
            var fallbackEnd = (int)Math.Clamp((upper - Minimum) / rangeTicks * count, fallbackStart, count);
            return (fallbackStart, fallbackEnd);
        }

        private double LineToValue(int line)
        {
            var count = ItemCount;
            if (count <= 0)
                return Minimum;

            if (line >= count)
                return Maximum;

            var timeIndex = TimeIndex;
            if (!UseLineNumbers && timeIndex is { HasTime: true } && timeIndex.Count > 0)
                return timeIndex.GetTicksAt(Math.Clamp(line, 0, timeIndex.Count - 1));

            return Minimum + (double)line / count * (Maximum - Minimum);
        }

        private int XToValueIndex(double x)
        {
            var count = ItemCount;
            if (count <= 0 || ActualWidth <= 0)
                return 0;

            return (int)Math.Clamp(Math.Floor(x / ActualWidth * count), 0, count);
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);

            if (_dragHandle == DragHandle.None)
                return;

            var lower = _dragLowerValue;
            var upper = _dragUpperValue;
            _dragHandle = DragHandle.None;
            ReleaseMouseCapture();

            SetCurrentValue(LowerValueProperty, lower);
            SetCurrentValue(UpperValueProperty, upper);
            InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);

            if (_hoverX >= 0)
            {
                _hoverX = -1;
                InvalidateVisual();
            }

            if (_dragHandle == DragHandle.None)
                Cursor = Cursors.Arrow;
        }

        private void NavigateAt(double positionX)
        {
            var command = NavigateCommand;
            var count = ItemCount;
            var width = ActualWidth;
            if (command == null || count <= 0 || width <= 0)
                return;

            var markers = Markers;
            if (markers != null)
            {
                var nearest = -1;
                var nearestDistance = double.MaxValue;
                foreach (var item in markers)
                {
                    if (item is not int index || index < 0 || index >= count)
                        continue;

                    var distance = Math.Abs(CenterOf(index, count, width) - positionX);
                    if (distance < nearestDistance)
                    {
                        nearestDistance = distance;
                        nearest = index;
                    }
                }

                if (nearest >= 0 && nearestDistance <= HandleHitTolerance && command.CanExecute(nearest))
                {
                    command.Execute(nearest);
                    return;
                }
            }

            var line = (int)Math.Clamp(Math.Floor(positionX / width * count), 0, count - 1);
            if (command.CanExecute(line))
                command.Execute(line);
        }

        private void ResetRange()
        {
            SetCurrentValue(LowerValueProperty, Minimum);
            SetCurrentValue(UpperValueProperty, Maximum);
            InvalidateVisual();
        }

        private bool IsRangeActive() => IsRangeEnabled && Maximum > Minimum;

        private static bool IsControlPressed() =>
            (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;

        private double GetEffectiveLower() =>
            _dragHandle is DragHandle.Lower or DragHandle.Move ? _dragLowerValue : LowerValue;

        private double GetEffectiveUpper() =>
            _dragHandle is DragHandle.Upper or DragHandle.Move ? _dragUpperValue : UpperValue;

        private string GetDurationText()
        {
            if (_dragHandle != DragHandle.None)
            {
                var size = (long)Math.Max(0, GetEffectiveUpper() - GetEffectiveLower());
                return UseLineNumbers
                    ? FormatLineCount(size)
                    : DurationFormatter.Format(size);
            }

            return DurationText;
        }

        private static string FormatLineCount(long count) =>
            count == 1 ? "1 line" : $"{count} lines";

        private static double CenterOf(int index, int count, double width) =>
            index * width / count;

        private static void OnMarkersChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (LogMinimapControl)d;

            if (control._observableMarkers != null)
                control._observableMarkers.CollectionChanged -= control.OnMarkersCollectionChanged;

            control._observableMarkers = e.NewValue as INotifyCollectionChanged;

            if (control._observableMarkers != null)
                control._observableMarkers.CollectionChanged += control.OnMarkersCollectionChanged;

            control.InvalidateVisual();
        }

        private static void OnMatchBucketsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (LogMinimapControl)d;

            if (control._observableMatchBuckets != null)
                control._observableMatchBuckets.CollectionChanged -= control.OnMatchBucketsCollectionChanged;

            control._observableMatchBuckets = e.NewValue as INotifyCollectionChanged;

            if (control._observableMatchBuckets != null)
                control._observableMatchBuckets.CollectionChanged += control.OnMatchBucketsCollectionChanged;

            control.InvalidateVisual();
        }

        private void OnMarkersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
            InvalidateVisual();

        private void OnMatchBucketsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
            InvalidateVisual();
    }
}
