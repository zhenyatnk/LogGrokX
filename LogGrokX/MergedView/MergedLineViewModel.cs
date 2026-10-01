using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using LogGrokX.Controls.TextRender;
using LogGrokX.Data;

namespace LogGrokX.MergedView
{
    public sealed class MergedLineViewModel : BaseLogLineViewModel, IThreadGroupedItem
    {
        private readonly string _transformedText;
        private ParseResult? _parseResult;
        private LinePartViewModel?[]? _parts;

        public MergedLineViewModel(MergedDocumentItem source, int lineNumber, long ticks, int mergedIndex = -1)
            : base(lineNumber, source.Document.MarkedLines)
        {
            Source = source;
            MergedIndex = mergedIndex;

            _transformedText = source.Document.GetTransformedLine(lineNumber);
            var componentIndex = source.Document.GetFoldingComponentIndex(_transformedText);
            var uniqueId = HashCode.Combine(
                lineNumber,
                source.Document.DocumentId.GetHashCode(StringComparison.Ordinal),
                source.ColorIndex,
                componentIndex);

            Text = new LinePartViewModel(uniqueId, _transformedText);
            TimeText = ticks >= 0 ? TimestampParser.Format(ticks) : string.Empty;
        }

        public MergedDocumentItem Source { get; }

        public int MergedIndex { get; }

        public string SourceTitle => Source.Title;

        public Brush BackgroundBrush => Source.RowBackground;

        public LinePartViewModel Text { get; }

        public string TimeText { get; }

        public LinePartViewModel this[int mergedFieldIndex] => GetFieldPart(mergedFieldIndex);

        protected override IEnumerable<LinePartViewModel> GetDecodableParts() =>
            Enumerable.Range(0, Source.MergedFieldCount).Select(GetFieldPart);

        public ReadOnlySpan<char> GetComponentSpan(int mergedFieldIndex)
        {
            var sourceFieldIndex = Source.GetSourceFieldIndex(mergedFieldIndex);
            if (sourceFieldIndex < 0)
                return ReadOnlySpan<char>.Empty;

            var parseResult = Parse();
            if (sourceFieldIndex >= parseResult.ComponentCount)
                return ReadOnlySpan<char>.Empty;

            var lineMeta = parseResult.Get().ParsedLineComponents;
            var start = lineMeta.ComponentStart(sourceFieldIndex);
            var length = lineMeta.ComponentLength(sourceFieldIndex);

            if (start < 0 || length < 0 || start + length > _transformedText.Length)
                return ReadOnlySpan<char>.Empty;

            return _transformedText.AsSpan(start, length);
        }

        public override string GetFieldText(int fieldIndex) => GetComponentSpan(fieldIndex).ToString();

        public bool HasSameThread(IThreadGroupedItem? other, int threadFieldIndex)
        {
            if (other is not MergedLineViewModel otherLine || !ReferenceEquals(otherLine.Source, Source))
                return false;

            return GetComponentSpan(threadFieldIndex)
                .SequenceEqual(otherLine.GetComponentSpan(threadFieldIndex));
        }

        public override string ToString() => Text.OriginalText ?? string.Empty;

        public override string GetDisplayText(TextViewSharedFoldingState? foldingState)
        {
            var state = Source.Document.FoldingState;
            var textModel = Text.TextModel;
            if (textModel.CollapsibleRanges == null)
                return textModel.GetDisplayedText(null);

            var collapsedLines = state[textModel.UniqueId]
                                 ?? state.GetDefaultSettings(textModel.CollapsibleRanges, textModel.Count);

            return textModel.GetDisplayedText(collapsedLines);
        }

        private ParseResult Parse() => _parseResult ??= Source.Document.LineParser.Parse(_transformedText);

        private LinePartViewModel GetFieldPart(int mergedFieldIndex)
        {
            _parts ??= new LinePartViewModel?[Source.MergedFieldCount];
            if (mergedFieldIndex >= 0 && mergedFieldIndex < _parts.Length && _parts[mergedFieldIndex] is { } cached)
                return cached;

            var sourceFieldIndex = Source.GetSourceFieldIndex(mergedFieldIndex);
            string text;
            if (sourceFieldIndex < 0)
            {
                text = string.Empty;
            }
            else
            {
                var parseResult = Parse();
                var lineMeta = parseResult.Get().ParsedLineComponents;
                var start = lineMeta.ComponentStart(sourceFieldIndex);
                var length = lineMeta.ComponentLength(sourceFieldIndex);
                text = start < 0 || length <= 0 || start + length > _transformedText.Length
                    ? string.Empty
                    : _transformedText.Substring(start, length);
            }

            var uniqueId = sourceFieldIndex >= 0
                ? HashCode.Combine(Index, sourceFieldIndex)
                : HashCode.Combine(Index, -1, mergedFieldIndex);
            var part = new LinePartViewModel(uniqueId, text);
            if (mergedFieldIndex >= 0 && mergedFieldIndex < _parts.Length)
                _parts[mergedFieldIndex] = part;

            return part;
        }
    }
}