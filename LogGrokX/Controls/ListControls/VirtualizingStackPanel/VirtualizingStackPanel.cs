using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using LogGrokX.Diagnostics;

namespace LogGrokX.Controls.ListControls.VirtualizingStackPanel
{
    public partial class VirtualizingStackPanel : VirtualizingPanel, IScrollInfo
    {
        public static readonly DependencyProperty FirstVisibleIndexProperty = DependencyProperty.Register(
            "FirstVisibleIndex", typeof(int), typeof(VirtualizingStackPanel),
            new FrameworkPropertyMetadata(-1, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public static readonly DependencyProperty GroupByThreadProperty = DependencyProperty.Register(
            "GroupByThread", typeof(bool), typeof(VirtualizingStackPanel),
            new FrameworkPropertyMetadata(false, OnGroupingChanged));

        public static readonly DependencyProperty ThreadFieldIndexProperty = DependencyProperty.Register(
            "ThreadFieldIndex", typeof(int), typeof(VirtualizingStackPanel),
            new FrameworkPropertyMetadata(-1, OnGroupingChanged));

        private static readonly Logger Log = Logger.Get("VirtualizingStackPanel");

        private List<VisibleItem> _visibleItems = new();
        private readonly Stack<ListViewItem> _recycled = new();

        private Size _viewPort;
        private Size _extent;
        private Point _offset;
        private double _viewPortHeightInPixels;

        public VirtualizingStackPanel()
        {
            IGrowingCollection? currentItems = null;

            void UpdateGrowingCollectionSubscription()
            {
                if (ItemsControl.GetItemsOwner(this)?.ItemsSource is not IGrowingCollection newItems ||
                    currentItems == newItems) return;

                currentItems = newItems;
                currentItems.CollectionGrown += _ => InvalidateMeasure();
                currentItems.SourceChanged += () =>
                {
                    _selection.Clear();
                    InvalidateMeasure();
                };
            }

            Loaded += (_, _) =>
            {
                // ReSharper disable once UnusedVariable
                var necessaryChildrenTouch = Children;
                var itemContainerGenerator = (ItemContainerGenerator) ItemContainerGenerator;
                itemContainerGenerator.ItemsChanged += (_, _) =>
                {
                    UpdateGrowingCollectionSubscription();

                    if (Items.Count <= 0) return;

                    CurrentPosition = Math.Min(CurrentPosition, Items.Count - 1);
                    
                    foreach (var visibleItem in _visibleItems)
                    {
                        SetIsCurrentItem(visibleItem.Element, visibleItem.Index == CurrentPosition);
                    }
                };

                UpdateGrowingCollectionSubscription();
            };

            _selection.Changed += () =>
            {
                ListView.UpdateReadonlySelectedItems(_selection);
                SelectionChanged?.Invoke();
            };
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            var perfStart = Stopwatch.GetTimestamp();
            var allocStart = PerfProbe.AllocMark();
            UpdateViewPort(availableSize);
            UpdateExtent();
            
            BuildVisibleItems(availableSize, VerticalOffset);

            var maxWidth = 
                _visibleItems.Any() ? _visibleItems.Max(item => item.Element.DesiredSize.Width) : 0.0;
            VisibleItemsMaxWidth = maxWidth;

            var visibleItemsHeight = _visibleItems.Sum(v => v.Height);
            IsViewportIsCompletelyFilled = visibleItemsHeight >= availableSize.Height;

            var result = double.IsPositiveInfinity(availableSize.Height) ? 
                new Size(maxWidth, visibleItemsHeight) : 
                new Size(Math.Max(maxWidth, availableSize.Width), availableSize.Height);
            PerfProbe.RecordAlloc("panelMeasure", allocStart, PerfProbe.AllocMark());
            PerfProbe.RecordMeasure(perfStart);
            return result;
        }
        
        public double VisibleItemsMaxWidth { get; private set; }
        public bool IsViewportIsCompletelyFilled { get; private set; }

        public int FirstVisibleIndex
        {
            get => (int)GetValue(FirstVisibleIndexProperty);
            set => SetValue(FirstVisibleIndexProperty, value);
        }

        public bool GroupByThread
        {
            get => (bool)GetValue(GroupByThreadProperty);
            set => SetValue(GroupByThreadProperty, value);
        }

        public int ThreadFieldIndex
        {
            get => (int)GetValue(ThreadFieldIndexProperty);
            set => SetValue(ThreadFieldIndexProperty, value);
        }

        private static void OnGroupingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is VirtualizingStackPanel panel)
                panel.InvalidateMeasure();
        }
      
