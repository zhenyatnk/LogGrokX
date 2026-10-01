using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using LogGrokX.Controls;
using LogGrokX.Controls.TextRender;
using LogGrokX.MarkedLines;

namespace LogGrokX
{
    public abstract class BaseLogLineViewModel : ItemViewModel, ILineMark
    {
        private readonly Selection _markedLines;
        private readonly Action _onMarkedLinesChanged;

        protected BaseLogLineViewModel(int index, Selection markedLines)
        {
            Index = index;
            IndexViewModel = new LinePartViewModel(HashCode.Combine(-1, index), Index.ToString(), detectBase64: false);
            _markedLines = markedLines;
            _onMarkedLinesChanged = () => InvokePropertyChanged(nameof(IsMarked));
            _markedLines.SubscribeWeak(_onMarkedLinesChanged);
        }
        
        public int Index { get; }

        public LinePartViewModel IndexViewModel { get; }

        public virtual string GetDisplayText(TextViewSharedFoldingState? foldingState) =>
            ToString() ?? string.Empty;

        public virtual string GetFieldText(int fieldIndex) => string.Empty;

        protected virtual IEnumerable<LinePartViewModel> GetDecodableParts() => Enumerable.Empty<LinePartViewModel>();

        private List<LinePartViewModel> DecodableParts
        {
            get
            {
                if (_decodableParts != null)
                    return _decodableParts;

                _decodableParts = GetDecodableParts().ToList();
                foreach (var part in _decodableParts)
                    part.PropertyChanged += OnPartPropertyChanged;
                return _decodableParts;
            }
        }

        private List<LinePartViewModel>? _decodableParts;

        public bool IsPem => DecodableParts.Any(p => p.IsPem);

        public bool IsBase64 => DecodableParts.Any(p => p.IsBase64);

        public bool IsHex => DecodableParts.Any(p => p.IsHex);

        public bool IsDecodable => IsPem || IsBase64 || IsHex;

        public bool IsDecoded
        {
            get => DecodableParts.Any(p => p.IsDecoded);
            set
            {
                foreach (var part in DecodableParts.Where(p => p.IsPem || p.IsBase64 || p.IsHex))
                    part.IsDecoded = value;
            }
        }

        public bool IsPemDecoded
        {
            get => DecodableParts.Any(p => p.IsPem && p.IsPemDecoded);
            set
            {
                foreach (var part in DecodableParts.Where(p => p.IsPem))
                    part.IsPemDecoded = value;
            }
        }

        public bool IsBase64Decoded
        {
            get => DecodableParts.Any(p => p.IsBase64 && p.IsBase64Decoded);
            set
            {
                foreach (var part in DecodableParts.Where(p => p.IsBase64))
                    part.IsBase64Decoded = value;
            }
        }

        public bool IsHexDecoded
        {
            get => DecodableParts.Any(p => p.IsHex && p.IsHexDecoded);
            set
            {
                foreach (var part in DecodableParts.Where(p => p.IsHex))
                    part.IsHexDecoded = value;
            }
        }

        private void OnPartPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(LinePartViewModel.IsPemDecoded)
                or nameof(LinePartViewModel.IsBase64Decoded)
                or nameof(LinePartViewModel.IsHexDecoded)
                or nameof(LinePartViewModel.IsDecoded))
                InvokePropertyChanged(e.PropertyName);
        }
        
        public bool IsMarked
        {
            get => _markedLines.Contains(Index);
            set
            {
                if (value)
                    _markedLines.Add(Index);
                else 
                    _markedLines.Remove(Index);
            }
        }
    }
}