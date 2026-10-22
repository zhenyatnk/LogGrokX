using System;
using System.Linq;
using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using System.Windows.Controls;
using LogGrokX.Controls;
using LogGrokX.Controls.GridView;
using LogGrokX.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Tests
{
    [TestClass]
    public class GridViewFactoryTests
    {
        [TestMethod]
        public void ReplacingViewSizesColumnsToFullHeadersImmediately()
        {
            RunOnSta(() =>
            {
                System.Windows.Controls.GridView CreateView(string regex)
                {
                    var meta = new LogMetaInformation(new LogFormat { Regex = regex });
                    var view = (System.Windows.Controls.GridView)new GridViewFactory(meta, false, null).CreateView(null);
                    view.Columns.RemoveAt(0);
                    foreach (var column in view.Columns)
                    {
                        column.HeaderTemplate.Seal();
                        var model = (HeaderViewModel)((FrameworkElement)column.HeaderTemplate.LoadContent()).DataContext;
                        var label = new FrameworkElementFactory(typeof(TextBlock));
                        label.SetValue(TextBlock.TextProperty, model.FieldHeader);
                        column.HeaderTemplate = new DataTemplate { VisualTree = label };
                        var text = new FrameworkElementFactory(typeof(TextBlock));
                        text.SetValue(TextBlock.TextProperty, "x");
                        column.CellTemplate = new DataTemplate { VisualTree = text };
                    }
                    return view;
                }

                var list = new ListView { ItemsSource = new[] { "row" }, View = CreateView("^(?<Level>)(?<Message>)") };
                var window = new Window
                {
                    Content = list, Width = 600, Height = 200, Left = -10000, Top = -10000,
                    ShowActivated = false, ShowInTaskbar = false
                };
                try
                {
                    window.Show();
                    window.UpdateLayout();
                    var view = CreateView("^(?<Time>)(?<Level>)(?<Context>)(?<Thread>)(?<Process>)(?<Source>)(?<Message>)");
                    list.View = view;
                    window.UpdateLayout();
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                    foreach (var column in view.Columns)
                    {
                        var header = (FrameworkElement)column.HeaderTemplate.LoadContent();
                        header.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                        Assert.IsGreaterThanOrEqualTo(header.DesiredSize.Width, column.ActualWidth);
                    }
                }
                finally { window.Close(); }
            });
        }

        private const string RegexPattern =
            @"^(?<Time>\d{4}-\d{2}-\d{2}\s[^\s]+)\s+(?<Level>[^\s]+)\s+(?<Thread>[^\s]+)\s+(?<Component>[^\s]+)\s+(?<Message>.*)$";

        private const string Source =
            "2024-01-15 08:32:11.482 INFO ThreadA Component1 hello world";

        [TestMethod]
        [DataRow(double.NaN)]
        [DataRow(double.PositiveInfinity)]
        [DataRow(0.0)]
        [DataRow(-1.0)]
        public void CreateViewIgnoresIncompleteStoredWidths(double invalidWidth)
        {
            RunOnSta(() =>
            {
                var meta = new LogMetaInformation(new LogFormat { Regex = RegexPattern });
                var widths = Enumerable.Repeat(80.0, meta.FieldNames.Length + 2).ToArray();
                widths[2] = invalidWidth;

                var view = (System.Windows.Controls.GridView)new GridViewFactory(meta, false, null).CreateView(widths);

                Assert.IsTrue(view.Columns.All(column => double.IsNaN(column.Width)));
            });
        }

        [TestMethod]
        public void CreateViewAssignsContentTextProvider()
        {
            RunOnSta(() =>
            {
                var meta = new LogMetaInformation(new LogFormat { Regex = RegexPattern });
                var factory = new GridViewFactory(meta, false, null);

                var view = (System.Windows.Controls.GridView)factory.CreateView(null);

                Assert.AreEqual(meta.FieldNames.Length + 2, view.Columns.Count);

                var columns = view.Columns.Cast<LogGridViewColumn>().ToArray();

                Assert.IsNull(columns[0].ContentTextProvider);
                Assert.IsNotNull(columns[1].ContentTextProvider);

                for (var i = 0; i < meta.FieldNames.Length; i++)
                    Assert.IsNotNull(columns[i + 2].ContentTextProvider);

                Assert.IsTrue(columns[1].FitToContent);
                for (var i = 0; i < meta.FieldNames.Length; i++)
                    Assert.IsTrue(columns[i + 2].FitToContent);

                var line = CreateLine(meta);

                Assert.AreEqual("0", columns[1].ContentTextProvider!(line));
                Assert.AreEqual("ThreadA",
                    columns[2 + Array.IndexOf(meta.FieldNames, "Thread")].ContentTextProvider!(line));
                Assert.AreEqual("hello world",
                    columns[2 + Array.IndexOf(meta.FieldNames, "Message")].ContentTextProvider!(line));
            });
        }

        [TestMethod]
        [DataRow("ru")]
        [DataRow("de")]
        [DataRow("ja")]
        public void ColumnHeadersBindToRegexGroupNames(string language)
        {
            RunOnSta(() =>
            {
                var previous = CultureInfo.CurrentUICulture;
                try
                {
                    CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
                    var meta = new LogMetaInformation(new LogFormat { Regex = RegexPattern });
                    var expected = new[] { "Index" }.Concat(meta.FieldNames).ToArray();

                    for (var i = 0; i < expected.Length; i++)
                    {
                        var header = new LogGridViewHeader { DataContext = new HeaderViewModel(expected[i], null) };
                        var label = ((DockPanel)header.Content).Children.OfType<TextBlock>().Single();
                        var binding = label.GetBindingExpression(TextBlock.TextProperty).ParentBinding;
                        Assert.AreEqual(nameof(HeaderViewModel.FieldHeader), binding.Path.Path);
                        Assert.IsNull(binding.Converter);
                        Assert.AreEqual(expected[i], ((HeaderViewModel)header.DataContext).FieldHeader);
                    }
                }
                finally
                {
                    CultureInfo.CurrentUICulture = previous;
                }
            });
        }

        private static LineViewModel CreateLine(LogMetaInformation meta, string source = Source)
        {
            var parser = new RegexBasedLineParser(meta);
            return new LineViewModel(0, source, parser, new Selection(),
                new TransformationPerformer(Array.Empty<string>()));
        }

        [TestMethod]
        [DataRow("en", "Filter column")]
        [DataRow("ru", "Фильтр столбца")]
        [DataRow("de", "Spalte filtern")]
        [DataRow("fr", "Filtrer la colonne")]
        [DataRow("es", "Filtrar columna")]
        [DataRow("ja", "列のフィルター")]
        [DataRow("pl", "Filtruj kolumnę")]
        [DataRow("pt-BR", "Filtrar coluna")]
        public void ColumnFilterToolTipAndAutomationNameUseSelectedLanguage(string language, string expected)
        {
            RunOnSta(() =>
            {
                var previous = CultureInfo.CurrentUICulture;
                try
                {
                    CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
                    var header = new LogGridViewHeader { DataContext = new HeaderViewModel("Level", null) };
                    var button = (ToggleButton)header.FindName("CategoriesSearchButton");
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

                    Assert.AreEqual(expected, button.ToolTip);
                    Assert.AreEqual(expected, AutomationProperties.GetName(button));
                }
                finally
                {
                    CultureInfo.CurrentUICulture = previous;
                }
            });
        }

        private static void RunOnSta(Action action)
        {
            Exception error = null;
            var thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    error = e;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (error != null)
                throw error;
        }
    }
}
