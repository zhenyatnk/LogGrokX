#nullable enable

using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using LogGrokX.Controls.TextRender;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Wpf.Ui.Appearance;
using Wpf.Ui.Markup;

namespace LogGrokX.Tests;

[TestClass]
[DoNotParallelize]
public sealed class LogGridViewSelectionBrushTests
{
    [TestMethod]
    public void TextViewSelectionBrushUsesThemeAccentAcrossThemeChanges()
    {
        RunOnSta(() =>
        {
            _ = new Border();

            var themes = new ThemesDictionary { Theme = ApplicationTheme.Light };
            var resources = new ResourceDictionary();
            resources.MergedDictionaries.Add(themes);
            resources.MergedDictionaries.Add(new ControlsDictionary());
            resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/LogGrokX;component/Styles/LogGridViewCellStyle.xaml")
            });

            var textView = new TextView { Resources = resources };
            var window = new Window
            {
                Content = textView,
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
                foreach (var theme in new[] { ApplicationTheme.Light, ApplicationTheme.Dark, ApplicationTheme.Light })
                {
                    themes.Theme = theme;
                    window.UpdateLayout();
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                    var selection = (SolidColorBrush)textView.SelectionBrush!;
                    Assert.AreEqual((Color)textView.FindResource("SystemAccentColorPrimary"), selection.Color);
                    Assert.AreEqual(0.5, selection.Opacity);
                }
            }
            finally
            {
                window.Close();
            }
        });
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