#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Tests;

[TestClass]
[DoNotParallelize]
public sealed class LogViewContextMenuTests
{
    [TestMethod]
    public void FilterMenuVisibilityTracksAvailabilityWithoutConversionExceptions()
    {
        RunOnSta(() =>
        {
            var item = new MenuItem { ItemsSource = new[] { "Time" } };
            var resources = new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/LogGrokX;component/Styles/LogViewContextMenu.xaml")
            };
            var style = (Style)resources["FilterContextMenuItem"];
            var exceptions = new List<Exception>();
            var threadId = Environment.CurrentManagedThreadId;
            void OnFirstChance(object? sender, FirstChanceExceptionEventArgs args)
            {
                if (Environment.CurrentManagedThreadId == threadId &&
                    args.Exception is ArgumentException or FormatException)
                    exceptions.Add(args.Exception);
            }

            AppDomain.CurrentDomain.FirstChanceException += OnFirstChance;
            try
            {
                item.Style = style;
                foreach (var canFilter in new[] { true, false, true })
                {
                    item.DataContext = new { CanFilter = canFilter };
                    FlushBindings();
                    Assert.AreEqual(0, exceptions.Count, string.Join(Environment.NewLine, exceptions));
                    Assert.AreEqual(canFilter ? Visibility.Visible : Visibility.Collapsed, item.Visibility);
                }

                item.ItemsSource = null;
                FlushBindings();
                Assert.AreEqual(Visibility.Collapsed, item.Visibility);
                Assert.AreEqual(0, exceptions.Count, string.Join(Environment.NewLine, exceptions));
            }
            finally
            {
                AppDomain.CurrentDomain.FirstChanceException -= OnFirstChance;
            }
        });
    }

    private static void FlushBindings() =>
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

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
