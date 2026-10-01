using System;
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
            IndexViewModel = new LinePartViewModel(HashCode.Combine(-1, index), Index.ToString());
            _markedLines = markedLines;
            _onMarkedLinesChanged = () => InvokePropertyChanged(nameof(IsMarked));
            _markedLines.SubscribeWeak(_onMarkedLinesChanged);
        }
        
        public int Index { get; }

        public LinePartViewModel IndexViewModel { get; }

        public virtual string GetDisplayText(TextViewSharedFoldingState? foldingState) =>
            ToString() ?? string.Empty;

        public virtual string GetFieldText(int fieldIndex) => string.Empty;
        
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