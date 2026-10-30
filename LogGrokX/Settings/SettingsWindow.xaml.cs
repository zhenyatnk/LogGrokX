using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using LogGrokX.Localization;

namespace LogGrokX.Settings
{
    public partial class SettingsWindow
    {
        private static string JsonFilter => Strings.Get("JsonFilter");
        private bool _saved;

        public SettingsWindow(SettingsViewModel viewModel)
        {
            DataContext = viewModel;
            InitializeComponent();
            viewModel.NavigationRequested += OnNavigationRequested;
            Closed += (_, _) => viewModel.NavigationRequested -= OnNavigationRequested;
        }

        private void OnProblemClick(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkContentElement { DataContext: SettingsProblem problem } &&
                DataContext is SettingsViewModel viewModel)
                viewModel.NavigateTo(problem);
        }

        private void OnNavigationRequested(SettingsProblem problem)
        {
            switch (problem.Field)
            {
                case SettingsField.BigLineSize:
                    SettingsTabs.SelectedItem = ViewTab;
                    FocusLater(() => FocusTextBox(BigLineSizeBox));
                    break;
                case SettingsField.MaxDumpsCount:
                    SettingsTabs.SelectedItem = DebugTab;
                    FocusLater(() => FocusTextBox(MaxDumpsCountBox));
                    break;
                case SettingsField.ProfileName:
                    SettingsTabs.SelectedItem = ProfilesTab;
                    FocusLater(() => FocusTextBox(ProfileNameBox));
                    break;
                case SettingsField.LogFormatRegex or SettingsField.IndexedFields or
                    SettingsField.Transformations or SettingsField.XorMask:
                    SettingsTabs.SelectedItem = ProfilesTab;
                    ProfileTabs.SelectedItem = LogFormatsTab;
                    FocusLater(() => FocusLogFormatField(problem));
                    break;
                case SettingsField.ColorRegex or SettingsField.ForegroundColor or SettingsField.BackgroundColor:
                    SettingsTabs.SelectedItem = ProfilesTab;
                    ProfileTabs.SelectedItem = ColorsTab;
                    if (problem.ColorRule != null)
                    {
                        var column = problem.Field switch
                        {
                            SettingsField.ForegroundColor => 1,
                            SettingsField.BackgroundColor => 2,
                            _ => 0
                        };
                        FocusLater(() => FocusCell(ColorRulesGrid, problem.ColorRule, column));
                    }
                    break;
                case SettingsField.MappingValue:
                    SettingsTabs.SelectedItem = ValueMappingsTab;
                    if (problem.MappingItem != null)
                        FocusLater(() => FocusCell(MappingItemsGrid, problem.MappingItem, 0));
                    break;
            }
        }

        private void FocusLater(Action focus) =>
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                UpdateLayout();
                focus();
            });

        private void FocusLogFormatField(SettingsProblem problem)
        {
            if (problem.LogFormat == null ||
                LogFormatsList.ItemContainerGenerator.ContainerFromItem(problem.LogFormat) is not DependencyObject container)
                return;
            var box = FindDescendant<TextBox>(container, candidate => Equals(candidate.Tag, problem.Field));
            if (box != null)
                FocusTextBox(box);
        }

        private static void FocusTextBox(TextBox box)
        {
            box.BringIntoView();
            box.Focus();
            Keyboard.Focus(box);
            box.SelectAll();
        }

        private static void FocusCell(DataGrid grid, object item, int columnIndex)
        {
            if (columnIndex >= grid.Columns.Count)
                return;
            var column = grid.Columns[columnIndex];
            grid.SelectedItem = item;
            grid.ScrollIntoView(item, column);
            grid.UpdateLayout();
            grid.CurrentCell = new DataGridCellInfo(item, column);
            if (grid.ItemContainerGenerator.ContainerFromItem(item) is DataGridRow row &&
                column.GetCellContent(row)?.Parent is DataGridCell cell)
            {
                cell.BringIntoView();
                cell.Focus();
                grid.BeginEdit();
            }
            else
            {
                grid.Focus();
            }
        }

        private static T? FindDescendant<T>(DependencyObject parent, Func<T, bool> predicate) where T : DependencyObject
        {
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            {
                var child = VisualTreeHelper.GetChild(parent, index);
                if (child is T match && predicate(match))
                    return match;
                if (FindDescendant(child, predicate) is { } nested)
                    return nested;
            }
            return null;
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is SettingsViewModel viewModel && viewModel.Save())
            {
                _saved = true;
                Close();
            }
        }

        private void OnFixAndSaveClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is SettingsViewModel viewModel && viewModel.FixAndSave())
            {
                _saved = true;
                Close();
            }
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_saved && DataContext is SettingsViewModel viewModel)
                viewModel.Cancel();
            base.OnClosing(e);
        }

        private void OnImportValueMappingsClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is not SettingsViewModel viewModel)
                return;

            var dialog = new OpenFileDialog
            {
                Title = Strings.Get("ImportValueMappings"),
                Filter = JsonFilter,
                Multiselect = true
            };
            if (dialog.ShowDialog(this) == true)
                viewModel.ImportValueMappings(dialog.FileNames);
        }

        private void OnExportValueMappingClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is SettingsViewModel { SelectedValueMapping: { } selected } viewModel)
                Export(viewModel, selected.DisplayName, all: false);
        }

        private void OnExportAllValueMappingsClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is SettingsViewModel viewModel)
                Export(viewModel, "value-mappings", all: true);
        }

        private void Export(SettingsViewModel viewModel, string name, bool all)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var fileName = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
            var dialog = new SaveFileDialog
            {
                Title = all ? Strings.Get("ExportAllValueMappings") : Strings.Get("ExportValueMapping"),
                Filter = JsonFilter,
                DefaultExt = ".json",
                FileName = (fileName.Length > 0 ? fileName : "value-mappings") + ".json"
            };
            if (dialog.ShowDialog(this) == true)
                viewModel.ExportValueMappings(dialog.FileName, all);
        }
    }
}
