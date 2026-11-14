using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using LogGrokX.Controls.TextRender;
using LogGrokX.MarkedLines;

namespace LogGrokX.Controls.ListControls
{
   public class MarkedLinesListView : BaseListView
    {
        public static readonly DependencyProperty ReadonlySelectedItemsProperty = DependencyProperty.Register(
            "ReadonlySelectedItems", typeof(IEnumerable), typeof(MarkedLinesListView),
            new PropertyMetadata(default(IEnumerable)));

        public IEnumerable ReadonlySelectedItems
        {
            get => (IEnumerable) GetValue(ReadonlySelectedItemsProperty);
            set => SetValue(ReadonlySelectedItemsProperty, value);
        }

        public DelegateCommand CopyDocumentLinesCommand { get; }

        public MarkedLinesListView()
        {
            CopyDocumentLinesCommand = new DelegateCommand(CopyDocumentLines);
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

            TextCopy.ClipboardService.SetText(text.ToString());
        }

        protected override void OnSelectionChanged(SelectionChangedEventArgs e)
        {
            CancelSelectionOperations();
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
