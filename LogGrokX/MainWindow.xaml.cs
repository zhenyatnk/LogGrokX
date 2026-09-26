using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using AvalonDock.Layout;
using AvalonDock.Serializer.Xml;
using LogGrokX.AvalonDockExtensions;

namespace LogGrokX
{
    public static class Constants
    {
        public const string MarkedLinesContentId = "###MarkedLinesContentId";

        public const string MergedViewContentId = "###MergedViewContentId";

        public const string DefaultAvalonDockLayout =
            @"<?xml version=""1.0"" encoding=""utf-8""?>
                <LayoutRoot>
                    <RootPanel Orientation=""Horizontal"">
                    <LayoutDocumentPane />
                    <LayoutAnchorablePane DockWidth=""300"" DocMinWidth=""300"">
                    <LayoutAnchorable 
                        AutoHideMinWidth=""100"" 
                        AutoHideMinHeight=""100"" 
                        Title=""Marked lines"" 
                        IsSelected=""True"" 
                        ContentId=""###MarkedLinesContentId"" 
                        CanClose=""False"" 
                        CanHide=""False""/>
                    </LayoutAnchorablePane>
                    </RootPanel>
                    <TopSide />
                    <RightSide />
                    <LeftSide />
                    <BottomSide />
                    <FloatingWindows />
                    <Hidden />
        </LayoutRoot>";
    }

    public partial class MainWindow
    {
        private sealed class WindowPlacement
        {
            public double Left { get; set; }
            public double Top { get; set; }
            public double Width { get; set; }
            public double Height { get; set; }
            public WindowState State { get; set; }
        }

        private readonly MainWindowViewModel _viewModel;

        private LayoutDocument? _mergedDocument;

