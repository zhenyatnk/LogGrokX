using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using LogGrokX.Localization;

namespace LogGrokX.Search;

public partial class SavedSearchesWindow
{
    private readonly SavedSearchesViewModel _viewModel;
    private bool _synchronizingSelection;

    public SavedSearchesWindow(SavedSearchPatternStore store)
    {
        _viewModel = new SavedSearchesViewModel(store, ConfirmDiscardChanges);
        DataContext = _viewModel;
        InitializeComponent();
        _viewModel.FocusListRequested += FocusSelectedSearch;
        _viewModel.FocusEditorRequested += () => Dispatcher.BeginInvoke(new Action(() => EditNameBox.Focus()));
        Loaded += (_, _) => FocusSelectedSearch();
        Closing += OnClosing;
        Closed += (_, _) => _viewModel.Dispose();
    }

    private bool ConfirmDiscardChanges() => MessageBox.Show(this,
        Strings.Get("DiscardSavedSearchChangesConfirmation"), Strings.Get("SavedSearches"),
        MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;

    private void OnClosing(object? sender, CancelEventArgs e) => e.Cancel = !_viewModel.ConfirmDiscardChanges();

    private void OnSavedSearchSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_synchronizingSelection) return;
        _synchronizingSelection = true;
        try
        {
            _viewModel.SelectedSearch = SavedSearchesList.SelectedItem as SavedSearchPattern;
            if (!ReferenceEquals(SavedSearchesList.SelectedItem, _viewModel.SelectedSearch))
            {
                SavedSearchesList.SetCurrentValue(Selector.SelectedItemProperty, _viewModel.SelectedSearch);
                FocusSelectedSearch();
            }
        }
        finally
        {
            _synchronizingSelection = false;
        }
    }

    private void FocusSelectedSearch() => Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
    {
        if (_viewModel.SelectedSearch is { } selected && SavedSearchesList.Items.Contains(selected))
        {
            SavedSearchesList.ScrollIntoView(selected);
            SavedSearchesList.UpdateLayout();
            if (SavedSearchesList.ItemContainerGenerator.ContainerFromItem(selected) is ListBoxItem item)
            {
                item.Focus();
                return;
            }
        }

        SavedSearchesList.Focus();
    }));

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
