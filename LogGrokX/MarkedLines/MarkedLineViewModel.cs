using System;
using System.Collections.Generic;
using LogGrokX.Colors;
using LogGrokX.Controls.TextRender;

namespace LogGrokX.MarkedLines
{
    public class MarkedLineViewModel : BaseLogLineViewModel
    {
        public DocumentViewModel Document { get; }
        public LinePartViewModel Text { get; }
        public ColorSettings ColorSettings { get; }

        public MarkedLineViewModel(DocumentViewModel document, int lineNumber, string text)
            : base(lineNumber, document.MarkedLines)        
        {
            Document = document;

            var uniqueId = HashCode.Combine(lineNumber, document.GetFoldingComponentIndex(text));
            Text = new LinePartViewModel(uniqueId, text);
            ColorSettings = document.ColorSettings;
        }

        protected override IEnumerable<LinePartViewModel> GetDecodableParts() => new[] { Text };

        public override string ToString() => Text.OriginalText ?? string.Empty;

        public override string GetDisplayText(TextViewSharedFoldingState? foldingState)
        {
            var state = Document.FoldingState;
            var textModel = Text.TextModel;
            if (textModel.CollapsibleRanges == null)
                return textModel.GetDisplayedText(null);

            var collapsedLines = state[textModel.UniqueId]
                                 ?? state.GetDefaultSettings(textModel.CollapsibleRanges, textModel.Count);

            return textModel.GetDisplayedText(collapsedLines);
        }
    }
}