using System.Windows;

namespace LogGrokX.Search;

public partial class SavedSearchesWindow
{
    private readonly SavedSearchesViewModel _viewModel;

    public SavedSearchesWindow(SavedSearchPatternStore store)
    {
        _viewModel = new SavedSearchesViewModel(store);
        DataContext = _viewModel;
        InitializeComponent();
        Closed += (_, _) => _viewModel.Dispose();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
