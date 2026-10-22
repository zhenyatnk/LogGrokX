using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using LogGrokX.Colors;
using Wpf.Ui.Appearance;

namespace LogGrokX.Controls.ListControls
{
    public class ItemActivatedEventArgs : RoutedEventArgs
    {
        public ItemActivatedEventArgs(RoutedEvent routedEvent, object source, object itemContent) : base(routedEvent, source)
        {
            ItemContent = itemContent;

        }
        public object ItemContent { get; init; }
    }
    
    public class BaseLogListViewItem : ListViewItem
    {
        private static readonly List<WeakReference<BaseLogListViewItem>> RegisteredItems = new();
        private static bool _themeSubscribed;

        private readonly ItemsControl _itemsControl;

        private Brush? _overrideForeground;

        private Brush? _overrideBackground;

        public BaseLogListViewItem(ItemsControl itemsControl)
        {
            _itemsControl = itemsControl;
            if (Environment.GetEnvironmentVariable("LOGGROKX_BITMAPCACHE") == "1")
                CacheMode = new BitmapCache();
            _itemsControl.Items.CurrentChanged += (_, _) =>
            {
                UpdateIsCurrentProperty();
            };

            RegisteredItems.Add(new WeakReference<BaseLogListViewItem>(this));
            EnsureThemeSubscription();
        }

        private static void EnsureThemeSubscription()
        {
            if (_themeSubscribed) return;
            _themeSubscribed = true;
            ApplicationThemeManager.Changed += OnApplicationThemeChanged;
        }

        private static void OnApplicationThemeChanged(ApplicationTheme theme, Color accent)
        {
            for (var i = RegisteredItems.Count - 1; i >= 0; i--)
            {
                if (RegisteredItems[i].TryGetTarget(out var item))
                    item.UpdateColorOverride();
                else
                    RegisteredItems.RemoveAt(i);
            }
        }

        public static readonly DependencyProperty OnItemActivatedCommandProperty = DependencyProperty.RegisterAttached(
            "OnItemActivatedCommand", typeof(ICommand), typeof(BaseLogListViewItem), new PropertyMetadata(default(ICommand), OnItemActivatedCommandChanged));

        public static readonly DependencyProperty IsSubscribedToItemActivatedEventProperty = DependencyProperty.RegisterAttached(
            "IsSubscribedToItemActivatedEvent", typeof(bool), typeof(BaseLogListViewItem), new PropertyMetadata(false));

        public static void SetIsSubscribedToItemActivatedEvent(DependencyObject element, bool value)
        {
            element.SetValue(IsSubscribedToItemActivatedEventProperty, value);
        }

        public static bool GetIsSubscribedToItemActivatedEvent(DependencyObject element)
        {
            return (bool)element.GetValue(IsSubscribedToItemActivatedEventProperty);
        }
        
        private static void OnItemActivatedCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (GetIsSubscribedToItemActivatedEvent(d)) return;
            if (d is not UIElement uiElement) return;
            uiElement.AddHandler(ItemActivatedEvent, new RoutedEventHandler((sender, args) =>
            {
                var command = GetOnItemActivatedCommand(d);
                if (command == null)
                    return;
                if (args is not ItemActivatedEventArgs itemActivatedEventArgs) throw new InvalidOperationException();
                var itemContent = itemActivatedEventArgs.ItemContent;
                if (command.CanExecute(itemContent))
                    command.Execute(itemContent);
            }));
            SetIsSubscribedToItemActivatedEvent(d, true);
        }

        public static void SetOnItemActivatedCommand(DependencyObject element, ICommand value)
        {
            element.SetValue(OnItemActivatedCommandProperty, value);
        }

        public static ICommand? GetOnItemActivatedCommand(DependencyObject element)
        {
            return element.GetValue(OnItemActivatedCommandProperty) as ICommand;
        }
        
        public static readonly RoutedEvent ItemActivatedEvent = 
            EventManager.RegisterRoutedEvent(nameof(ItemActivated), RoutingStrategy.Bubble, 
                typeof(RoutedEventHandler), typeof(BaseLogListViewItem));
        
        public event RoutedEventHandler ItemActivated
        {
            add => AddHandler(ItemActivatedEvent, value);
            remove => RemoveHandler(ItemActivatedEvent, value);
        }

        public static readonly DependencyProperty IsGroupFirstProperty = DependencyProperty.RegisterAttached(
            "IsGroupFirst", typeof(bool), typeof(BaseLogListViewItem), new PropertyMetadata(false));

        public static void SetIsGroupFirst(DependencyObject element, bool value) => element.SetValue(IsGroupFirstProperty, value);

        public static bool GetIsGroupFirst(DependencyObject element) => (bool)element.GetValue(IsGroupFirstProperty);

        public static readonly DependencyProperty IsGroupLastProperty = DependencyProperty.RegisterAttached(
            "IsGroupLast", typeof(bool), typeof(BaseLogListViewItem), new PropertyMetadata(false));

