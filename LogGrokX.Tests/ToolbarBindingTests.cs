#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Tests;

[TestClass]
[DoNotParallelize]
public class ToolbarBindingTests
{
    [TestMethod]
    [DataRow("ToolbarButtonStyle", false)]
    [DataRow("ToolbarToggleButtonStyle", true)]
    public void AutomationName_TracksToolTipWithoutNullBindingErrors(string styleKey, bool isToggle)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var resources = new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/LogGrokX;component/Styles/FluentStyles.xaml")
                };
                using var output = new StringWriter();
                using var listener = new TextWriterTraceListener(output);
                var traceSource = PresentationTraceSources.DataBindingSource;
                var originalLevel = traceSource.Switch.Level;
                traceSource.Switch.Level = SourceLevels.Error;
                traceSource.Listeners.Add(listener);
                try
                {
                    Control button = isToggle ? new ToggleButton() : new Wpf.Ui.Controls.Button();
                    button.Style = (Style)resources[styleKey];
                    FlushBindings();
                    Assert.AreEqual(string.Empty, AutomationProperties.GetName(button));

                    button.ToolTip = "Open log file";
                    FlushBindings();
                    Assert.AreEqual("Open log file", AutomationProperties.GetName(button));

                    button.ToolTip = null;
                    FlushBindings();
                    Assert.AreEqual(string.Empty, AutomationProperties.GetName(button));
                    Assert.AreEqual(string.Empty, output.ToString());
                }
                finally
                {
                    traceSource.Listeners.Remove(listener);
                    traceSource.Switch.Level = originalLevel;
                }
            }
            catch (Exception exception)
            {
                error = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null)
            throw error;
    }

    [TestMethod]
    public void ToolbarForeground_WhenButtonResourceIsStale_UsesPrimaryTextColor()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var resources = new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/LogGrokX;component/Styles/FluentStyles.xaml")
                };
                resources["TextFillColorPrimaryBrush"] = Brushes.White;
                resources["ButtonForeground"] = Brushes.Black;
                resources["ButtonForegroundPressed"] = Brushes.Black;
                var icon = new Wpf.Ui.Controls.SymbolIcon
                {
                    Symbol = Wpf.Ui.Controls.SymbolRegular.Settings24
                };
                var button = new Wpf.Ui.Controls.Button
                {
                    Style = (Style)resources["ToolbarButtonStyle"],
                    Content = icon
                };
                var window = new Window
                {
                    Content = button,
                    Resources = resources,
                    Width = 100,
                    Height = 100,
                    ShowInTaskbar = false
                };
                try
                {
                    window.Show();
                    Assert.AreEqual(System.Windows.Media.Colors.White, ((SolidColorBrush)button.Foreground).Color);
                    Assert.AreEqual(System.Windows.Media.Colors.White, ((SolidColorBrush)icon.Foreground).Color);
                }
                finally
                {
                    window.Close();
                }
            }
            catch (Exception exception)
            {
                error = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null)
            throw error;
    }

    [STATestMethod]
    public void UpdatePillFillsItsBackgroundWithTheCurrentAccent()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LogGrokX", "MainWindow.xaml");
        var window = XDocument.Load(path).Root!;
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        const string styleKey = "UpdatePillButtonStyle";
        var style = window.Descendants(presentation + "Style")
            .Single(element => (string?)element.Attribute(x + "Key") == styleKey);
        var dictionary = new XElement(presentation + "ResourceDictionary",
            new XAttribute(XNamespace.Xmlns + "x", x.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "ui", window.GetNamespaceOfPrefix("ui")!.NamespaceName),
            style);
        var resources = (ResourceDictionary)XamlReader.Parse(dictionary.ToString());
        var button = new Wpf.Ui.Controls.Button
        {
            Style = (Style)resources[styleKey],
            IsEnabled = false,
            Content = "Downloading 43%"
        };
        button.Resources["AccentFillColorDefaultBrush"] = Brushes.Blue;
        button.ApplyTemplate();
        var progress = (ProgressBar)button.Template.FindName("DownloadProgress", button);
        Assert.AreEqual(Visibility.Collapsed, progress.Visibility);

        foreach (var percent in new[] { 0d, 43.4, 100d })
        {
            button.DataContext = new { IsUpdateDownloading = true, IsUpdateReady = false, UpdateProgress = percent };
            var size = new Size(200, 32);
            button.Measure(size);
            button.Arrange(new Rect(size));
            FlushBindings();
            button.UpdateLayout();

            Assert.AreEqual(Visibility.Visible, progress.Visibility);
            Assert.AreEqual(30, progress.ActualHeight, 0.01);
            var track = (FrameworkElement)progress.Template.FindName("PART_Track", progress);
            var indicator = (Border)progress.Template.FindName("PART_Indicator", progress);
            Assert.AreEqual(track.ActualWidth * percent / 100, indicator.ActualWidth, 1);
            Assert.AreEqual(track.ActualWidth, ((Border)indicator.Child).ActualWidth, 1);
        }

        var fill = (Border)((Border)progress.Template.FindName("PART_Indicator", progress)).Child;
        Assert.AreSame(Brushes.Blue, fill.Background);
        button.Resources["AccentFillColorDefaultBrush"] = Brushes.Green;
        FlushBindings();
        Assert.AreSame(Brushes.Green, fill.Background);

        button.DataContext = new { IsUpdateDownloading = false, IsUpdateReady = true, UpdateProgress = 100d };
        FlushBindings();
        Assert.AreEqual(Visibility.Collapsed, progress.Visibility);
    }

    private static void FlushBindings() =>
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

    [TestMethod]
    [DataRow("Primary")]
    [DataRow("Transparent")]
    public void PressedButtonInDataTemplate_UsesItsOwnForegroundWithoutBindingErrors(string appearance)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var presenter = new ContentPresenter
                {
                    Content = "Saved search",
                    ContentTemplate = (DataTemplate)XamlReader.Parse($"""
                        <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                      xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml">
                            <ui:Button Appearance="{appearance}" Content="Apply" />
                        </DataTemplate>
                        """),
                    Resources = new ResourceDictionary
                    {
                        Source = new Uri("pack://application:,,,/LogGrokX;component/Styles/FluentStyles.xaml")
                    }
                };
                presenter.Measure(new Size(200, 50));
                presenter.Arrange(new Rect(0, 0, 200, 50));
                presenter.UpdateLayout();
                var button = (Wpf.Ui.Controls.Button)VisualTreeHelper.GetChild(presenter, 0);
                Assert.AreSame(presenter, button.TemplatedParent);
                button.Style = (Style)presenter.Resources["FluentButtonStyle"];
                button.ApplyTemplate();
                button.PressedForeground = Brushes.Red;

                using var output = new StringWriter();
                using var listener = new TextWriterTraceListener(output);
                var traceSource = PresentationTraceSources.DataBindingSource;
                var originalLevel = traceSource.Switch.Level;
                traceSource.Switch.Level = SourceLevels.Error;
                traceSource.Listeners.Add(listener);
                try
                {
                    SetReadOnlyState(button, typeof(UIElement), "IsMouseOver", true);
                    SetReadOnlyState(button, typeof(ButtonBase), "IsPressed", true);
                    Assert.IsTrue(button.IsMouseOver);
                    Assert.IsTrue(button.IsPressed);
                    FlushBindings();
                    var foreground = button.Foreground;
                    FlushBindings();
                    Assert.AreEqual(string.Empty, output.ToString());
                    Assert.AreSame(button.PressedForeground, foreground);

                    SetReadOnlyState(button, typeof(ButtonBase), "IsPressed", false);
                    FlushBindings();
                    Assert.AreNotSame(button.PressedForeground, button.Foreground);
                    Assert.AreEqual(string.Empty, output.ToString());
                }
                finally
                {
                    traceSource.Listeners.Remove(listener);
                    traceSource.Switch.Level = originalLevel;
                }
            }
            catch (Exception exception)
            {
                error = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null)
            throw error;
    }

    private static void SetReadOnlyState(DependencyObject element, Type owner, string property, bool value)
    {
        if (property == "IsMouseOver")
        {
            var writeFlag = typeof(UIElement).GetMethod("WriteFlag", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var flag = Enum.Parse(writeFlag.GetParameters()[0].ParameterType, "IsMouseOverCache");
            writeFlag.Invoke(element, new[] { flag, (object)value });
        }
        var field = owner.GetField(property + "PropertyKey", BindingFlags.Static | BindingFlags.NonPublic)!;
        element.SetValue((DependencyPropertyKey)field.GetValue(null)!, value);
    }
}
