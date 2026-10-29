using System.ComponentModel;
using System.Windows;
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
                Dispatcher.BeginInvoke(() => SourceList.ScrollIntoView(line));
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
