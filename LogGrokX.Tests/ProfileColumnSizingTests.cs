#nullable enable
using System;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using LogGrokX.Controls;
using LogGrokX.Controls.GridView;
using LogGrokX.MarkedLines;
using LogGrokX.Search;
using LogGrokX.Theming;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using LogListView = LogGrokX.Controls.ListControls.ListView;
using LogPanel = LogGrokX.Controls.ListControls.VirtualizingStackPanel.VirtualizingStackPanel;

namespace LogGrokX.Tests;

[TestClass]
[DoNotParallelize]
public class ProfileColumnSizingTests
{
    [TestMethod]
    [DataRow(false, 300.0)]
    [DataRow(true, 300.0)]
    [DataRow(false, 12.0)]
    [DataRow(true, 12.0)]
    public void ChangingProfileInMergedViewSizesAllHiddenDocumentTabs(bool detachHiddenTabs, double manualWidth)
    {
        RunOnSta(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), $"loggrokx-column-profile-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            var settingsPath = Path.Combine(directory, "appsettings.yaml");
            File.Copy(Path.Combine(RepositoryRoot, "LogGrokX", "appsettings.yaml"), settingsPath);
            var paths = Enumerable.Range(0, 3).Select(i => Path.Combine(directory, $"sample-{i}.log")).ToArray();
            try
            {
                var settings = ApplicationSettings.LoadFromFile(settingsPath);
                settings.SelectedProfile = "Classic";
                using var main = CreateMain(settings);
                foreach (var path in paths)
                {
                    File.WriteAllText(path, string.Concat(Enumerable.Repeat(
                        "2026-10-21 12:34:02.229 - [TRAC] - [1:8124:10304:M:U] - [workplace proxy.exe] - [DBG] - TraceLogger registered\n",
                        20)));
                    main.AddDocument(path);
                }
                WaitFor(() => main.Documents.All(document => !document.LogViewModel.IsLoading));
                main.IsMergedView = true;
                WaitFor(() => !main.MergedViewModel.IsLoading && main.MergedViewModel.Lines.Count == 60);

                var resources = LoadAppResources();
                resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/LogGrokX;component/Styles/ListViewItemStyle.xaml")
                });
                var template = CreateTableTemplate(resources);
                var documents = main.Documents.Select(document => new ContentControl
                {
                    Content = document.LogViewModel,
                    ContentTemplate = template,
                    Visibility = Visibility.Collapsed
                }).ToArray();
                var merged = new ContentControl
                {
                    Content = main.MergedViewModel,
                    ContentTemplate = template,
                    Visibility = Visibility.Collapsed
                };
                var root = new Grid();
                foreach (var document in documents)
                    root.Children.Add(document);
                root.Children.Add(merged);
                main.Documents.CollectionChanged += (_, args) =>
                {
                    if (args.Action == NotifyCollectionChangedAction.Replace)
                        for (var index = 0; index < documents.Length; index++)
                            documents[index].Content = main.Documents[index].LogViewModel;
                };
                var window = new Window
                {
                    Content = root, Resources = resources, Width = 1400, Height = 350,
                    Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false
                };
                void Show(ContentControl selected)
                {
                    foreach (var host in documents.Append(merged))
                        host.Visibility = ReferenceEquals(host, selected) ? Visibility.Visible : Visibility.Collapsed;
                    window.UpdateLayout();
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                    window.UpdateLayout();
                }
                try
                {
                    window.Show();
                    foreach (var document in documents)
                    {
                        Show(document);
                        WaitFor(() => GetList(document).Items.Count == 20);
                        WaitForSizing(window);
                    }
                    FindColumn(GetList(documents[^1]), "Time").Width = manualWidth;
                    window.UpdateLayout();
                    foreach (var profile in new[] { "Venn", "Classic", "Venn" })
                    {
                        Show(merged);
                        WaitFor(() => GetList(merged).Items.Count == 60);
                        if (detachHiddenTabs)
                            foreach (var document in documents)
                                root.Children.Remove(document);

                        main.SelectedProfile = main.Profiles.Single(item => item.Name == profile);
                        WaitFor(() => main.Documents.All(document => !document.LogViewModel.IsLoading)
                                      && !main.MergedViewModel.IsLoading && main.MergedViewModel.Lines.Count == 60);
                        WaitForSizing(window);
                        AssertColumnsFit(GetList(merged));
                        foreach (var document in documents)
                        {
                            if (detachHiddenTabs)
                                root.Children.Add(document);
                            Show(document);
                            WaitForSizing(window);
                            AssertColumnsFit(GetList(document), profile == "Classic" ? "Time" : null);
                            if (profile == "Classic")
                                Assert.AreEqual(manualWidth, FindColumn(GetList(document), "Time").ActualWidth, 0.01,
                                    "Profile switching lost the manually resized column.");
                        }
                    }
                    Show(merged);
                    Show(documents[^1]);
                    WaitForSizing(window);
                    AssertColumnsFit(GetList(documents[^1]));
                }
                finally
                {
                    window.Close();
                }
            }
            finally
            {
                foreach (var path in paths.Append(settingsPath))
                    File.Delete(path);
                Directory.Delete(directory);
            }
        });
    }

    private static LogListView GetList(DependencyObject host) =>
        host.GetVisualChildren<LogListView>().Single();

    private static DataTemplate CreateTableTemplate(ResourceDictionary resources)
    {
        var list = new FrameworkElementFactory(typeof(LogListView));
        list.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("Lines"));
        list.SetBinding(ListView.ViewProperty, new Binding("CustomView"));
        list.SetBinding(LogListView.ColumnSettingsProperty, new Binding("ColumnSettings"));
        list.SetBinding(LogListView.IsLoadingProperty, new Binding("IsLoading"));
        list.SetValue(ItemsControl.ItemContainerStyleProperty, resources["LogGridItemContainerStyle"]);
        list.SetValue(Control.FontFamilyProperty, resources["JetBrainsMono"]);
        list.SetValue(Control.FontSizeProperty, 12.0);
        list.SetValue(ScrollViewer.CanContentScrollProperty, true);
        list.SetValue(ItemsControl.ItemsPanelProperty,
            new ItemsPanelTemplate(new FrameworkElementFactory(typeof(LogPanel))));
        return new DataTemplate { VisualTree = list };
    }

    private static void AssertColumnsFit(LogListView list, string? manuallySizedField = null)
    {
        var view = (GridView)list.View;
        foreach (var column in view.Columns)
        {
            var header = list.GetVisualChildren<GridViewColumnHeader>().Single(item => ReferenceEquals(item.Column, column));
            var content = header.GetVisualChildren<LogGridViewHeader>().SingleOrDefault();
            if (content == null)
                continue;
            if (content.DataContext is not HeaderViewModel { FieldHeader: { Length: > 0 } name })
                continue;
            if (name == manuallySizedField)
                continue;
            content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Assert.IsGreaterThanOrEqualTo(content.DesiredSize.Width + header.Padding.Left + header.Padding.Right - 1, column.ActualWidth,
                $"Column {name} clips its header after profile change.");
            if (column is LogGridViewColumn { ContentTextProvider: { } provider })
            {
                var typeface = new Typeface(list.FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
                var width = list.Items.Cast<object>().Max(item => new FormattedText(provider(item),
                    CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, list.FontSize,
                    Brushes.Black, 1).WidthIncludingTrailingWhitespace);
                Assert.IsGreaterThanOrEqualTo(width, column.ActualWidth,
                    $"Column {name} clips its content after profile change.");
            }
        }
    }

    private static GridViewColumn FindColumn(LogListView list, string fieldName) =>
        ((GridView)list.View).Columns.Single(column =>
        {
            column.HeaderTemplate.Seal();
            return ((FrameworkElement)column.HeaderTemplate.LoadContent()).DataContext is HeaderViewModel model
                   && model.FieldHeader == fieldName;
        });

    private static string RepositoryRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    private static ResourceDictionary LoadAppResources()
    {
        var source = XDocument.Load(Path.Combine(RepositoryRoot, "LogGrokX", "Bootstrap", "App.xaml")).Root!;
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var dictionary = source.Element(presentation + "Application.Resources")!.Element(presentation + "ResourceDictionary")!;
        foreach (var element in dictionary.Descendants().Where(element =>
                     element.Name.NamespaceName.StartsWith("clr-namespace:LogGrokX", StringComparison.Ordinal)))
            element.Name = XName.Get(element.Name.LocalName, element.Name.NamespaceName + ";assembly=LogGrokX");
        foreach (var attribute in source.Attributes().Where(attribute => attribute.IsNamespaceDeclaration))
            dictionary.SetAttributeValue(attribute.Name, attribute.Value.StartsWith("clr-namespace:LogGrokX", StringComparison.Ordinal)
                ? attribute.Value + ";assembly=LogGrokX" : attribute.Value);
        return (ResourceDictionary)XamlReader.Parse(dictionary.ToString());
    }

    private static MainWindowViewModel CreateMain(ApplicationSettings settings) => new(settings,
        new SearchAutocompleteCache(), new SavedSearchPatternStore(), new UiThemeService(),
        new TimelinePlacementService(settings), new TextZoomService(settings),
        new ThreadGroupingService(settings), new MergedFilesViewService(),
        new UpdateCheckService(settings), documents => new MarkedLinesViewModel(documents));

    private static void WaitForSizing(Window window)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromMilliseconds(600) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
        window.UpdateLayout();
    }

    private static void WaitFor(Func<bool> ready)
    {
        var frame = new DispatcherFrame();
        var started = DateTime.UtcNow;
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(10), DispatcherPriority.Background,
            (_, _) =>
            {
                if (ready() || DateTime.UtcNow - started > TimeSpan.FromSeconds(5))
                    frame.Continue = false;
            }, Dispatcher.CurrentDispatcher);
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
        Assert.IsTrue(ready(), "Document loading did not finish");
    }

    private static void RunOnSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
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
