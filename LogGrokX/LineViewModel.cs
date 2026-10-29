using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using LogGrokX.Controls;
using LogGrokX.Controls.TextRender;
using LogGrokX.Data;

namespace LogGrokX
{
public class LineViewModel : BaseLogLineViewModel, IThreadGroupedItem
{
        private readonly string _sourceString;
        private readonly ILineParser _parser;

        private readonly ParseResult _parseResult;
        private readonly string _transformResult;
        private readonly LinePartViewModel?[] _parts;
        private readonly FieldValueMapper[]? _valueMappers;
        private readonly Func<FieldValueMapper[]>? _valueMappersResolver;
        private readonly DecodedPartsState? _decodedParts;
        private FieldValueMapper[]? _resolvedValueMappers;
        private int _resolvedValueMappersVersion = -1;
        private LinePartViewModel? _emptyPart;

        public LineViewModel(int index, string sourceString, ILineParser parser, Selection markedLines,
            TransformationPerformer transformationPerformer, DisplaySettingsMonitor displaySettings, FieldValueMapper[]? valueMappers = null,
            Func<FieldValueMapper[]>? valueMappersResolver = null, DecodedPartsState? decodedParts = null)
            : base(index, markedLines, displaySettings)
        {
            _decodedParts = decodedParts;
            _parser = parser;
            _sourceString = sourceString;
            _valueMappers = valueMappers;
            _valueMappersResolver = valueMappersResolver;
            _transformResult = transformationPerformer.Transform(sourceString); 
            _parseResult = parser.Parse(
                _transformResult);
            _parts = new LinePartViewModel?[_parseResult.ComponentCount];
        }

        public LinePartViewModel this[int index] => GetValue(index);

        public ReadOnlySpan<char> GetComponentSpan(int index)
        {
            if (index < 0 || index >= _parseResult.ComponentCount)
                return ReadOnlySpan<char>.Empty;

            var lineMeta = _parseResult.Get().ParsedLineComponents;
            var start = lineMeta.ComponentStart(index);
            var length = lineMeta.ComponentLength(index);

            if (start < 0 || length < 0 || start + length > _transformResult.Length)
                return ReadOnlySpan<char>.Empty;

            return _transformResult.AsSpan(start, length);
        }

        public override string GetFieldText(int fieldIndex) => GetComponentSpan(fieldIndex).ToString();

        protected override IEnumerable<LinePartViewModel> GetDecodableParts() =>
            Enumerable.Range(0, _parseResult.ComponentCount).Select(GetValue);

        public bool HasSameThread(IThreadGroupedItem? other, int threadFieldIndex)
        {
            if (other is not LineViewModel otherLine)
                return false;

            return GetComponentSpan(threadFieldIndex)
                .SequenceEqual(otherLine.GetComponentSpan(threadFieldIndex));
        }

        private LinePartViewModel GetValue(int index)
        {
            if (index < 0 || index >= _parts.Length)
                return _emptyPart ??= new LinePartViewModel(HashCode.Combine(Index, -1), string.Empty, DisplaySettings,
                    detectBase64: false);

            if (_parts[index] is { } cached)
                return cached;

            var uniqueId = HashCode.Combine(base.Index, index);
            var text = GetComponentSpan(index).ToString();

            var decodedState = _decodedParts?.For(base.Index, index);
            var part = _valueMappersResolver != null
                ? new LinePartViewModel(uniqueId, text, DisplaySettings,
                    valueMapperResolver: () => GetValueMapper(index) ?? FieldValueMapper.None,
                    decodedState: decodedState)
                : new LinePartViewModel(uniqueId, text, DisplaySettings, valueMapper: GetValueMapper(index),
                    decodedState: decodedState);
            _parts[index] = part;

            return part;
        }

        private FieldValueMapper? GetValueMapper(int index)
        {
            var mappers = _valueMappers;
            if (_valueMappersResolver != null)
            {
                if (_resolvedValueMappers == null ||
                    _resolvedValueMappersVersion != ValueMapper.CurrentVersion)
                {
                    _resolvedValueMappers = _valueMappersResolver();
                    _resolvedValueMappersVersion = ValueMapper.CurrentVersion;
                }

                mappers = _resolvedValueMappers;
            }

            return mappers != null && index >= 0 && index < mappers.Length ? mappers[index] : null;
        }

        public override bool Equals(object? o)
        {
            if (o is LineViewModel other)
                return other.Index == Index && other._sourceString == _sourceString
                    && ReferenceEquals(other._parser, _parser);
            return false;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Index, _sourceString, _parser);
        }

        public override string ToString()
        {
            return _transformResult.TrimEnd();
        }

        public override string GetDisplayText(TextViewSharedFoldingState? foldingState)
        {
            var lineMeta = _parseResult.Get().ParsedLineComponents;
            var componentCount = _parseResult.ComponentCount;
            var builder = new StringBuilder();
            var offset = 0;

            for (var i = 0; i < componentCount; i++)
            {
                var start = lineMeta.ComponentStart(i);
                var length = lineMeta.ComponentLength(i);

                if (start < offset || start + length > _transformResult.Length)
                    continue;

                builder.Append(_transformResult, offset, start - offset);
                var componentText = _transformResult.Substring(start, length);
                builder.Append(GetComponentDisplayText(i, componentText, foldingState));
                offset = start + length;
            }

            builder.Append(_transformResult, offset, _transformResult.Length - offset);
            return builder.ToString().TrimEnd();
        }

        private string GetComponentDisplayText(int componentIndex, string componentText,
            TextViewSharedFoldingState? foldingState)
        {
            var textModel = componentIndex >= 0 && componentIndex < _parts.Length &&
                            _parts[componentIndex] is { HasAlternateText: true } decodedPart
                ? decodedPart.TextModel
                : new TextModel(HashCode.Combine(Index, componentIndex),
                    ValueMappingText.Apply(componentText, GetValueMapper(componentIndex)));

            if (foldingState == null || textModel.CollapsibleRanges == null)
                return textModel.GetDisplayedText(null);

            var collapsedLines = foldingState[textModel.UniqueId]
                                 ?? foldingState.GetDefaultSettings(textModel.CollapsibleRanges, textModel.Count);

            return textModel.GetDisplayedText(collapsedLines);
        }
    }
}
