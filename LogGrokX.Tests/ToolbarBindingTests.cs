#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
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
