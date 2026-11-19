#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LogGrokX.Controls.GridView;
using LogGrokX.Controls.ListControls;
using LogGrokX.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using LogListView = LogGrokX.Controls.ListControls.ListView;

namespace LogGrokX.Tests;

[TestClass]
[DoNotParallelize]
public class ColumnVisibilityTests
{
    private const string Regex = @"^(?<Time>\S+) (?<Level>\S+) (?<Message>.*)$";

    [TestMethod]
    public void HiddenColumnIsRemovedAndRestoredAtOriginalPosition()
    {
        RunOnSta(() =>
        {
            var view = CreateView();
            var expected = view.Columns.Cast<LogGridViewColumn>().Select(column => column.FieldName).ToArray();
            var settings = new ColumnSettings();
            settings.SetColumnHidden("Level", true);

            var list = new LogListView
            {
                ColumnSettings = settings,
                ItemsSource = Array.Empty<object>(),
                View = view
            };
            WithWindow(list, window =>
            {
                Assert.IsFalse(HasColumn(view, "Level"));

                settings.SetColumnHidden("Level", false);
                Flush(window);

                Assert.IsTrue(HasColumn(view, "Level"));
                var actual = view.Columns.Cast<LogGridViewColumn>().Select(column => column.FieldName).ToArray();
                CollectionAssert.AreEqual(expected, actual);
            });
        });
    }

    [TestMethod]
    public void RestoringColumnKeepsOrderWhenOtherColumnsStayHidden()
    {
        RunOnSta(() =>
        {
            var view = CreateView();
            var settings = new ColumnSettings();
            var list = new LogListView
            {
                ColumnSettings = settings,
                ItemsSource = Array.Empty<object>(),
                View = view
            };
            WithWindow(list, window =>
            {
                ToggleColumn(list, view, "Time", false);
                Flush(window);
                ToggleColumn(list, view, "Level", false);
                Flush(window);
                Assert.IsFalse(HasColumn(view, "Time"));
                Assert.IsFalse(HasColumn(view, "Level"));

                ToggleColumn(list, view, "Level", true);
                Flush(window);

                Assert.IsTrue(HasColumn(view, "Level"));
                Assert.IsFalse(HasColumn(view, "Time"));
                Assert.IsTrue(FieldColumnIndex(view, "Level") < FieldColumnIndex(view, "Message"));
            });
        });
    }

    [TestMethod]
    public void ColumnMenuCheckboxTogglesVisibility()
    {
        RunOnSta(() =>
        {
            var view = CreateView();
            var settings = new ColumnSettings();
            var list = new LogListView
            {
                ColumnSettings = settings,
                ItemsSource = Array.Empty<object>(),
                View = view
            };
            WithWindow(list, window =>
            {
                var level = view.Columns.OfType<LogGridViewColumn>()
                    .Single(column => column.FieldName == "Level");
                var menu = list.BuildColumnContextMenu(view, level);
                Assert.IsNotNull(menu);
                var levelItem = FindMenuItem(menu, "Level");
                Assert.IsTrue(levelItem.IsChecked);

                levelItem.IsChecked = false;
                levelItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, levelItem));
                Flush(window);
                Assert.IsFalse(HasColumn(view, "Level"));

                levelItem.IsChecked = true;
                levelItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, levelItem));
                Flush(window);
                Assert.IsTrue(HasColumn(view, "Level"));
            });
        });
    }

    [TestMethod]
    public void HiddenStateIsAppliedToRecreatedView()
    {
        RunOnSta(() =>
        {
            var settings = new ColumnSettings();
            settings.SetColumnHidden("Time", true);

            var view = CreateView();
            var list = new LogListView
            {
                ColumnSettings = settings,
                ItemsSource = Array.Empty<object>(),
                View = view
            };
            WithWindow(list, window =>
            {
                Assert.IsFalse(HasColumn(view, "Time"));

                var recreated = CreateView();
                list.View = recreated;
                Flush(window);

                Assert.IsFalse(HasColumn(recreated, "Time"));
                Assert.IsTrue(HasColumn(recreated, "Level"));
            });
        });
    }

    [TestMethod]
    public void ColumnSettingsRaiseChangeOnlyWhenHiddenStateChanges()
    {
        var settings = new ColumnSettings();
        var changes = 0;
        settings.HiddenColumnsChanged += (_, _) => changes++;

        settings.SetColumnHidden("Level", true);
        settings.SetColumnHidden("level", true);
        settings.SetColumnHidden("Level", false);
        settings.SetColumnHidden("Level", false);
        settings.SetColumnHidden("Time", true);
        settings.ShowAllColumns();
        settings.ShowAllColumns();

        Assert.AreEqual(4, changes);
        Assert.AreEqual(0, settings.HiddenColumns.Count);
    }

    private static void ToggleColumn(LogListView list, System.Windows.Controls.GridView view, string fieldName, bool isChecked)
    {
        var anchor = view.Columns.OfType<LogGridViewColumn>().First();
        var menu = list.BuildColumnContextMenu(view, anchor);
        Assert.IsNotNull(menu);
        var item = FindMenuItem(menu, fieldName);
        item.IsChecked = isChecked;
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, item));
    }

    private static int FieldColumnIndex(System.Windows.Controls.GridView view, string fieldName)
    {
        var columns = view.Columns.Cast<LogGridViewColumn>().ToList();
        return columns.FindIndex(column =>
            string.Equals(column.FieldName, fieldName, StringComparison.OrdinalIgnoreCase));
    }

    private static System.Windows.Controls.GridView CreateView()
    {
        var meta = new LogMetaInformation(new LogFormat { Regex = Regex });
        return (System.Windows.Controls.GridView)GridViewFactory.CreateView(
            new LogGridModel(meta, new ColumnSettings()), null);
    }

    private static bool HasColumn(System.Windows.Controls.GridView view, string fieldName) =>
        view.Columns.OfType<LogGridViewColumn>().Any(column =>
            string.Equals(column.FieldName, fieldName, StringComparison.OrdinalIgnoreCase));

    private static MenuItem FindMenuItem(ContextMenu menu, string header)
    {
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            if (string.Equals(item.Header as string, header, StringComparison.Ordinal))
                return item;
            foreach (var child in item.Items.OfType<MenuItem>())
            {
                if (string.Equals(child.Header as string, header, StringComparison.Ordinal))
                    return child;
            }
        }

        throw new AssertFailedException($"Menu item '{header}' not found");
    }

    private static void WithWindow(LogListView list, Action<Window> action)
    {
        var window = new Window
        {
            Content = list, Width = 600, Height = 200, Left = -10000, Top = -10000,
            ShowActivated = false, ShowInTaskbar = false
        };
        try
        {
            window.Show();
            Flush(window);
            action(window);
        }
        finally
        {
            window.Close();
        }
    }

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        window.UpdateLayout();
    }

    private static void RunOnSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            try
            {
                action();
            }
            catch (Exception exception)
            {
                error = exception;
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }
}
