using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Win32;

namespace LogGrokX.Settings
{
    public partial class SettingsWindow
    {
        private const string JsonFilter = "JSON files (*.json)|*.json|All files (*.*)|*.*";

        public SettingsWindow(SettingsViewModel viewModel)
        {
            DataContext = viewModel;
            InitializeComponent();
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is SettingsViewModel viewModel && viewModel.Save())
                Close();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnImportValueMappingsClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is not SettingsViewModel viewModel)
                return;

            var dialog = new OpenFileDialog
            {
                Title = "Import value mappings",
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
                Title = all ? "Export all value mappings" : "Export value mapping",
                Filter = JsonFilter,
                DefaultExt = ".json",
                FileName = (fileName.Length > 0 ? fileName : "value-mappings") + ".json"
            };
            if (dialog.ShowDialog(this) == true)
                viewModel.ExportValueMappings(dialog.FileName, all);
        }
    }
}