        protected override Size ArrangeOverride(Size finalSize)
        {
            var perfStart = Stopwatch.GetTimestamp();
            var allocStart = PerfProbe.AllocMark();
            var screenBound = finalSize.Height;

            var invisibleItemOffset = screenBound;
            foreach (var recycledItem in _recycled)
            {
                var desiredSizeHeight = recycledItem.DesiredSize.Height;
                var childRect = new Rect(0, invisibleItemOffset, recycledItem.DesiredSize.Width, desiredSizeHeight);
                recycledItem.Arrange(childRect);
                invisibleItemOffset += desiredSizeHeight;
            }

            var onScreenCount = _visibleItems
                .Where(v =>
                    GreaterOrEquals(v.LowerBound, 0.0) && LessOrEquals(v.UpperBound, screenBound))
                .Select(v =>
                    (Math.Min(v.LowerBound, screenBound) - Math.Max(v.UpperBound, 0.0)) / (v.LowerBound - v.UpperBound))
                .Sum();

            UpdateViewPort(onScreenCount);
            UpdateExtent();
            
            foreach (var (item, _, upperBound, lowerBound) in _visibleItems)
            {
                var childRect = new Rect(-_offset.X, upperBound, item.DesiredSize.Width, lowerBound - upperBound);
                item.Arrange(childRect);
            }

            PerfProbe.RecordAlloc("panelArrange", allocStart, PerfProbe.AllocMark());
            PerfProbe.RecordArrange(perfStart);
            return finalSize;
        }

        private void BuildVisibleItems(Size availableSize, double verticalOffset)
        {
            var firstVisibleItemIndex = (int) Math.Floor(verticalOffset);
            var startOffset = firstVisibleItemIndex - verticalOffset;

            var resolvedFirstVisibleIndex = Items.Count == 0
                ? -1
                : Math.Clamp(firstVisibleItemIndex, 0, Items.Count - 1);
            if (FirstVisibleIndex != resolvedFirstVisibleIndex)
                FirstVisibleIndex = resolvedFirstVisibleIndex;

            if (InternalChildren.Count == 0)
                _visibleItems.Clear();
            
            var (newVisibleItems, itemsToRecycle) =
                GenerateItemsDownWithRelativeOffset(
                    startOffset, firstVisibleItemIndex, availableSize.Height, _visibleItems);

            _visibleItems = newVisibleItems;

            UpdateGroupFlags();

            RecycleItems(itemsToRecycle);
            UpdateSelection();
        }

        private void UpdateGroupFlags()
        {
            foreach (var visibleItem in _visibleItems)
                UpdateGroupFlags(visibleItem.Element, visibleItem.Index);
        }

        private void UpdateGroupFlags(ListViewItem element, int index)
        {
            var isGroupFirst = false;
            var isGroupLast = false;
            var isGroupContinuation = false;

            if (GroupByThread && ThreadFieldIndex >= 0 && Items[index] is IThreadGroupedItem line)
            {
                isGroupFirst = index == 0 || !line.HasSameThread(Items[index - 1] as IThreadGroupedItem, ThreadFieldIndex);
                isGroupLast = index == Items.Count - 1;
                isGroupContinuation = !isGroupFirst;
            }

            BaseLogListViewItem.SetIsGroupFirst(element, isGroupFirst);
            BaseLogListViewItem.SetIsGroupLast(element, isGroupLast);
            BaseLogListViewItem.SetIsGroupContinuation(element, isGroupContinuation);
        }

