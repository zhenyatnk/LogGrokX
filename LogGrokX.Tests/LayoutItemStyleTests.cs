#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using AvalonDock;
using AvalonDock.Controls;
using AvalonDock.Layout;
using LogGrokX.AvalonDockExtensions;
using LogGrokX.Controls;
using LogGrokX.Localization;
using LogGrokX.MarkedLines;
using LogGrokX.Search;
using LogGrokX.Theming;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Tests;

[TestClass]
[DoNotParallelize]
public sealed class LayoutItemStyleTests
{
    [TestMethod]
    public void MarkedLinesPanelUsesLayoutTitleWithoutDocumentBindings()
    {
        RunOnSta(() =>
        {
            using var marked = new MarkedLinesViewModel(new ObservableCollection<DocumentViewModel>());
            var panel = new LayoutAnchorable
            {
                Title = Strings.Get("MarkedLines"),
                Content = new ContentControl { Content = marked }
            };
            VerifyPanelTitle(panel, new LayoutAnchorablePane(panel));
        });
    }

    [TestMethod]
    public void OtherDocumentsUseLayoutTitleWithoutCurrentDocumentBindings()
    {
        RunOnSta(() =>
        {
            var document = new LayoutDocument
            {
                Title = Strings.Get("MergedFiles"),
                Content = new ContentControl { Content = new object() }
            };
            VerifyPanelTitle(document, new LayoutDocumentPane(document));
        });
    }

    [TestMethod]
    public void LogDocumentsKeepFullTooltipAndShortenOnlyInactiveTitles()
    {
        RunOnSta(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), "loggrokx-long-document-name-" + Guid.NewGuid() + ".log");
            File.WriteAllText(path, "test");
            try
            {
                var settings = ApplicationSettings.Instance();
                using var main = new MainWindowViewModel(settings, new SearchAutocompleteCache(),
                    new SavedSearchPatternStore(), new UiThemeService(), new TimelinePlacementService(settings),
                    new TextZoomService(settings), new ThreadGroupingService(settings), new MergedFilesViewService(),
                    new UpdateCheckService(settings), documents => new MarkedLinesViewModel(documents));
                main.AddDocument(path);
                var viewModel = main.CurrentDocument!;
                var document = new LayoutDocument { Content = new ContentControl { Content = viewModel } };
                using var errors = new BindingErrors();
                var selector = LoadSelector();
                var manager = CreateManager(selector, new LayoutDocumentPane(document));
                var item = manager.GetLayoutItemFromModel(document);
                Assert.IsNotNull(item);
                PumpBindings();

                Assert.AreSame(selector.DocumentStyle, item.Style);
                Assert.AreEqual(viewModel.Title, item.Title);
                Assert.AreEqual(viewModel.Title, item.ToolTip);

                viewModel.IsCurrentDocument = false;
                PumpBindings();
                var shortened = new StringShortenerConvereter().Convert(viewModel.Title, typeof(string), "25",
                    System.Globalization.CultureInfo.CurrentCulture);
                Assert.AreEqual(shortened, item.Title);
                Assert.AreEqual(viewModel.Title, item.ToolTip);

                viewModel.IsCurrentDocument = true;
                PumpBindings();
                Assert.AreEqual(viewModel.Title, item.Title);
                Assert.AreEqual(string.Empty, errors.Text);
                GC.KeepAlive(manager);
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    private static void VerifyPanelTitle(LayoutContent panel, ILayoutPanelElement pane)
    {
        using var errors = new BindingErrors();
        var selector = LoadSelector();
        var manager = CreateManager(selector, pane);
        var item = manager.GetLayoutItemFromModel(panel);
        Assert.IsNotNull(item);
        PumpBindings();
        Assert.AreSame(selector.DefaultStyle, item.Style);
        Assert.AreEqual(panel.Title, item.Title);
        Assert.AreEqual(panel.Title, item.ToolTip);

        panel.Title = "Restored panel title";
        PumpBindings();
        Assert.AreEqual(panel.Title, item.Title);
        Assert.AreEqual(panel.Title, item.ToolTip);
        Assert.AreEqual(string.Empty, errors.Text);
        GC.KeepAlive(manager);
    }

    private static DockingManager CreateManager(StyleSelector selector, ILayoutPanelElement pane) => new()
    {
        LayoutItemContainerStyleSelector = selector,
        Layout = new LayoutRoot { RootPanel = new LayoutPanel(pane) }
    };

    private static DocumentLayoutItemStyleSelector LoadSelector()
    {
        var resources = new ResourceDictionary
        {
            Source = new Uri("/LogGrokX;component/Styles/LayoutItemStyles.xaml", UriKind.Relative)
        };
        return (DocumentLayoutItemStyleSelector)resources["DocumentLayoutItemStyleSelector"];
    }

    private static void PumpBindings()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
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
        if (error != null) throw error;
    }

    private sealed class BindingErrors : TraceListener
    {
        private readonly StringBuilder _text = new();
        private readonly SourceLevels _previous = PresentationTraceSources.DataBindingSource.Switch.Level;

        public BindingErrors()
        {
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
            PresentationTraceSources.DataBindingSource.Listeners.Add(this);
        }

        public string Text => _text.ToString();

        public override void Write(string? message) => _text.Append(message);

        public override void WriteLine(string? message) => _text.AppendLine(message);

        protected override void Dispose(bool disposing)
        {
            PresentationTraceSources.DataBindingSource.Listeners.Remove(this);
            PresentationTraceSources.DataBindingSource.Switch.Level = _previous;
            base.Dispose(disposing);
        }
    }
}
