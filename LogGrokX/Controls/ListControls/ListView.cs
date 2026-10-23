using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LogGrokX.Controls.GridView;
using LogGrokX.Controls.TextRender;
using LogGrokX.MarkedLines;

namespace LogGrokX.Controls.ListControls
{
    public class ColumnSettings
    {
        public double[]? ColumnWidths { get; set; }
    }
    
    public class ListView : BaseListView
    {
        private const int MaxContentBlocks = 16;
        private const int ContentBlockLength = 256;
        private const double HorizontalPadding = 16;
        private const double MinColumnWidth = 40;

        private int _previousItemCount;
        private bool _headerAdjusted;
        private bool _contentFitDone;

        private VirtualizingStackPanel.VirtualizingStackPanel? _panel;

        private bool? _haveExternalColumnSettings;

        private System.Windows.Controls.GridView? _settingsGridView;

        private readonly DispatcherTimer _contentFitTimer;
        private DispatcherOperation? _resetColumnsWidthOperation;

        private readonly List<(INotifyPropertyChanged Column, PropertyChangedEventHandler Handler)> _columnWidthHandlers = new();
        
        public static readonly DependencyProperty ReadonlySelectedItemsProperty =
            DependencyProperty.Register(nameof(ReadonlySelectedItems), typeof(IEnumerable), typeof(ListView));

        public static readonly DependencyProperty ColumnSettingsProperty = DependencyProperty.Register(
            "ColumnSettings", typeof(ColumnSettings), typeof(ListView),
            new PropertyMetadata(default(ColumnSettings), OnColumnSettingsChanged));

        public static readonly DependencyProperty IsLoadingProperty = DependencyProperty.Register(
            nameof(IsLoading), typeof(bool), typeof(ListView), new PropertyMetadata(false, OnIsLoadingChanged));

        public ColumnSettings? ColumnSettings
        {
            get => (ColumnSettings?)GetValue(ColumnSettingsProperty);
            set => SetValue(ColumnSettingsProperty, value);
        }

        public bool IsLoading
        {
            get => (bool)GetValue(IsLoadingProperty);
            set => SetValue(IsLoadingProperty, value);
        }
        
        public ListViewItem GetContainerForItem() => new LogListViewItem(this);

        public ListView()
        {
            _contentFitTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _contentFitTimer.Tick += (_, _) =>
            {
                _contentFitTimer.Stop();
                TryFitColumnsToContent();
            };
            Loaded += (_, _) => ScheduleRemeasure(0);
            IsVisibleChanged += (_, args) =>
            {
                if (args.NewValue is true)
                    ScheduleRemeasure(0);
                else
                    _contentFitTimer.Stop();
            };
            Unloaded += (_, _) =>
            {
                _contentFitTimer.Stop();
                _panel = null;
            };

            CommandBindings.Add(new CommandBinding(RoutedCommands.ToggleMarks,
                (_, args) =>
                {
                    RoutedCommands.ToggleMarksHandler(GetSelectedIndices().Select(i => Items[i]).OfType<ILineMark>());
                    args.Handled = true;
                },
                (_, args) =>
                {
                    args.CanExecute = GetSelectedIndices().Any();
                    args.Handled = true;
                }));
            ScheduleResetColumnsWidth();
        }

        public void UpdateReadonlySelectedItems(IEnumerable<int> selectedIndices)
        {
            ReadonlySelectedItems =
                selectedIndices
                    .Where(index => index < Items.Count && index >= 0)
                    .Select(index => Items[index]).ToList();
        }

        public void PrepareItemContainer(ListViewItem container, object item)
        {
            var itemContainerStyle = ItemContainerStyle;
            if (container.ReadLocalValue(FrameworkElement.StyleProperty) is Style style
                && style == itemContainerStyle
                && ReferenceEquals(System.Windows.Controls.GridView.GetColumnCollection(container),
                    (View as System.Windows.Controls.GridView)?.Columns))
                return;
            container.Content = null;
            container.Style = itemContainerStyle;
            PrepareContainerForItemOverride(container, item);
            container.Content = item;
        }

        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            if (e.Property != ViewProperty)
                return;