        public MainWindow(MainWindowViewModel mainWindowViewModel)
        {
            _viewModel = mainWindowViewModel;
            DataContext = mainWindowViewModel;
            Closing += OnClosing;
            Loaded += OnLoaded;
            PreviewMouseWheel += OnPreviewMouseWheel;
            HorizontalMouseWheel.Enable();
            Closed += (_, _) => HorizontalMouseWheel.Disable();
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;

            InitializeComponent();
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainWindowViewModel.IsMergedView))
                UpdateMergedDocument();
        }

        private void OnPreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            if ((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == 0)
                return;

            if (e.Delta > 0)
                _viewModel.ZoomInCommand.Execute(null);
            else
                _viewModel.ZoomOutCommand.Execute(null);

            e.Handled = true;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            RestoreWindowPlacement();
            LoadLayout();
            DockingManager.ActiveContentChanged += (_, _) => UpdateMergedViewActive();
            UpdateMergedDocument();
        }

        private void UpdateMergedDocument()
        {
            if (_viewModel.IsMergedView)
                ShowMergedDocument();
            else
                HideMergedDocument();

            UpdateMergedViewActive();
        }

        private void UpdateMergedViewActive()
        {
            if (_mergedDocument == null)
            {
                _viewModel.IsMergedViewActive = false;
                return;
            }

            var active = DockingManager.ActiveContent;
            if (active == null)
                return;

            if (ReferenceEquals(active, _mergedDocument.Content))
            {
                _viewModel.IsMergedViewActive = true;
                return;
            }

            var isDocument = DockingManager.Layout.Descendents()
                .OfType<LayoutDocument>()
                .Any(document => ReferenceEquals(document.Content, active));

            if (isDocument)
                _viewModel.IsMergedViewActive = false;
        }

        private void ShowMergedDocument()
        {
            if (_mergedDocument != null)
                return;

            var documentPane = DockingManager.Layout.Descendents()
                .OfType<LayoutDocumentPane>()
                .FirstOrDefault();
            if (documentPane == null)
                return;

            _mergedDocument = new LayoutDocument
            {
                Title = "Merged files",
                ContentId = Constants.MergedViewContentId,
                CanClose = false,
                Content = new ContentControl
                {
                    Content = _viewModel.MergedViewModel,
                    ContentTemplate = (DataTemplate)FindResource("MergedViewTemplate")
                }
            };

            documentPane.Children.Add(_mergedDocument);
            _mergedDocument.IsActive = true;
        }

        private void HideMergedDocument()
        {
            if (_mergedDocument == null)
                return;

            foreach (var documentPane in DockingManager.Layout.Descendents().OfType<LayoutDocumentPane>())
                documentPane.Children.Remove(_mergedDocument);

            _mergedDocument = null;
        }

        private void LoadLayout()
        {
            var settingsFileName = GetLayoutFileName();
            using var reader =
                File.Exists(settingsFileName)
                    ? new StreamReader(settingsFileName)
                    : new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes(Constants.DefaultAvalonDockLayout)));
            
            var contentProvider = (IContentProvider) DataContext;
            var layoutSerializer = new XmlLayoutSerializer(DockingManager);
            
            layoutSerializer.LayoutSerializationCallback += (_, args) =>
            {
                if (args.Model is LayoutDocument)
                {
                    args.Cancel = true;
                    return;
                }

                var content = contentProvider.GetContent(args.Model.ContentId);
                if (content != null)
                {
                    args.Content = new ContentControl { Content = content };
                }
            };
            layoutSerializer.Deserialize(reader);

            foreach (var layoutAnchorable in DockingManager.Layout.Children
                .OfType<LayoutAnchorable>()
                .Where(l => l.IsHidden).ToList())
            {
                layoutAnchorable.Show();
            }
        }

        private void OnClosing(object? sender, CancelEventArgs e)
        {
            SaveWindowPlacement();
            SaveLayout();
        }

        private void SaveLayout()
        {
            var fileName = GetLayoutFileName();
            var serializer = new XmlLayoutSerializer(DockingManager);
            using var writer = new StreamWriter(fileName);
            
            serializer.Serialize(writer);
        }

        private void RestoreWindowPlacement()
        {
            try
            {
                var fileName = GetWindowPlacementFileName();
                if (!File.Exists(fileName)) return;

                using var stream = File.OpenRead(fileName);
                var placement = JsonSerializer.Deserialize<WindowPlacement>(stream);
                if (placement == null) return;

                if (placement.Width > 0) Width = placement.Width;
                if (placement.Height > 0) Height = placement.Height;

                if (IsOnAnyScreen(placement.Left, placement.Top))
                {
                    Left = placement.Left;
                    Top = placement.Top;
                    WindowStartupLocation = WindowStartupLocation.Manual;
                }

                if (placement.State == WindowState.Maximized)
                    WindowState = WindowState.Maximized;
            }
            catch (Exception e)
            {
                Trace.TraceWarning($"Failed to restore window placement: {e.Message}");
            }
        }

        private void SaveWindowPlacement()
        {
            try
            {
                var placement = new WindowPlacement
                {
                    Width = RestoreBounds.Width,
                    Height = RestoreBounds.Height,
                    Left = RestoreBounds.Left,
                    Top = RestoreBounds.Top,
                    State = WindowState
                };

                using var stream = File.Create(GetWindowPlacementFileName());
                JsonSerializer.Serialize(stream, placement, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception e)
            {
                Trace.TraceWarning($"Failed to save window placement: {e.Message}");
            }
        }

        private static bool IsOnAnyScreen(double left, double top)
        {
            return !double.IsNaN(left) && !double.IsNaN(top) &&
                   left > SystemParameters.VirtualScreenLeft - 10 &&
                   top > SystemParameters.VirtualScreenTop - 10 &&
                   left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 10 &&
                   top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 10;
        }

        private static string GetLayoutFileName()
        {
            return HomeDirectoryPathProvider.GetDataFileFullPath("layout.settings");
        }

        private static string GetWindowPlacementFileName()
        {
            return HomeDirectoryPathProvider.GetDataFileFullPath("window.settings");
        }
    }
}
