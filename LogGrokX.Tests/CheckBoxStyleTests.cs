#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using LogGrokX.Filter;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using Wpf.Ui.Markup;
using CheckBox = System.Windows.Controls.CheckBox;
using ListBox = System.Windows.Controls.ListBox;

namespace LogGrokX.Tests;

[TestClass]
[DoNotParallelize]
public sealed class CheckBoxStyleTests
{
    [TestMethod]
    [DataRow(ApplicationTheme.Light)]
    [DataRow(ApplicationTheme.Dark)]
    public void DeselectingAllThenScrollingDoesNotProduceAnimationWarnings(ApplicationTheme theme)
    {
        RunOnSta(() =>
        {
            var list = new ListBox();
            var resources = new ResourceDictionary();
            resources.MergedDictionaries.Add(new ThemesDictionary { Theme = theme });
            resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/LogGrokX;component/Styles/CheckBoxStyles.xaml")
            });
            var style = (Style)resources[typeof(CheckBox)];
            var factory = new FrameworkElementFactory(typeof(CheckBox));
            factory.SetValue(FrameworkElement.StyleProperty, style);
            factory.SetValue(FrameworkElement.MinWidthProperty, 0.0);
            factory.SetValue(FrameworkElement.MinHeightProperty, 20.0);
            factory.SetValue(Control.PaddingProperty, new Thickness(0));
            factory.SetBinding(ContentControl.ContentProperty, new Binding(nameof(ElementViewModel.Name)));
            factory.SetBinding(ToggleButton.IsCheckedProperty,
                new Binding(nameof(ElementViewModel.IsActive)) { Mode = BindingMode.TwoWay });
            var settings = new FilterSettings(null!, null!);
            var options = Enumerable.Range(0, 120)
                .Select(index => new ElementViewModel(index.ToString(), 0, settings, () => 0)).ToArray();
            list.Resources = resources;
            list.ItemTemplate = new DataTemplate { VisualTree = factory };
            list.ItemsSource = options;
            ScrollViewer.SetCanContentScroll(list, true);
            System.Windows.Controls.VirtualizingStackPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling);
            var window = new Window
            {
                Content = list, Width = 270, Height = 220, Left = -10000, Top = -10000,
                ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual
            };
            PresentationTraceSources.Refresh();
            using var output = new StringWriter();
            using var listener = new TextWriterTraceListener(output);
            var trace = PresentationTraceSources.AnimationSource;
            var previousLevel = trace.Switch.Level;
            trace.Switch.Level = SourceLevels.Warning;
            trace.Listeners.Add(listener);
            try
            {
                window.Show();
                FlushLayout(window);
                foreach (var option in options)
                    option.IsActive = false;
                FlushLayout(window);
                var viewer = Descendants<ScrollViewer>(list).First();
                viewer.ScrollToBottom();
                FlushLayout(window);
                Assert.IsGreaterThan(0.0, viewer.VerticalOffset);
                viewer.ScrollToTop();
                FlushLayout(window);
                viewer.ScrollToVerticalOffset(30);
                FlushLayout(window);

                var checkBox = Descendants<CheckBox>(list).First(box => box.IsVisible);
                var glyph = (SymbolIcon)checkBox.Template.FindName("ControlIcon", checkBox);
                Assert.AreEqual(false, checkBox.IsChecked);
                Assert.AreEqual(Visibility.Collapsed, glyph.Visibility);
                checkBox.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
                FlushLayout(window);
                Assert.IsTrue(((ElementViewModel)checkBox.DataContext).IsActive);
                Assert.AreEqual(Visibility.Visible, glyph.Visibility);
                checkBox.SetCurrentValue(ToggleButton.IsCheckedProperty, false);
                FlushLayout(window);
                Assert.IsFalse(((ElementViewModel)checkBox.DataContext).IsActive);
                Assert.AreEqual(Visibility.Collapsed, glyph.Visibility);
                Assert.AreEqual(string.Empty, output.ToString());
            }
            finally
            {
                window.Close();
                trace.Listeners.Remove(listener);
                trace.Switch.Level = previousLevel;
            }
        });
    }

    private static System.Collections.Generic.IEnumerable<T> Descendants<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
                yield return match;
            foreach (var nested in Descendants<T>(child))
                yield return nested;
        }
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
