using System;
using System.Collections.Generic;
using System.Linq;
using LogGrokX.Data;
using LogGrokX.Data.Index;

namespace LogGrokX.MergedView
{
    internal sealed class MergedComponentIndexer : IComponentIndexer, IDisposable
    {
        private readonly MergedSchema _schema;
        private readonly Dictionary<DocumentViewModel, Action<(int componentNumber, IndexKey key)>> _handlers = new();
        private IReadOnlyList<MergedDocumentItem> _sources = Array.Empty<MergedDocumentItem>();

        public MergedComponentIndexer(MergedSchema schema)
        {
            _schema = schema;
        }

        public event Action<int, string>? ComponentAdded;

        public void UpdateSources(IReadOnlyList<MergedDocumentItem> sources)
        {
            var current = new HashSet<DocumentViewModel>(sources.Select(source => source.Document));

            foreach (var document in _handlers.Keys.ToArray())
            {
                if (current.Contains(document))
                    continue;
                document.Indexer.NewComponentAdded -= _handlers[document];
                _handlers.Remove(document);
            }

            _sources = sources.ToArray();

            foreach (var document in current)
            {
                if (_handlers.ContainsKey(document))
                    continue;

                Action<(int componentNumber, IndexKey key)> handler =
                    added => OnComponentAdded(document, added.componentNumber, added.key);
                _handlers[document] = handler;
                document.Indexer.NewComponentAdded += handler;
            }
        }

        public IEnumerable<string> GetAllComponents(int componentNumber)
        {
            var values = new HashSet<string>(StringComparer.Ordinal);
            foreach (var source in _sources)
            {
                var indexedFieldIndex = GetIndexedFieldIndex(source, componentNumber);
                if (indexedFieldIndex < 0)
                    continue;
                foreach (var value in source.Document.Indexer.GetAllComponents(indexedFieldIndex))
                    values.Add(value);
            }

            return values;
        }

        public int GetIndexCountForComponent(int componentIndex, string componentValue)
        {
            var count = 0;
            foreach (var source in _sources)
            {
                var indexedFieldIndex = GetIndexedFieldIndex(source, componentIndex);
                if (indexedFieldIndex < 0)
                    continue;
                count += source.Document.Indexer.GetIndexCountForComponent(indexedFieldIndex, componentValue);
            }

            return count;
        }

        public void Dispose()
        {
            foreach (var (document, handler) in _handlers)
                document.Indexer.NewComponentAdded -= handler;
            _handlers.Clear();
            _sources = Array.Empty<MergedDocumentItem>();
        }

        private void OnComponentAdded(DocumentViewModel document, int indexedFieldIndex, IndexKey key)
        {
            var fieldName = document.MetaInformation.GetFieldNameByIndexedFieldIndex(indexedFieldIndex);
            var mergedFieldIndex = _schema.IndexOf(fieldName);
            var mergedIndexedFieldIndex = _schema.MetaInformation.GetIndexedFieldIndexByFieldIndex(mergedFieldIndex);
            if (mergedIndexedFieldIndex < 0)
                return;

            ComponentAdded?.Invoke(mergedIndexedFieldIndex, key.GetComponent(indexedFieldIndex).ToString());
        }

        private int GetIndexedFieldIndex(MergedDocumentItem source, int mergedIndexedFieldIndex)
        {
            var indexedFields = _schema.MetaInformation.IndexedFieldNumbers;
            if (mergedIndexedFieldIndex < 0 || mergedIndexedFieldIndex >= indexedFields.Length)
                return -1;

            var mergedFieldIndex = indexedFields[mergedIndexedFieldIndex];
            var sourceFieldIndex = source.GetSourceFieldIndex(mergedFieldIndex);
            if (sourceFieldIndex < 0)
                return -1;
            return source.Document.MetaInformation.GetIndexedFieldIndexByFieldIndex(sourceFieldIndex);
        }
    }
}
