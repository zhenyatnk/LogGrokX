#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Wpf.Ui.Appearance;
using Wpf.Ui.Markup;

namespace LogGrokX.Tests;

[TestClass]
[DoNotParallelize]
public sealed class LogRowSeamTests
{
    [TestMethod]
    public void AdjacentLogRowsLeaveNoGapForTheBackgroundToShowThrough()
    {
        RunOnSta(() =>
        {
            _ = new Border();

            var appResources = new ResourceDictionary();
            appResources.MergedDictionaries.Add(new ThemesDictionary { Theme = ApplicationTheme.Light });
            appResources.MergedDictionaries.Add(new ControlsDictionary());
            var styleDictionary = new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/LogGrokX;component/Styles/ListViewItemStyle.xaml")
            };
            appResources.MergedDictionaries.Add(styleDictionary);

            var baseStyle = (Style)styleDictionary["LogGridItemContainerStyle"];
            var derived = new Style(typeof(ListViewItem), baseStyle);
            derived.Setters.Add(new Setter(Control.TemplateProperty, styleDictionary["LogLineControlTemplate"]));
            derived.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Red));
            derived.Setters.Add(new Setter(FrameworkElement.HeightProperty, 15.5));

            var view = new GridView();
            view.Columns.Add(new GridViewColumn { Width = 380 });
            var list = new LogGrokX.Controls.ListControls.ListView
            {
                Background = Brushes.Transparent,
                ItemContainerStyle = derived,
                View = view,
                ItemsPanel = new ItemsPanelTemplate
                {
                    VisualTree = new FrameworkElementFactory(
                        typeof(LogGrokX.Controls.ListControls.VirtualizingStackPanel.VirtualizingStackPanel))
                }
            };
            ScrollViewer.SetCanContentScroll(list, true);
            var items = new List<object>();
            for (var i = 0; i < 40; i++) items.Add($"row {i}");
            list.ItemsSource = items;

            var window = new Window
            {
                Content = list,
                Resources = appResources,
                Width = 400,
                Height = 300,
                Left = -10000,
                Top = -10000,
                Background = Brushes.Magenta,
                ShowActivated = false,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual
            };
            window.Show();
            FlushLayout(window);

            var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window);
            var width = bitmap.PixelWidth;
            var height = bitmap.PixelHeight;
            var pixels = new int[width * height];
            bitmap.CopyPixels(pixels, width * 4, 0);

            var firstRed = -1;
            var lastRed = -1;
            var column = width / 2;
            for (var y = 0; y < height; y++)
            {
                var c = pixels[y * width + column];
                if ((byte)((c >> 16) & 0xFF) == 255 && (byte)((c >> 8) & 0xFF) == 0 && (byte)(c & 0xFF) == 0)
                {
                    if (firstRed < 0) firstRed = y;
                    lastRed = y;
                }
            }

            var interiorGaps = 0;
            var gapPositions = new List<int>();
            for (var y = firstRed + 1; y < lastRed; y++)
            {
                var c = pixels[y * width + column];
                if ((byte)((c >> 16) & 0xFF) == 255 && (byte)((c >> 8) & 0xFF) == 0 && (byte)(c & 0xFF) == 255)
                {
                    interiorGaps++;
                    gapPositions.Add(y);
                }
            }

            window.Close();

            Assert.IsGreaterThanOrEqualTo(0, firstRed);
            Assert.AreEqual(0, interiorGaps,
                $"interior background gaps between rows at y=[{string.Join(",", gapPositions)}]");
        });
    }

    [TestMethod]
    public void SelectingARowDoesNotAddABorder()
    {
        RunOnSta(() =>
        {
            _ = new Border();

            var resources = new ResourceDictionary();
            resources.MergedDictionaries.Add(new ThemesDictionary { Theme = ApplicationTheme.Light });
            resources.MergedDictionaries.Add(new ControlsDictionary());
            var styleDictionary = new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/LogGrokX;component/Styles/ListViewItemStyle.xaml")
            };
            resources.MergedDictionaries.Add(styleDictionary);

            var item = new ListViewItem
            {
                Style = (Style)styleDictionary["ItemContainerStyle"],
                Content = "row"
            };
            var window = new Window
            {
                Content = item,
                Resources = resources,
                Width = 200,
                Height = 60,
                Left = -10000,
                Top = -10000,
                ShowActivated = false,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual
            };
            try
            {
                window.Show();
                FlushLayout(window);
                var unselectedBackground = item.Background;
                var unselectedBorderBrush = item.BorderBrush;
                var unselectedBorderThickness = item.BorderThickness;

                item.IsSelected = true;
                FlushLayout(window);

                Assert.AreNotEqual(unselectedBackground, item.Background);
                Assert.AreEqual(unselectedBorderBrush, item.BorderBrush);
                Assert.AreEqual(unselectedBorderThickness, item.BorderThickness);
            }
            finally
            {
                window.Close();
            }
        });
    }

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
}