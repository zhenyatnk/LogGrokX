using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace LogGrokX.MarkedLines
{
    public class MarkedLinesViewModel : ViewModelBase, IDisposable
    {
        private readonly ObservableCollection<MarkedLineViewModel> _markedLines = new();
        private readonly ObservableCollection<DocumentViewModel> _documents;
        private readonly HashSet<DocumentViewModel> _alreadySubscribed = new();
        private bool _disposed;
        
        public ObservableCollection<MarkedLineViewModel> MarkedLines => _markedLines;

        public DelegateCommand ItemActivatedCommand { get; }

        public bool HaveMarkedLines => _markedLines.Count != 0;
        
        public IEnumerable? SelectedItems
        {
            get;
            set;
        }

        public MarkedLinesViewModel(ObservableCollection<DocumentViewModel> documents)
        {
            _documents = documents;
            SubscribeToNewDocumentChanges(_documents);

            _documents.CollectionChanged += OnDocumentsCollectionChanged;

            ItemActivatedCommand = new DelegateCommand(
                o =>
                {
                    if (o is not MarkedLineViewModel item) return;
                    NavigationRequested?.Invoke(item.Document, item.Index);
                });
            
            UpdateLinesCollection();
        }
        
        public event Action<DocumentViewModel, int>? NavigationRequested;

        private void OnDocumentsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            SubscribeToNewDocumentChanges(_documents);
            UpdateLinesCollection();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _documents.CollectionChanged -= OnDocumentsCollectionChanged;
            foreach (var document in _alreadySubscribed)
                document.MarkedLinesChanged -= UpdateLinesCollection;
            _alreadySubscribed.Clear();
        }

        private void SubscribeToNewDocumentChanges(ObservableCollection<DocumentViewModel> documents)
        {
            var newDocuments = new HashSet<DocumentViewModel>(documents);
            foreach (var document in _alreadySubscribed.ToArray())
            {
                if (newDocuments.Contains(document))
                    continue;

                document.MarkedLinesChanged -= UpdateLinesCollection;
                _alreadySubscribed.Remove(document);
            }

            foreach (var documentViewModel in documents)
            {
                if (_alreadySubscribed.Add(documentViewModel))
                    documentViewModel.MarkedLinesChanged += UpdateLinesCollection;
            }
        }

        private void UpdateLinesCollection()
        {
            var allDocuments = new HashSet<DocumentViewModel>(_documents);
            for (var i = _markedLines.Count - 1; i >= 0; i--)
            {
                if (!allDocuments.Contains(_markedLines[i].Document))
                    _markedLines.RemoveAt(i);
            }

            var index = 0;
            
            foreach (var (document, (lineNumber, text)) in _documents.SelectMany(
                document => document.MarkedLineViewModels.Select(line => (document, line))))
            {
                if (_markedLines.Count <= index || _markedLines[index].Document != document)
                {
                    _markedLines.Insert(index, new MarkedLineViewModel(document, lineNumber, text));
                    index++;
                    continue;
                }

                while (index < _markedLines.Count && _markedLines[index].Index < lineNumber)
                {
                    _markedLines.RemoveAt(index);
                }

                if (index >= _markedLines.Count || _markedLines[index].Index > lineNumber)
                {
                    _markedLines.Insert(index, new MarkedLineViewModel(document, lineNumber, text));
                }

                index++;
            }

            while (_markedLines.Count > index)
            {
                _markedLines.RemoveAt(index);
            }
            
            InvokePropertyChanged(nameof(HaveMarkedLines));
        }
    }
}