        private void RecycleItems(IEnumerable<VisibleItem> itemsToRecycle)
        {
            foreach (var (uiElement, _, _, _)  in itemsToRecycle)
            {
                uiElement.Visibility = Visibility.Collapsed;
                _recycled.Push(uiElement);
            }
        }

        private (List<VisibleItem> newVisibleItems, List<VisibleItem> newItemsToRecycle)
            GenerateItemsDownWithRelativeOffset(
                double relativeOffset, int startIndex,
                double heightToBuild, List<VisibleItem> currentVisibleItems)
        {
            double? currentOffset = null;
            var currentIndex = startIndex;

            var oldItems = new HashSet<VisibleItem>(currentVisibleItems, new GenericEqualityComparer<VisibleItem>());
            var newItems = new List<VisibleItem>();

            while ((currentOffset == null || currentOffset < heightToBuild) && currentIndex < Items.Count)
            {
                var index = currentIndex;
                var currentItem =
                    currentVisibleItems.Search(current =>
                        current.Index == index && current is { Element: ContentControl contentControl }
                                               && (contentControl.Content?.Equals(Items[index]) ?? false));

                ListViewItem? itemToAdd;
                
                if (currentItem is {} foundItem)
                {
                    oldItems.Remove(foundItem);
                    itemToAdd = foundItem.Element;
                    itemToAdd.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                }
                else
                {
                    itemToAdd = GenerateElement(currentIndex);
                }

                if (itemToAdd == null)
                    break;

                var itemHeight = itemToAdd.DesiredSize.Height;
                currentOffset ??= itemHeight * relativeOffset;
                newItems.Add(new VisibleItem(itemToAdd, currentIndex, currentOffset.Value,
                    currentOffset.Value + itemHeight));
                currentOffset += itemHeight;
                currentIndex++;
            }

            return (newItems, oldItems.ToList());
        }

        private IList Items => ItemsControl.GetItemsOwner(this)?.Items ?? (IList)new ArrayList();

        private void InsertAndMeasureItem(ListViewItem item, int itemIndex, bool isNewElement)
        {
            if (!InternalChildren.Cast<UIElement>().Contains(item))
                AddInternalChild(item);

            void UpdateItem(ListBoxItem itm)
            {
                var context = Items[itemIndex];
                if (itm.DataContext == context && itm.Content == context) return;
                itm.DataContext = this.DataContext;
                itm.Content = context;
                if (itm.IsSelected) itm.IsSelected = false;

                itm.InvalidateMeasure();
                foreach (var descendant in itm.GetVisualChildren<FrameworkElement>())
                    descendant.InvalidateMeasure();
                itm.Measure(new Size(Double.PositiveInfinity, Double.PositiveInfinity));
            }

            if (isNewElement)
            {
                ListView.PrepareItemContainer(item, Items[itemIndex]!);
                UpdateItem(item);
            }
            else
            {
                UpdateItem(item);
            }
            
        }

        private Size GetExtent()
        {
            if (!(ItemsControl.GetItemsOwner(this) is System.Windows.Controls.ListView listView))
                return new Size(0, 0);

            var extentWidth =
                listView.View switch
                {
                    System.Windows.Controls.GridView gridView => gridView.Columns.Sum(column => column.ActualWidth),
                    _ => 0.0
                };

            return new Size(Math.Max(extentWidth, _viewPort.Width), listView.Items.Count);
        }
        
        private void UpdateExtent()
        {
            var extent = GetExtent();
            if (extent == _extent) return;
            _extent = extent;
            SetVerticalOffset(_offset.Y);
            ScrollOwner?.InvalidateScrollInfo();
        }

        private void UpdateViewPort(Size availableSize)
        {
            var newViewPort = new Size(availableSize.Width, _viewPort.Height);
            _viewPortHeightInPixels = availableSize.Height;

            SetViewPort(newViewPort);
        }

        private void UpdateViewPort(double visibleItemsCount)
        {
            var newViewPort = new Size(_viewPort.Width, visibleItemsCount);
            SetViewPort(newViewPort);
        }

