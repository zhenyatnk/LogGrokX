#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using LogGrokX.Controls;
using LogGrokX.Controls.GridView;
using LogGrokX.Data;
using LogGrokX.Data.Index;
using LogGrokX.Filter;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using LogListView = LogGrokX.Controls.ListControls.ListView;
using LogPanel = LogGrokX.Controls.ListControls.VirtualizingStackPanel.VirtualizingStackPanel;

namespace LogGrokX.Tests;

[TestClass]
[DoNotParallelize]
public class GridProfileTransitionTests
{
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public void ReplacingProfileKeepsFullHeadersIncludingFilterButtons(bool restoreSmallWidths, bool customHeaderStyle)
    {
        RunOnSta(() =>
        {
            var list = CreateList();
            Style? headerStyle = null;
            if (customHeaderStyle)
            {
                headerStyle = new Style(typeof(GridViewColumnHeader));
                headerStyle.Setters.Add(new Setter(Control.FontSizeProperty, 18.0));
                headerStyle.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
                headerStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 3, 12, 3)));
                list.Resources["UiGridViewColumnHeaderStyle"] = headerStyle;
                list.Resources[typeof(GridViewColumnHeader)] = headerStyle;
            }
            list.ItemsSource = new[] { "x" };
            list.View = CreateView("^(?<Message>.*)");
            WithWindow(list, window =>
            {
                var view = CreateView("^(?<Time>)(?<Level>)(?<Context>)(?<Process>)(?<Source>)(?<Message>)");
                if (restoreSmallWidths)
                    foreach (var column in view.Columns)
                        column.Width = 15;
                list.ColumnSettings = new Controls.ListControls.ColumnSettings
                {
                    ColumnWidths = restoreSmallWidths ? view.Columns.Select(column => column.Width).ToArray() : null
                };
                list.View = view;
                Flush(window);
                WaitForContentFit(window);

                foreach (var column in view.Columns)
                {
                    var header = new GridViewColumnHeader
                    {
                        Content = string.Empty, ContentTemplate = column.HeaderTemplate, Style = headerStyle
                    };
                    header.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    Assert.IsGreaterThan(20.0, header.DesiredSize.Width);
                    Assert.IsGreaterThanOrEqualTo(header.DesiredSize.Width - 0.01, column.ActualWidth);
                }
            });
        });
    }

    [TestMethod]
    public void FittingHeadersPreservesMessageContentWidth()
    {
        RunOnSta(() =>
        {
            const string regex = "^(?<Message>.*)";
            var meta = new LogMetaInformation(new LogFormat { Regex = regex });
            var list = CreateList();
            list.ItemsSource = new[] { new LineViewModel(0, new string('W', 120), new RegexBasedLineParser(meta),
                new Selection(), new TransformationPerformer(Array.Empty<string>())) };
            var view = CreateView(regex, bindCells: true);
            list.View = view;
            WithWindow(list, window =>
            {
                WaitForContentFit(window);
                Assert.IsGreaterThan(600.0, view.Columns[1].ActualWidth);
            });
        });
    }

    [TestMethod]
    [DataRow("^.*$")]
    [DataRow("^(?<Text>.*)")]
    public void StructuredAndPlainProfilesReplaceVisibleAndRecycledRowsWithoutBindingErrors(string plainRegex)
    {
        RunOnSta(() =>
        {
            const string regex = "^(?<Time>\\S+) (?<Level>\\S+) (?<Context>\\S+) (?<Process>\\S+) (?<Source>\\S+) (?<Message>.*)";
            var structured = new LogMetaInformation(new LogFormat { Regex = regex });
            var plain = new LogMetaInformation(new LogFormat { Regex = plainRegex });
            var structuredRows = CreateRows(structured);
            var plainRows = CreateRows(plain);
            var listener = new BindingErrors();
            PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
            var previousLevel = PresentationTraceSources.DataBindingSource.Switch.Level;
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
            try
            {
                var list = CreateList();
                list.ItemsSource = structuredRows;
                list.View = CreateView(regex, bindCells: true);
                WithWindow(list, window =>
                {
                    var panel = Descendants<LogPanel>(list).Single();
                    panel.SetVerticalOffset(30);
                    Flush(window);
                    list.ItemsSource = plainRows;
                    list.View = CreateView(plainRegex, bindCells: true);
                    Flush(window);

                    list.View = CreateView(regex, bindCells: true);
                    list.ItemsSource = structuredRows;
                    Flush(window);
                    foreach (var offset in new[] { 30, 0, 60 })
                    {
                        panel.SetVerticalOffset(offset);
                        Flush(window);
                        var containers = Descendants<ListViewItem>(panel)
                            .Where(row => row.Visibility == Visibility.Visible).ToArray();
                        Assert.IsNotEmpty(containers);
                        foreach (var container in containers)
                        {
                            var row = (LineViewModel)container.Content;
                            Assert.AreSame(structuredRows[row.Index], row);
                            Assert.AreEqual("IPC", row[4].OriginalText);
                            Assert.AreSame(((GridView)list.View).Columns, GridView.GetColumnCollection(container));
                        }
                    }
                });
                Assert.AreEqual(string.Empty, listener.Errors);
            }
            finally
            {
                PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
                PresentationTraceSources.DataBindingSource.Switch.Level = previousLevel;
            }
        });
    }

    private static LineViewModel[] CreateRows(LogMetaInformation meta)
    {
        var parser = new RegexBasedLineParser(meta);
        return Enumerable.Range(0, 90).Select(index => new LineViewModel(index,
            "12:34 INFO context process IPC message", parser, new Selection(),
            new TransformationPerformer(Array.Empty<string>()))).ToArray();
    }

    private static GridView CreateView(string regex, bool bindCells = false)
    {
        var format = new LogFormat { Regex = regex };
        var unindexed = new LogMetaInformation(format);
        format.IndexedFields = unindexed.FieldNames.Where(name => name != "Message" && name != "Time").ToArray();
        var meta = new LogMetaInformation(format);
        var indexer = new EmptyIndexer();
        var filters = new FilterSettings(indexer, meta);
        var view = (GridView)new GridViewFactory(meta, true,
            field => new FilterViewModel(field, filters, indexer, meta)).CreateView(null);
        view.Columns.RemoveAt(0);
        for (var index = 0; index < view.Columns.Count; index++)
        {
            var text = new FrameworkElementFactory(typeof(TextBlock));
            if (bindCells)
            {
                var path = index == 0 ? new PropertyPath("IndexViewModel.OriginalText")
                    : new PropertyPath(".[(0)].OriginalText", index - 1);
                text.SetBinding(TextBlock.TextProperty, new Binding { Path = path });
            }
            else
                text.SetValue(TextBlock.TextProperty, "x");
            view.Columns[index].CellTemplate = new DataTemplate { VisualTree = text };
        }
        return view;
    }

    private static LogListView CreateList()
    {
        var presenter = new FrameworkElementFactory(typeof(GridViewRowPresenter));
        presenter.SetBinding(GridViewRowPresenter.ColumnsProperty, new Binding
        {
            Path = new PropertyPath(GridView.ColumnCollectionProperty),
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        presenter.SetBinding(GridViewRowPresenter.ContentProperty,
            new Binding("Content") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        var style = new Style(typeof(ListViewItem));
        style.Setters.Add(new Setter(Control.TemplateProperty,
            new ControlTemplate(typeof(ListViewItem)) { VisualTree = presenter }));
        return new LogListView
        {
            FontFamily = new FontFamily("Consolas"), FontSize = 12,
            ItemContainerStyle = style,
            ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(LogPanel)))
        };
    }

    private static void WithWindow(LogListView list, Action<Window> action)
    {
        var window = new Window
        {
            Content = list, Width = 1000, Height = 240, Left = -10000, Top = -10000,
            ShowActivated = false, ShowInTaskbar = false
        };
        try { window.Show(); Flush(window); action(window); }
        finally { window.Close(); }
    }

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        window.UpdateLayout();
    }

    private static void WaitForContentFit(Window window)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
        {
            Interval = TimeSpan.FromMilliseconds(600)
        };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
        Flush(window);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
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

    private sealed class EmptyIndexer : IComponentIndexer
    {
        public event Action<int, string>? ComponentAdded { add { } remove { } }
        public IEnumerable<string> GetAllComponents(int componentNumber) => Array.Empty<string>();
        public int GetIndexCountForComponent(int componentIndex, string componentValue) => 0;
    }

    private sealed class BindingErrors : TraceListener
    {
        public string Errors { get; private set; } = string.Empty;
        public override void Write(string? message) => Errors += message;
        public override void WriteLine(string? message) => Errors += message;
    }

    private static void RunOnSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { error = exception; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }
}
