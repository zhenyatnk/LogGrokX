using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Threading;
using LogGrokX.Localization;

namespace LogGrokX.MarkedLines
{
    public class MarkedLinesViewModel : ViewModelBase, IDisposable
    {
        private ObservableCollection<MarkedLineViewModel> _markedLines = new();
        private readonly ObservableCollection<DocumentViewModel> _documents;
        private readonly HashSet<DocumentViewModel> _alreadySubscribed = new();
        private bool _disposed;
        private bool _isClearing;
        private int _updateDepth;
        private int _updateVersion;
        
        public BulkOperationService BulkOperations { get; }

        public ObservableCollection<MarkedLineViewModel> MarkedLines => _markedLines;

        public DelegateCommand ItemActivatedCommand { get; }

        public DelegateCommand ClearDocumentMarksCommand { get; }

        public bool HaveMarkedLines => _markedLines.Count != 0;
        
        public IEnumerable? SelectedItems
        {
            get;
            set;
        }

        public MarkedLinesViewModel(ObservableCollection<DocumentViewModel> documents, BulkOperationService bulkOperations)
        {
            BulkOperations = bulkOperations ?? throw new ArgumentNullException(nameof(bulkOperations));
            _documents = documents;
            SubscribeToNewDocumentChanges(_documents);

            _documents.CollectionChanged += OnDocumentsCollectionChanged;

            ItemActivatedCommand = new DelegateCommand(
                o =>
                {
                    if (_isClearing || _updateDepth != 0 || o is not MarkedLineViewModel item) return;
                    NavigationRequested?.Invoke(item.Document, item.Index);
                });
            ClearDocumentMarksCommand = DelegateCommand.Create<DocumentViewModel>(ClearDocumentMarkedLines);
            
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
            _updateVersion++;
            BulkOperations.Cancel(this);
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

        private void ClearDocumentMarkedLines(DocumentViewModel document)
        {
            if (_disposed || _isClearing || !_documents.Contains(document))
                return;
            _isClearing = true;
            _updateVersion++;
            BulkOperations.Cancel(this);
            try
            {
                document.MarkedLines.Clear();
            }
            finally
            {
                _isClearing = false;
                UpdateLinesCollection();
            }
        }

        private async void UpdateLinesCollection()
        {
            if (_disposed || _isClearing)
                return;
            _updateDepth++;
            var version = ++_updateVersion;
            var cancellationToken = BulkOperations.Begin(Strings.Get("UpdateMarkedLines"),
                _documents.Sum(document => document.MarkedLines.Count), this);
            try
            {
                var existingLines = _markedLines;
                var updatedLines = new List<MarkedLineViewModel>();
                var existingIndex = 0;
                await BulkSelection.ProcessAsync(_documents.SelectMany(
                    document => document.MarkedLineViewModels.Select(line => (document, line))), item =>
                {
                    if (version != _updateVersion)
                        throw new OperationCanceledException();
                    var (document, (lineNumber, text)) = item;
                    while (existingIndex < existingLines.Count &&
                           (existingLines[existingIndex].Document != document || existingLines[existingIndex].Index < lineNumber))
                        existingIndex++;
                    if (existingIndex < existingLines.Count && existingLines[existingIndex].Document == document &&
                        existingLines[existingIndex].Index == lineNumber)
                    {
                        updatedLines.Add(existingLines[existingIndex++]);
                    }
                    else
                    {
                        updatedLines.Add(new MarkedLineViewModel(document, lineNumber, text));
                    }
                }, cancellationToken, BulkOperations.Report);

                if (version != _updateVersion)
                    return;
                _markedLines = new ObservableCollection<MarkedLineViewModel>(updatedLines);
                InvokePropertyChanged(nameof(MarkedLines));
                InvokePropertyChanged(nameof(HaveMarkedLines));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            finally
            {
                _updateDepth--;
                BulkOperations.Complete(cancellationToken);
            }
        }
    }
}
