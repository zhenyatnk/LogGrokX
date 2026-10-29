using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using LogGrokX.Symbols;

namespace LogGrokX
{
    public partial class CallStackWindow
    {
        public CallStackWindow(CallStackViewModel viewModel)
        {
            DataContext = viewModel;
            InitializeComponent();
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CallStackViewModel.CurrentSourceLine) &&
                ((CallStackViewModel)DataContext).CurrentSourceLine is { } line)
                Dispatcher.BeginInvoke(() => CenterLine(line), DispatcherPriority.Loaded);
        }

        private void CenterLine(SourceLineViewModel line)
        {
            var index = SourceList.Items.IndexOf(line);
            if (index < 0)
                return;

            SourceList.UpdateLayout();
            if (FindScrollViewer(SourceList) is not { } scrollViewer)
            {
                SourceList.ScrollIntoView(line);
                return;
            }

            scrollViewer.ScrollToVerticalOffset(Math.Max(0, index - Math.Max(0, scrollViewer.ViewportHeight - 1) / 2));
            scrollViewer.ScrollToHorizontalOffset(0);
        }

        private static ScrollViewer? FindScrollViewer(DependencyObject element)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            {
                var child = VisualTreeHelper.GetChild(element, i);
                if ((child as ScrollViewer ?? FindScrollViewer(child)) is { } scrollViewer)
                    return scrollViewer;
            }

            return null;
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