            _headerAdjusted = false;
            _contentFitDone = false;
            _previousItemCount = 0;
            ScheduleRemeasure(0);
        }

        public IEnumerable<object>? ReadonlySelectedItems
        {
            get => (GetValue(ReadonlySelectedItemsProperty) as IEnumerable)?.Cast<object>();
            set => SetValue(ReadonlySelectedItemsProperty, value);
        }

        public void NavigateTo(int lineNumber, bool center = false)
        {
            GetPanel()?.NavigateTo(lineNumber, center);
        }

        public void BringIndexIntoView(in int lineNumber)
        {
            GetPanel()?.BringIndexIntoViewPublic(lineNumber);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (GetPanel()?.ProcessKeyDown(e.Key) == true)
                e.Handled = true;
            else 
                base.OnKeyDown(e);
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            if (GetPanel()?.ProcessMouseDown(e.ChangedButton) == true)
            {
                e.Handled = true;
            }
            
            base.OnMouseDown(e);
        }
        protected override void OnItemsChanged(NotifyCollectionChangedEventArgs e)
        {
            base.OnItemsChanged(e);

            var panel = GetPanel();
            if (_previousItemCount == 0 && Items.Count > 0 && panel != null)
            {
                ScheduleResetColumnsWidth();
                _previousItemCount = Items.Count;            
            }
        }

        protected override void OnItemsSourceChanged(IEnumerable? oldValue, IEnumerable? newValue)
        {
            if (oldValue is IGrowingCollection oldCollection)
                oldCollection.CollectionGrown -= ScheduleRemeasure;

            base.OnItemsSourceChanged(oldValue, newValue);

            _previousItemCount = 0;
            _headerAdjusted = false;
            _contentFitDone = false;

            if (newValue == null)
                return;

            if (newValue is IGrowingCollection growingCollection)
            {
                growingCollection.CollectionGrown += ScheduleRemeasure;
                ScheduleMandatoryRemeasure();
            }
            
            ScheduleRemeasure(0);
        }
        
        protected override IEnumerable<int> GetSelectedIndices() => GetPanel()?.SelectedIndices ?? Enumerable.Empty<int>();

        protected override void OnTextInput(TextCompositionEventArgs e)
        {
            // Disable Text search
        }

