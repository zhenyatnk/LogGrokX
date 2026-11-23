using System;
using System.Collections.Generic;

namespace LogGrokX.MergedView
{
    public sealed class MergedDocumentItem : ViewModelBase
    {
        private bool _isSelected = true;
        private int[] _mergedToSourceField = Array.Empty<int>();
        private string[]? _fieldNames;

        public MergedDocumentItem(DocumentViewModel document, int colorIndex)
        {
            Document = document;
            ColorIndex = colorIndex;
            RowColor = MergedViewPalette.GetRowColor(colorIndex);
            TimelineColor = MergedViewPalette.GetTimelineColor(colorIndex);
        }

        public DocumentViewModel Document { get; }

        public int ColorIndex { get; }

        internal bool HasSameSource(MergedDocumentItem other) =>
            ReferenceEquals(Document, other.Document) && ColorIndex == other.ColorIndex;

        public string Title => Document.Title;

        public string RowColor { get; }

        public string TimelineColor { get; }

        internal void UpdateFieldMap(IReadOnlyList<string> mergedFields)
        {
            var map = new int[mergedFields.Count];
            var fieldNames = FieldNames;
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

        internal string[] FieldNames => _fieldNames ??= Document.MetaInformation.FieldNames;

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
