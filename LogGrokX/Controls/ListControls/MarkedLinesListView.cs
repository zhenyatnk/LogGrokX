using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using LogGrokX.Controls.TextRender;
using LogGrokX.MarkedLines;

namespace LogGrokX.Controls.ListControls
{
   public class MarkedLinesListView : BaseListView
    {
        private int _collectionChangeVersion;

        public static readonly DependencyProperty ReadonlySelectedItemsProperty = DependencyProperty.Register(
            "ReadonlySelectedItems", typeof(IEnumerable), typeof(MarkedLinesListView),
            new PropertyMetadata(default(IEnumerable)));

        public IEnumerable ReadonlySelectedItems
        {
            get => (IEnumerable) GetValue(ReadonlySelectedItemsProperty);
            set => SetValue(ReadonlySelectedItemsProperty, value);
        }

        public DelegateCommand CopyDocumentLinesCommand { get; }

        public DelegateCommand ClearDocumentMarksCommand { get; }

        internal bool IsActivationSuppressed { get; private set; }

        public MarkedLinesListView()
        {
            CopyDocumentLinesCommand = new DelegateCommand(CopyDocumentLines);
            ClearDocumentMarksCommand = new DelegateCommand(ClearDocumentMarks);
            ((INotifyCollectionChanged)Items).CollectionChanged += (_, _) => SuppressActivationUntilLayout();
        }

        private void SuppressActivationUntilLayout()
        {
            var version = ++_collectionChangeVersion;
            IsActivationSuppressed = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (version == _collectionChangeVersion)
                    IsActivationSuppressed = false;
            }), DispatcherPriority.Background);
        }

        private void ClearDocumentMarks(object parameter)
        {
            if (parameter is not DocumentViewModel || DataContext is not MarkedLinesViewModel viewModel)
                return;
            Keyboard.ClearFocus();
            viewModel.ClearDocumentMarksCommand.Execute(parameter);
        }

        private void CopyDocumentLines(object parameter)
        {
            if (parameter is not DocumentViewModel document)
                return;

            var foldingState = TextView.GetSharedFoldingState(this);
            var text = new StringBuilder();
            text.Append(document.Title);
            text.Append(':');

            foreach (var line in Items.OfType<MarkedLineViewModel>()
                         .Where(m => m.Document == document)
                         .OrderBy(m => m.Index))
            {
                text.Append("\r\n");
                text.Append(line.GetDisplayText(foldingState).Replace("\0", string.Empty).TrimEnd());
            }

            try
            {
                TextCopy.ClipboardService.SetText(text.ToString());
            }
            catch (ExternalException)
            {
            }
        }

        protected override void OnSelectionChanged(SelectionChangedEventArgs e)
        {
            ReadonlySelectedItems = SelectedItems;
            base.OnSelectionChanged(e);
        }

        protected override ListViewItem GetContainerForItemOverride()
        {
            return new BaseLogListViewItem(this);
        }

        protected override long GetSelectedCount() => ReadonlySelectedItems?.Cast<object>().Count() ?? 0;

        protected override IEnumerable<int> GetSelectedIndices()
        {
            var selectedItems = new HashSet<object>(ReadonlySelectedItems.Cast<object>());
            for (var i = 0; i < Items.Count; i++)
            {
                if (selectedItems.Contains(Items[i]))
                    yield return i;
            }
        }
    }
}