        // ad hoc 
        // TODO redo columns tuning
        private async void ScheduleMandatoryRemeasure()
        {
            for (var i = 0; i< 10 && !_headerAdjusted; i++)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200));
                if (Items.Count < 2) continue;
                
                ScheduleRemeasure(0);
                return;
            }            
        }

        private void ScheduleRemeasure(int _)
        {
            var itemsCount = Items.Count;
            var panel = GetPanel();
            
            if (!_headerAdjusted && itemsCount > _previousItemCount && panel != null)
            {
                ScheduleResetColumnsWidth();
                _previousItemCount = itemsCount;

            }
            _panel?.InvalidateMeasure();
            ScheduleContentFit();
        }

        private static void OnIsLoadingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ListView listView && e.NewValue is false)
                listView.ScheduleContentFit();
        }

        private static void OnColumnSettingsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var listView = (ListView)d;
            listView.DetachColumnWidthHandlers();
            listView._settingsGridView = null;
            listView._haveExternalColumnSettings = null;
            listView._contentFitDone = false;
            listView.ScheduleContentFit();
        }

        private void ScheduleContentFit()
        {
            if (_contentFitDone || IsLoading || !IsLoaded || !IsVisible)
                return;

            _contentFitTimer.Stop();
            _contentFitTimer.Start();
        }

        private void TryFitColumnsToContent()
        {
            if (_contentFitDone || IsLoading || !IsLoaded || !IsVisible)
                return;

            if (View is not System.Windows.Controls.GridView gridView || Items.Count <= 0)
                return;

            if (!EnsureColumnSettings(gridView))
            {
                _contentFitDone = true;
                StoreColumnWidths(gridView);
                return;
            }

            ResetAutoColumnWidths(gridView);
            foreach (var row in this.GetVisualChildren<GridViewRowPresenter>())
            {
                foreach (var child in row.GetVisualChildren<FrameworkElement>())
                    child.InvalidateMeasure();
                row.InvalidateMeasure();
                row.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            }
            UpdateLayout();

            if (!FitColumnsToContent(gridView))
                return;

            _contentFitDone = true;
            _headerAdjusted = true;
            StoreColumnWidths(gridView);
        }

        private bool FitColumnsToContent(System.Windows.Controls.GridView gridView)
        {
            var columns = gridView.Columns;
            var columnCount = columns.Count;

            var hasFitColumns = false;
            for (var c = 0; c < columnCount; c++)
            {
                if (columns[c] is LogGridViewColumn { FitToContent: true, ContentTextProvider: not null })
                {
                    hasFitColumns = true;
                    break;
                }
            }

            if (!hasFitColumns)
            {
                EnsureHeaderWidths(gridView);
                return true;
            }

            if (!TryGetGlyphTypeface(FontFamily, out var typeface))
                return false;

            var fontSize = FontSize;
            var widths = new double[columnCount];

            double Measure(string? text)
            {
                return string.IsNullOrEmpty(text)
                    ? 0
                    : GlyphLine.MeasureWidth(text.AsSpan(), typeface, fontSize);
            }

            void MeasureItem(int index)
            {
                var item = Items[index];
                if (item == null)
                    return;

                for (var c = 0; c < columnCount; c++)
                {
                    if (columns[c] is not LogGridViewColumn { FitToContent: true, ContentTextProvider: { } provider })
                        continue;

                    var width = Measure(provider(item));
                    if (width > widths[c])
                        widths[c] = width;
                }
            }

            var itemCount = Items.Count;
            var sampledCount = MaxContentBlocks * ContentBlockLength;
            if (itemCount <= sampledCount)
            {
                for (var i = 0; i < itemCount; i++)
                    MeasureItem(i);
            }
            else
            {
                var blockSpan = itemCount / MaxContentBlocks;
                for (var b = 0; b < MaxContentBlocks; b++)
                {
                    var start = b * blockSpan;
                    var end = Math.Min(start + ContentBlockLength, itemCount);
                    for (var i = start; i < end; i++)
                        MeasureItem(i);
                }
            }

            for (var c = 0; c < columnCount; c++)
            {
                if (columns[c] is not LogGridViewColumn { FitToContent: true, ContentTextProvider: not null })
                    continue;

                columns[c].Width = Math.Max(widths[c] + HorizontalPadding, MinColumnWidth);
            }

            EnsureHeaderWidths(gridView);
            return true;
        }

        private void EnsureHeaderWidths(System.Windows.Controls.GridView gridView)
        {
            var headers = this.GetVisualChildren<GridViewColumnHeader>().ToArray();
            foreach (var column in gridView.Columns)
            {
                var header = headers.FirstOrDefault(item => ReferenceEquals(item.Column, column)) ?? new GridViewColumnHeader
                {
                    Content = column.Header ?? string.Empty,
                    ContentTemplate = column.HeaderTemplate ?? gridView.ColumnHeaderTemplate,
                    Style = column.HeaderContainerStyle ?? gridView.ColumnHeaderContainerStyle
                        ?? TryFindResource("UiGridViewColumnHeaderStyle") as Style
                        ?? TryFindResource(typeof(GridViewColumnHeader)) as Style
                };
                var content = header.GetVisualChildren<ContentPresenter>()
                    .FirstOrDefault(item => ReferenceEquals(item.TemplatedParent, header));
                double desiredWidth;
                if (content != null)
                {
                    content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    desiredWidth = content.DesiredSize.Width + header.Padding.Left + header.Padding.Right
                                   + header.BorderThickness.Left + header.BorderThickness.Right;
                }
                else
                {
                    header.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    desiredWidth = header.DesiredSize.Width;
                }
                var width = double.IsNaN(column.Width) ? column.ActualWidth : column.Width;
                if (width < desiredWidth)
                    column.Width = Math.Ceiling(desiredWidth);
            }
        }

        private static bool TryGetGlyphTypeface(FontFamily? fontFamily, out GlyphTypeface typeface)
        {
            typeface = null!;
            if (fontFamily == null)
                return false;

            var candidate = new Typeface(fontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            if (!candidate.TryGetGlyphTypeface(out var glyphTypeface))
                return false;

            typeface = glyphTypeface;
            return true;
        }
       
        private void ScheduleResetColumnsWidth()
        {
            if (_resetColumnsWidthOperation?.Status == DispatcherOperationStatus.Pending)
                return;

            _resetColumnsWidthOperation = Dispatcher.BeginInvoke(() =>
            {
                _resetColumnsWidthOperation = null;
                if (_contentFitDone || !IsLoaded || !IsVisible ||
                    View is not System.Windows.Controls.GridView gridView || GetPanel() == null || Items.Count <= 0)
                    return;

                if (!EnsureColumnSettings(gridView))
                    return;

                ResetAutoColumnWidths(gridView);
                if (GetPanel()?.IsViewportIsCompletelyFilled ?? false)
                {
                    _headerAdjusted = true;
                }
            }, DispatcherPriority.ApplicationIdle);
        }

        private void ResetAutoColumnWidths(System.Windows.Controls.GridView view)
        {
            Trace.TraceInformation($"Reset column widths (items: {Items.Count})");
            foreach (var column in view.Columns)
            {
                column.Width = 1;
                column.ClearValue(GridViewColumn.WidthProperty);
            }
        }

        private bool EnsureColumnSettings(System.Windows.Controls.GridView gridView)
        {
            if (!ReferenceEquals(gridView, _settingsGridView))
            {
                DetachColumnWidthHandlers();
                _settingsGridView = gridView;
                _haveExternalColumnSettings = null;
                _contentFitDone = false;
            }

            if (_haveExternalColumnSettings == null)
            {
                var columnSettings = ColumnSettings;
                if (columnSettings?.ColumnWidths is { } storedWidths &&
                    storedWidths.Length != gridView.Columns.Count)
                {
                    columnSettings.ColumnWidths = null;
                }

                _haveExternalColumnSettings = gridView.Columns.All(column =>
                    double.IsFinite(column.Width) && column.Width > 0);

                if (columnSettings != null)
                {
                    foreach (var column in gridView.Columns)
                    {
                        if (column is not INotifyPropertyChanged notifyPropertyChanged)
                            continue;

                        var capturedGridView = gridView;
                        PropertyChangedEventHandler handler = (_, args) =>
                        {
                            if (args.PropertyName != "ActualWidth")
                                return;

                            if (!ReferenceEquals(capturedGridView, View))
                                return;

                            StoreColumnWidths(capturedGridView);
                        };
                        notifyPropertyChanged.PropertyChanged += handler;
                        _columnWidthHandlers.Add((notifyPropertyChanged, handler));
                    }
                }
            }

            return _haveExternalColumnSettings != true;
        }

        private void StoreColumnWidths(System.Windows.Controls.GridView gridView)
        {
            if (!_contentFitDone || !IsLoaded || !IsVisible || ColumnSettings is not { } settings)
                return;

            var widths = gridView.Columns.Select(column =>
                double.IsNaN(column.Width) ? column.ActualWidth : column.Width).ToArray();
            if (widths.All(width => double.IsFinite(width) && width > 0))
                settings.ColumnWidths = widths;
        }

        private void DetachColumnWidthHandlers()
        {
            foreach (var (column, handler) in _columnWidthHandlers)
                column.PropertyChanged -= handler;

            _columnWidthHandlers.Clear();
        }

        private VirtualizingStackPanel.VirtualizingStackPanel? GetPanel()
        {
            _panel ??= this.GetVisualChildren<VirtualizingStackPanel.VirtualizingStackPanel>().FirstOrDefault();
            return _panel;
        }
    }
}
