using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace LogGrokX.MergedView
{
    public sealed class MergedDocumentItem : ViewModelBase
    {
        private bool _isSelected = true;
        private int[] _mergedToSourceField = Array.Empty<int>();

        public MergedDocumentItem(DocumentViewModel document, int colorIndex)
        {
            Document = document;
            ColorIndex = colorIndex;
            RowBackground = MergedViewPalette.GetRowBrush(colorIndex);
            TimelineBrush = MergedViewPalette.GetTimelineBrush(colorIndex);
        }

        public DocumentViewModel Document { get; }

        public int ColorIndex { get; }

        public string Title => Document.Title;

        public Brush RowBackground { get; }

        public Brush TimelineBrush { get; }

        internal void UpdateFieldMap(IReadOnlyList<string> mergedFields)
        {
            var map = new int[mergedFields.Count];
            var fieldNames = Document.MetaInformation.FieldNames;
            for (var i = 0; i < mergedFields.Count; i++)
                map[i] = MergedSchema.FindSourceFieldIndex(fieldNames, mergedFields[i]);
            _mergedToSourceField = map;
        }

        internal int GetSourceFieldIndex(int mergedFieldIndex)
        {
            if (mergedFieldIndex < 0 || mergedFieldIndex >= _mergedToSourceField.Length)
                return -1;
            return _mergedToSourceField[mergedFieldIndex];
        }

        internal int MergedFieldCount => _mergedToSourceField.Length;

        internal bool IsLineInThread(int lineNumber, int mergedThreadFieldIndex, string thread)
        {
            var sourceFieldIndex = GetSourceFieldIndex(mergedThreadFieldIndex);
            if (sourceFieldIndex < 0)
                return false;

            var indexedFieldIndex = Document.MetaInformation.GetIndexedFieldIndexByFieldIndex(sourceFieldIndex);
            if (indexedFieldIndex >= 0)
                return Document.Indexer.IsLineComponentEqual(lineNumber, indexedFieldIndex, thread);

            var text = Document.GetTransformedLine(lineNumber);
            try
            {
                var parseResult = Document.LineParser.Parse(text);
                if (sourceFieldIndex >= parseResult.ComponentCount)
                    return false;

                var lineMeta = parseResult.Get().ParsedLineComponents;
                var start = lineMeta.ComponentStart(sourceFieldIndex);
                var length = lineMeta.ComponentLength(sourceFieldIndex);
                return start >= 0 && length >= 0 && start + length <= text.Length
                       && text.AsSpan(start, length).SequenceEqual(thread);
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                    return;

                _isSelected = value;
                InvokePropertyChanged();
                SelectionChanged?.Invoke(this);
            }
        }

        public event Action<MergedDocumentItem>? SelectionChanged;
    }
}