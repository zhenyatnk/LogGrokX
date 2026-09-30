using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace LogGrokX.Controls.ListControls.VirtualizingStackPanel
{
    public partial class VirtualizingStackPanel
    {
        public static readonly DependencyProperty CurrentPositionProperty = DependencyProperty.Register(
            "CurrentPosition", typeof(int), typeof(VirtualizingStackPanel), 
            new FrameworkPropertyMetadata(-1, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnCurrentPositionChanged ));

        public static readonly DependencyProperty IsCurrentItemProperty = DependencyProperty.RegisterAttached(
            "IsCurrentItem", typeof(bool), typeof(VirtualizingStackPanel), new PropertyMetadata(default(bool)));

        public static readonly DependencyProperty ReplaceSelectionOnCurrentPositionProperty = DependencyProperty.Register(
            "ReplaceSelectionOnCurrentPosition", typeof(bool), typeof(VirtualizingStackPanel),
            new PropertyMetadata(false));

        public static void SetIsCurrentItem(ListBoxItem listViewItem, bool value)
        {
            listViewItem.SetValue(IsCurrentItemProperty, value);
        }

        public static bool GetIsCurrentItem(ListBoxItem listViewItem)
        {
            return (bool) listViewItem.GetValue(IsCurrentItemProperty);
        }

        private static void OnCurrentPositionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var panel = (VirtualizingStackPanel) d;
            var newValue = (int) e.NewValue;
            var itemCount = panel.ListView.Items.Count;
            if (newValue >= itemCount)
                newValue = itemCount - 1;
            if (newValue < 0)
                panel._selection.Clear();
            else if (panel.ReplaceSelectionOnCurrentPosition && !panel._keepSelectionOnCurrentPositionChange)
                panel._selection.Set(newValue);
            else
                panel._selection.Add(newValue);
            panel.UpdateSelection();
            foreach (var visibleItem in panel._visibleItems)
            {
                 SetIsCurrentItem(visibleItem.Element, visibleItem.Index == newValue);
            }
            panel.ListView.Items.MoveCurrentToPosition(newValue);
        }

        public int CurrentPosition
        {
            get => (int) GetValue(CurrentPositionProperty);
            set => SetValue(CurrentPositionProperty, value);
        }

        public bool ReplaceSelectionOnCurrentPosition
        {
            get => (bool) GetValue(ReplaceSelectionOnCurrentPositionProperty);
            set => SetValue(ReplaceSelectionOnCurrentPositionProperty, value);
        }
        
        private readonly Selection _selection = new();
        private bool _keepSelectionOnCurrentPositionChange;
        private ScrollContentPresenter? _scrollContentPresenter;

        public IEnumerable<int> SelectedIndices => _selection;

        public event Action? SelectionChanged;
        
        public bool ProcessKeyDown(Key key)
        {
            switch (key)
            {
                case Key.Down when Keyboard.Modifiers.HasFlag(ModifierKeys.Shift):
                    ExpandSelectionDown();
                    break;
                case Key.Up when Keyboard.Modifiers.HasFlag(ModifierKeys.Shift):
                    ExpandSelectionUp();
                    break;
                case Key.Down:
                    NavigateDown();
                    break;
                case Key.Up:
                    NavigateUp();
                    break;
                case Key.PageUp:
                    PageUp();
                    break;
                case Key.PageDown:
                    PageDown();
                    break;
                default:
                    return false;
            }

            UpdateSelection();
            return true;
        }

        public bool ProcessMouseDown(MouseButton changedButton)
        {
            var item = GetItemUnderMouse();
            var suitableVisibleItems = _visibleItems.Where(i => i.Element == item).ToList();

            if (item == null) return false;
            if (changedButton == MouseButton.Right && item.IsSelected) return false;
            if (!suitableVisibleItems.Any()) return false;
            
            var index = suitableVisibleItems.Single().Index;
            
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                if (item.IsSelected)
                {
                    _selection.Remove(index);
                    item.IsSelected = false;
                    return true;
                }

                _selection.Add(index);
            } 
            else  if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                _selection.AddRangeToValue(index);
            }
            else
            {
                _selection.Set(index);
            }

            SetCurrentPositionKeepingSelection(index);
            UpdateSelection();

            FocusManager.SetFocusedElement(item, item);
            return true;
        }
        
        private void SetCurrentPositionKeepingSelection(int index)
        {
            _keepSelectionOnCurrentPositionChange = true;
            try
            {
                CurrentPosition = index;
            }
            finally
            {
                _keepSelectionOnCurrentPositionChange = false;
            }
        }

        private Point? GetMousePosition()
        {
            if (ScrollContentPresenter != null)
            {
                return Mouse.GetPosition(ScrollContentPresenter);
            }

            return null;
        }

        private  ListViewItem? GetItemUnderMouse()
        {            
            var mousePosition = GetMousePosition();
            return mousePosition != null ? 
                ScrollContentPresenter?.GetItemUnderPoint<ListViewItem>(mousePosition.Value) : null;
        }

        private void UpdateSelection()
        {
            foreach (var visibleItem in _visibleItems)
            {
                var isItemSelected = _selection.Contains(visibleItem.Index);
                if (visibleItem.Element.IsSelected != isItemSelected)
                    visibleItem.Element.IsSelected = isItemSelected;
            }
        }

        public void NavigateTo(int index, bool center = false)
        {
            _selection.Clear();
            CurrentPosition = index;
            _selection.Add(index);
            if (center)
                CenterIndexInView(index);
            else
                BringIndexIntoView(CurrentPosition);
            UpdateSelection();
        }

        private void CenterIndexInView(int index)
        {
            SetVerticalOffset(index - _viewPort.Height / 2.0);
            UpdateLayout();

            for (var attempt = 0; attempt < 16; attempt++)
            {
                var target = _visibleItems.Search(v => v.Index == index);
                if (target == null)
                {
                    var firstVisible = _visibleItems.Count > 0 ? _visibleItems[0].Index : 0;
                    var lastVisible = _visibleItems.Count > 0 ? _visibleItems[^1].Index : 0;
                    var offsetBeforeBuild = VerticalOffset;

                    if (index < firstVisible)
                        ScrollUp(_viewPortHeightInPixels);
                    else if (index > lastVisible)
                        ScrollDown(_viewPortHeightInPixels);
                    else
                        return;

                    UpdateLayout();

                    if (Math.Abs(VerticalOffset - offsetBeforeBuild) < Epsilon)
                        return;
                    continue;
                }

                var targetCenter = (target.Value.UpperBound + target.Value.LowerBound) / 2.0;
                var viewportCenter = _viewPortHeightInPixels / 2.0;
                var delta = targetCenter - viewportCenter;

                if (!Greater(delta, 0.0) && !Less(delta, 0.0))
                    return;

                var offsetBeforeScroll = VerticalOffset;
                if (Greater(delta, 0.0))
                    ScrollDown(delta);
                else
                    ScrollUp(-delta);

                UpdateLayout();

                if (Math.Abs(VerticalOffset - offsetBeforeScroll) < Epsilon)
                    return;
            }
        }

        private void NavigateUp()
        {
            if (CurrentPosition <= 0) return;
            _selection.Clear();
            CurrentPosition--;
            BringIndexIntoView(CurrentPosition);
            UpdateSelection();
        }

        private void NavigateDown()
        {
            if (CurrentPosition >= Items.Count - 1) return;
            _selection.Clear();
            CurrentPosition++;
            BringIndexIntoViewWhileNavigatingDown(CurrentPosition);
            UpdateSelection();
        }

        private void ExpandSelectionUp()
        {
            if (_selection.Bounds is not {min: var min, max: var max})
            {
                return;
            }

            switch (CurrentPosition)
            {
                case <=0: return;
                case {} pos when pos == min: 
                    SetCurrentPositionKeepingSelection(CurrentPosition - 1);
                    _selection.Add(CurrentPosition);
                    break;
                case {} pos when pos == max:
                    _selection.Remove(CurrentPosition);
                    SetCurrentPositionKeepingSelection(_selection.Bounds.Value.max);
                    break;
            }
    
            BringIndexIntoView(CurrentPosition);
            UpdateSelection();
        }

        private void ExpandSelectionDown()
        {
            if (_selection.Bounds is not {min: var min, max: var max}) return;

            switch (CurrentPosition)
            {
                case {} pos when pos >= Items.Count - 1:
                    return;
                case {} pos when pos == max:
                    SetCurrentPositionKeepingSelection(CurrentPosition + 1);
                    _selection.Add(CurrentPosition);
                    break;
                case {} pos when pos ==min:
                    _selection.Remove(CurrentPosition);
                    SetCurrentPositionKeepingSelection(_selection.Bounds.Value.min);
                    break;
            }

            BringIndexIntoViewWhileNavigatingDown(CurrentPosition);
            UpdateSelection();
        }

        protected override void BringIndexIntoView(int index)
        {
            if (!_visibleItems.Any(element => element.Index == index
                                              && GreaterOrEquals(element.UpperBound, 0.0)
                                              && LessOrEquals(element.LowerBound, ActualHeight)))
            {
                SetVerticalOffset(index);
            }
        }

        private void BringIndexIntoViewWhileNavigatingDown(int index)
        {
            var screenBound = ActualHeight;
            VisibleItem? existed = _visibleItems.Find(v => v.Index == index);

            switch (existed)
            {
                case ({ }, _, _, { } lowerBound)
                    when lowerBound > screenBound:
                    ScrollDown(lowerBound - screenBound);
                    break;

                case (null, _, _, _) when _visibleItems.Max(v => v.Index) == index - 1:
                    var nextItem = GenerateOneItemDown();
                    if (nextItem != null)
                        ScrollDown(nextItem.Value.Height);
                    break;
                case (null, _, _, _):
                    SetVerticalOffset(index);
                    break;
            }
        }

        private VisibleItem? GenerateOneItemDown()
        {
            BuildVisibleItems(
                new Size(ActualWidth, _visibleItems[^1].LowerBound + 10.0),
                VerticalOffset);

            return _visibleItems[^1];
        }

        private ScrollContentPresenter? ScrollContentPresenter
        {
            get
            {
                var host = ItemsControl.GetItemsOwner(this); 
                _scrollContentPresenter ??= 
                    host
                        .GetVisualChildren<ScrollContentPresenter>()
                        .Where(c => c.Content is ItemsPresenter)
                        .FirstOrDefault(c 
                            => ReferenceEquals(((ItemsPresenter)(c.Content)).TemplatedParent, host));

                return _scrollContentPresenter;
            }
        }
    }
}