        public static void SetIsGroupLast(DependencyObject element, bool value) => element.SetValue(IsGroupLastProperty, value);

        public static bool GetIsGroupLast(DependencyObject element) => (bool)element.GetValue(IsGroupLastProperty);

        public static readonly DependencyProperty IsGroupContinuationProperty = DependencyProperty.RegisterAttached(
            "IsGroupContinuation", typeof(bool), typeof(BaseLogListViewItem), new PropertyMetadata(false));

        public static void SetIsGroupContinuation(DependencyObject element, bool value) => element.SetValue(IsGroupContinuationProperty, value);

        public static bool GetIsGroupContinuation(DependencyObject element) => (bool)element.GetValue(IsGroupContinuationProperty);

        public static readonly DependencyProperty IsOtherThreadProperty = DependencyProperty.RegisterAttached(
            "IsOtherThread", typeof(bool), typeof(BaseLogListViewItem), new PropertyMetadata(false));

        public static void SetIsOtherThread(DependencyObject element, bool value) => element.SetValue(IsOtherThreadProperty, value);

        public static bool GetIsOtherThread(DependencyObject element) => (bool)element.GetValue(IsOtherThreadProperty);

        public static readonly DependencyProperty HighlightMarkedRowsProperty = DependencyProperty.RegisterAttached(
            "HighlightMarkedRows", typeof(bool), typeof(BaseLogListViewItem), new PropertyMetadata(false));

        public static void SetHighlightMarkedRows(DependencyObject element, bool value) => element.SetValue(HighlightMarkedRowsProperty, value);

        public static bool GetHighlightMarkedRows(DependencyObject element) => (bool)element.GetValue(HighlightMarkedRowsProperty);

        public static readonly DependencyProperty IsCurrentItemProperty = DependencyProperty.Register(
            "IsCurrentItem", typeof(bool), typeof(BaseLogListViewItem), 
            new PropertyMetadata(false));

        public bool IsCurrentItem
        {
            get => (bool) GetValue(IsCurrentItemProperty);
            set
            {
                var oldValue = IsCurrentItem;
                SetValue(IsCurrentItemProperty, value);
                if (value && value != oldValue)
                {
                    RaiseEvent(new ItemActivatedEventArgs(ItemActivatedEvent, this, Content));
                }
            }
        }

        private new static readonly DependencyProperty ForegroundProperty = 
            TextElement.ForegroundProperty.AddOwner(typeof(BaseLogListViewItem), 
                new FrameworkPropertyMetadata(SystemColors.ControlTextBrush, 
                    FrameworkPropertyMetadataOptions.Inherits,
                    (_, _) => { },
                    CoerceForegroundProperty));
        
        private new static readonly DependencyProperty BackgroundProperty = 
            Panel.BackgroundProperty.AddOwner(typeof(BaseLogListViewItem), 
                new FrameworkPropertyMetadata(Panel.BackgroundProperty.DefaultMetadata.DefaultValue, 
                    FrameworkPropertyMetadataOptions.None,
                    (_, _) => { },
                    CoerceBackgroundProperty));

        private static object CoerceBackgroundProperty(DependencyObject d, object baseValue)
        {
            return (d as BaseLogListViewItem)?._overrideBackground ?? baseValue;
        }

        private static object CoerceForegroundProperty(DependencyObject d, object baseValue)
        {
            return (d as BaseLogListViewItem)?._overrideForeground ?? baseValue;
        }

        protected override void OnContentChanged(object oldContent, object? newContent)
        {
            base.OnContentChanged(oldContent, newContent);
            UpdateIsCurrentProperty();
            UpdateColorOverride();
        }

        private void UpdateColorOverride()
        {
            var colorSettings = ColorSettings.GetColorSettings(this);
            ColorSettings.ColorRule? rule = null;
            var rules = colorSettings?.Rules;
            if (rules is { Count: > 0 } && Content?.ToString() is { } text)
            {
                // ReSharper disable once ForCanBeConvertedToForeach
                // ReSharper disable once LoopCanBeConvertedToQuery
                for (var i = 0; i < rules.Count; i++)
                {
                    var colorSettingsRule = rules[i];
                    if (!colorSettingsRule.IsMatch(text)) continue;
                    rule = colorSettingsRule;
                    break;
                }
            }

            if (rule == null && _overrideForeground == null && _overrideBackground == null)
                return;

            var isDark = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;
            _overrideForeground = rule?.GetForegroundBrush(isDark);
            _overrideBackground = rule?.GetBackgroundBrush(isDark);
            CoerceValue(ForegroundProperty);
            CoerceValue(BackgroundProperty);
        }

        protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
        {
            if (IsCurrentItem)
                RaiseEvent(new ItemActivatedEventArgs(ItemActivatedEvent, this, Content));
            base.OnPreviewMouseDown(e);
        }

        private void UpdateIsCurrentProperty()
        {
            IsCurrentItem = Content?.Equals(_itemsControl.Items.CurrentItem) == true;
        }
    }
}
