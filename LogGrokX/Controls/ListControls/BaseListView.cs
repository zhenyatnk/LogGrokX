using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using LogGrokX.Controls.TextRender;
using LogGrokX.Localization;
using Microsoft.Win32;

namespace LogGrokX.Controls.ListControls
{
    public abstract class BaseListView : System.Windows.Controls.ListView
    {
        private CancellationTokenSource? _selectionOperationCancellation;

        public static readonly DependencyProperty BulkOperationsProperty = DependencyProperty.Register(
            nameof(BulkOperations), typeof(BulkOperationService), typeof(BaseListView));

        public BulkOperationService? BulkOperations
        {
            get => (BulkOperationService?)GetValue(BulkOperationsProperty);
            set => SetValue(BulkOperationsProperty, value);
        }

        protected BaseListView()
        {
            CommandBindings.Add(new CommandBinding(RoutedCommands.CopyToClipboard,
                (_, args) => CopyToClipboard("CopyToClipboard", false, args),
                CanExecuteCopyToClipboard));
            CommandBindings.Add(new CommandBinding(RoutedCommands.CopyAsDisplayed,
                (_, args) => CopyToClipboard("CopyAsDisplayed", true, args),
                CanExecuteCopyToClipboard));
        }

        private void CopyToClipboard(string commandName, bool asDisplayed, ExecutedRoutedEventArgs args)
        {
            Trace.TraceInformation($"{commandName}.Execute");
            CopySelectedItemsToClipboard(asDisplayed);
            args.Handled = true;
        }

        private void CanExecuteCopyToClipboard(object _, CanExecuteRoutedEventArgs args)
        {
            args.CanExecute = GetSelectedIndices().Any();
            args.Handled = true;
        }

        private async void CopySelectedItemsToClipboard(bool asDisplayed)
        {
            var items = GetSelectedIndices().Select(index => Items[index]);
            var foldingState = asDisplayed ? TextView.GetSharedFoldingState(this) : null;
            var lines = items.Select(item => asDisplayed && item is BaseLogLineViewModel viewModel
                ? viewModel.GetDisplayText(foldingState)
                : item.ToString());
            await CopyLinesWithFallbackAsync(lines);
        }

        protected async Task CopyLinesWithFallbackAsync(IEnumerable<string?> lines)
        {
            var cancellationToken = BeginSelectionOperation("Copy");
            try
            {
                var result = await BulkSelection.CopyAsync(lines,
                    text => TextCopy.ClipboardService.SetText(text), cancellationToken, BulkOperations is null ? null : BulkOperations.Report);
                if (result == BulkCopyResult.Copied)
                    return;

                var dialog = new SaveFileDialog
                {
                    Title = Strings.Get("SaveSelection"),
                    Filter = Strings.Get("TextFilesFilter"),
                    DefaultExt = ".txt",
                    FileName = "selection.txt"
                };
                if (dialog.ShowDialog(Window.GetWindow(this)) == true)
                    await BulkSelection.SaveAsync(lines, dialog.FileName, cancellationToken, BulkOperations is null ? null : BulkOperations.Report);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show(Window.GetWindow(this), Strings.Format("SelectionSaveFailed", e.Message),
                    Strings.Get("SaveSelection"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                BulkOperations?.Complete(cancellationToken);
            }
        }

        protected CancellationToken BeginSelectionOperation(string titleKey, long? total = null)
        {
            if (BulkOperations != null)
                return BulkOperations.Begin(Strings.Get(titleKey), total ?? GetSelectedCount(), DataContext ?? this);
            CancelSelectionOperations();
            _selectionOperationCancellation = new CancellationTokenSource();
            return _selectionOperationCancellation.Token;
        }

        protected void CancelSelectionOperations()
        {
            if (BulkOperations != null)
            {
                BulkOperations.Cancel(this);
                return;
            }
            _selectionOperationCancellation?.Cancel();
            _selectionOperationCancellation?.Dispose();
            _selectionOperationCancellation = null;
        }

        protected abstract IEnumerable<int> GetSelectedIndices();

        protected abstract long GetSelectedCount();
    }
}