        private void SetViewPort(Size newViewPort)
        {
            if (newViewPort == _viewPort) return;

            _viewPort = newViewPort;
            ScrollOwner?.InvalidateScrollInfo();
            SetVerticalOffset(VerticalOffset);
        }

        public bool CanVerticallyScroll { get; set; }

        public bool CanHorizontallyScroll { get; set; }

        public double ExtentWidth => GetExtent().Width;

        public double ExtentHeight => GetExtent().Height;

        public double ViewportWidth => _viewPort.Width;

        public double ViewportHeight => _viewPort.Height;

        public double HorizontalOffset => _offset.X;

        public double VerticalOffset => _offset.Y;

        public ScrollViewer? ScrollOwner { get; set; }

        public void LineDown() => ScrollDown(20);

        public void LineLeft() => SetHorizontalOffset(HorizontalOffset - _viewPort.Width / 2);

        public void LineRight() => SetHorizontalOffset(HorizontalOffset + _viewPort.Width / 2);

        public void LineUp() => ScrollUp(20);

        public Rect MakeVisible(Visual visual, Rect rectangle) => new();

        public void MouseWheelDown() => ScrollDown(ScrollUnitPixels);

        public void MouseWheelLeft() => SetHorizontalOffset(HorizontalOffset - _viewPort.Width / 2.0);

        public void MouseWheelRight() => SetHorizontalOffset(HorizontalOffset + _viewPort.Width / 2.0);

        public void MouseWheelUp() => ScrollUp(ScrollUnitPixels);

        public void PageDown() => ScrollDown(_viewPortHeightInPixels);

        public void PageLeft() => SetHorizontalOffset(_offset.X - _viewPort.Width);

        public void PageRight() => SetHorizontalOffset(_offset.X + _viewPort.Width);

        public void PageUp() => ScrollUp(_viewPortHeightInPixels);

        public void SetHorizontalOffset(double offset)
        {
            var fixedOffset =
                offset switch
                {
                    _ when offset < 0 || _viewPort.Width > _extent.Width => 0.0,
                    _ when offset + _viewPort.Width >= _extent.Width => _extent.Width - _viewPort.Width,
                    _ => offset
                };
            _offset.X = fixedOffset;
            ScrollOwner?.InvalidateScrollInfo();
            InvalidateArrange();
        }

        public void SetVerticalOffset(double offset)
        {
            UpdateExtent();

            var fixedVerticalOffset = offset switch
            {
                _ when offset < 0 || _viewPort.Height >= _extent.Height => 0.0,
                _ when (offset + _viewPort.Height >= _extent.Height) => _extent.Height - _viewPort.Height,
                _ => offset
            };

            var newOffset = new Point(_offset.X, fixedVerticalOffset);

            if (newOffset == _offset) return;

            _offset = newOffset;
            ScrollOwner?.InvalidateScrollInfo();
            InvalidateMeasure();
        }

        private void ScrollUp(double distance)
        {
            var perfStart = Stopwatch.GetTimestamp();
            var firstVisibleItem =
                _visibleItems.Search(v => LessOrEquals(v.UpperBound, 0)
                                          && v.LowerBound > 0);

            if (firstVisibleItem == null) return;

            var firstVisibleItemValue = firstVisibleItem.Value;
            var currentOffset = firstVisibleItemValue.UpperBound;
            var currentIndex = firstVisibleItemValue.Index;
            var builtDistance = -distance;

            while (currentOffset > -distance)
            {
                currentIndex--;
                var newItem = GenerateElement(currentIndex);
                if (newItem != null)
                {
                    var itemHeight = newItem.DesiredSize.Height;
                    _visibleItems.Add(new VisibleItem(newItem, currentIndex, currentOffset - itemHeight,
                        currentOffset));
                    currentOffset -= itemHeight;
                    continue;
                }

                builtDistance = currentOffset;
                break;
            }

            _visibleItems.Sort((a, b) => a.Index - b.Index);

            var itemToScroll = _visibleItems.Search(v => LessOrEquals(v.UpperBound, builtDistance)
                                                         && v.LowerBound > builtDistance);

            if (itemToScroll is not { } itemToScrollValue)
            {
                Log.Warn("ScrollUp: no item to scroll, builtDistance={0}, items={1}, visible={2}",
                    builtDistance, Items.Count, _visibleItems.Count);
                SetVerticalOffset(0);
                PerfProbe.RecordScroll(perfStart);
                return;
            }

            var delta = (itemToScrollValue.UpperBound - builtDistance) / itemToScrollValue.Height;
            SetVerticalOffset(itemToScrollValue.Index - delta);
            PerfProbe.RecordScroll(perfStart);
        }

