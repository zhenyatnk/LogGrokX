#nullable enable

using System;
using System.ComponentModel;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using LogGrokX.Controls.ListControls;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Wpf.Ui.Appearance;
using Wpf.Ui.Markup;

namespace LogGrokX.Tests;

[TestClass]
[DoNotParallelize]
public sealed class MarkedRowHighlightTests
{
    [TestMethod]
    [DataRow(true, true, Visibility.Visible)]
    [DataRow(true, false, Visibility.Collapsed)]
    [DataRow(false, true, Visibility.Collapsed)]
    public void MarkedRowOverlayFollowsMarkAndOption(bool isMarked, bool highlightEnabled, Visibility expected)
    {
        RunOnSta(() =>
        {
            var item = new ListViewItem
            {
                Content = new FakeLine { IsMarked = isMarked },
                Width = 120
            };
            var resources = new ResourceDictionary();
            resources.MergedDictionaries.Add(new ThemesDictionary { Theme = ApplicationTheme.Light });
            resources.MergedDictionaries.Add(new ControlsDictionary());
            resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/LogGrokX;component/Styles/ListViewItemStyle.xaml")
            });
            var template = (ControlTemplate)resources["LogLineControlTemplate"];
            item.Template = template;
            BaseLogListViewItem.SetHighlightMarkedRows(item, highlightEnabled);
            var window = CreateWindow(item);
            window.Resources = resources;
            try
            {
                window.Show();
                FlushLayout(window);
                var overlay = (Border)template.FindName("MarkedOverlay", item);
                Assert.IsNotNull(overlay);
                Assert.AreEqual(expected, overlay.Visibility);
                if (expected == Visibility.Visible)
                    Assert.AreSame(window.FindResource("SystemFillColorAttentionBrush"), overlay.Background);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static Window CreateWindow(object content) => new()
    {
        Content = content,
        Width = 200,
        Height = 100,
        Left = -10000,
        Top = -10000,
        Background = Brushes.Transparent,
        ShowActivated = false,
        ShowInTaskbar = false,
        WindowStartupLocation = WindowStartupLocation.Manual
    };

    private static void FlushLayout(FrameworkElement element)
    {
        element.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        element.UpdateLayout();
    }

    private static void RunOnSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { error = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null)
            throw error;
    }

    private sealed class FakeLine : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private bool _isMarked;

        public bool IsMarked
        {
            get => _isMarked;
            set
            {
                if (_isMarked == value) return;
                _isMarked = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsMarked)));
            }
        }
    }
}