        private ListViewItem? GenerateElement(int currentIndex)
        {
            if (currentIndex >= Items.Count || currentIndex < 0)
                return null;

            var perfStart = Stopwatch.GetTimestamp();
            var allocStart = GC.GetAllocatedBytesForCurrentThread();
            ListViewItem? newItem;
            if (_recycled.Count > 0)
            {
                newItem = _recycled.Pop();
                newItem.Visibility = Visibility.Visible;
                InsertAndMeasureItem(newItem, currentIndex, false);
            }
            else
            {
                newItem = ListView.GetContainerForItem();
                InsertAndMeasureItem(newItem, currentIndex, true);
            }

            PerfProbe.RecordElementAlloc(GC.GetAllocatedBytesForCurrentThread() - allocStart);
            PerfProbe.RecordElement(perfStart);
            return newItem;
        }

        private void ScrollDown(double distance)
        {
            var perfStart = Stopwatch.GetTimestamp();
            var lastItem =
                _visibleItems.Search(v => v.UpperBound < _viewPortHeightInPixels
                                          && GreaterOrEquals(v.LowerBound, _viewPortHeightInPixels));

            if (lastItem == null) return;

            var lastItemValue = lastItem.Value;
            var currentOffset = lastItemValue.LowerBound;
            var currentIndex = lastItemValue.Index;

            var builtDistance = distance;
            
            while (currentOffset - _viewPortHeightInPixels < distance)
            {
                currentIndex++;
                var newItem = GenerateElement(currentIndex);

                if (newItem != null)
                {
                    var itemHeight = newItem.DesiredSize.Height;
                    _visibleItems.Add(new VisibleItem(newItem, currentIndex, currentOffset, currentOffset + itemHeight));
                    currentOffset += itemHeight;
                    continue;
                }

                builtDistance = currentOffset - _viewPortHeightInPixels;
                break;
            }

            var itemToScroll = _visibleItems.Search(v => LessOrEquals(v.UpperBound, builtDistance)
                                                         && GreaterOrEquals(v.LowerBound, builtDistance));

            if (itemToScroll is not { } itemToScrollValue)
            {
                Log.Warn("ScrollDown: no item to scroll, builtDistance={0}, items={1}, visible={2}",
                    builtDistance, Items.Count, _visibleItems.Count);
                SetVerticalOffset(_extent.Height);
                PerfProbe.RecordScroll(perfStart);
                return;
            }

            var delta = (builtDistance - itemToScrollValue.UpperBound) / itemToScrollValue.Height;
            SetVerticalOffset(itemToScrollValue.Index + delta);
            PerfProbe.RecordScroll(perfStart);
        }

        private const double Epsilon = 0.00001;

        private const double ScrollUnitPixels = 60;

        private static bool Less(double d1, double d2) => d1 + Epsilon < d2;

        private static bool Greater(double d1, double d2) => d1 > d2 + Epsilon;

        private static bool GreaterOrEquals(double d1, double d2) => !Less(d1, d2);

        private static bool LessOrEquals(double d1, double d2) => !Greater(d1, d2);

        private ListView ListView
        {
            get
            {
                _listView ??= ItemsControl.GetItemsOwner(this) as ListView;
                if (_listView == null) throw new InvalidOperationException();
                return _listView;
            }
        }
        private ListView? _listView;
    }
}