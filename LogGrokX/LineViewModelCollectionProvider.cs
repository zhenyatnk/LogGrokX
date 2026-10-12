using System;
using System.Collections.Generic;
using LogGrokX.Controls;
using LogGrokX.Data;
using LogGrokX.Data.Index;
using LogGrokX.Data.Virtualization;

namespace LogGrokX
{
    public class LineViewModelCollectionProvider
    {
        private readonly IItemProvider<(int index, string str)> _lineProvider;
        private readonly ILineParser _lineParser;
        private readonly IReadOnlyList<ItemViewModel> _headerCollection;
        private readonly Selection _markedLines;
        private readonly TransformationPerformer _transformationPerformer;
        private readonly TimeIndex _timeIndex;
        private readonly string[] _fieldNames;

        public LineViewModelCollectionProvider(
            IItemProvider<(int index, string str)> lineProvider,
            ILineParser lineParser,             
            LogHeaderCollection headerCollection,
            Selection markedLines, 
            TransformationPerformer transformationPerformer,
            TimeIndex timeIndex,
            LogMetaInformation metaInformation)
        {
            _fieldNames = metaInformation.FieldNames;
            _lineProvider = lineProvider;
            _lineParser = lineParser;
            _headerCollection = headerCollection;

            _markedLines = markedLines;
            _transformationPerformer = transformationPerformer;
            _timeIndex = timeIndex;
        }

        public (IReadOnlyList<ItemViewModel> headerCollection, IReadOnlyList<ItemViewModel> linesCollectoin, 
            Func<int, int> getIndexByValue) 
            GetLogLinesCollection(
                Indexer indexer, // index: Components -> log line numbers
                IReadOnlyDictionary<int, IEnumerable<string>> exclusions,
                (long From, long To)? timeRange = null,
                (int From, int To)? lineRange = null)
        {
            var (itemProvider, getIndexByValue) = GetLineProvider(indexer, exclusions, timeRange, lineRange);
            var lineCollection = CreateLinesCollection(itemProvider);
            return (_headerCollection,  lineCollection, getIndexByValue);
        }

        public (IReadOnlyList<ItemViewModel> headerCollection, IReadOnlyList<ItemViewModel> linesCollection, Func<int, int> getIndexByValue) 
            GetLogLinesCollection(IItemProvider<(int, string)> itemProvider)
        {
            return (_headerCollection, 
                    CreateLinesCollection(itemProvider),
                    x => x);
        }

        private VirtualList<(int index, string str), ItemViewModel> CreateLinesCollection(IItemProvider<(int, string)> itemProvider)
        {
            return new VirtualList<(int index, string str), ItemViewModel>(itemProvider,
                indexAndString => 
                    new LineViewModel(indexAndString.index, indexAndString.str, _lineParser,
                        _markedLines, _transformationPerformer,
                        ValueMapper.Current.ForFields(_fieldNames)));
        }

        private (IItemProvider<(int index, string str)> itemProvider, Func<int, int> GetIndexByValue) GetLineProvider(
            Indexer indexer,
            IReadOnlyDictionary<int, IEnumerable<string>> exclusions,
            (long From, long To)? timeRange,
            (int From, int To)? lineRange)
        {
            //if (exclusions.Count == 0) return (_lineProvider, x=> x);
            IIndexedLinesProvider lineNumbersProvider = indexer.GetIndexedLinesProvider(exclusions);

            if (timeRange is { } range &&
                _timeIndex.FindLineRange(range.From, range.To) is { } timeLineRange)
            {
                lineNumbersProvider = new LineRangeIndexedLinesProvider(
                    lineNumbersProvider, timeLineRange.StartLine, timeLineRange.EndLine);
            }
            else if (lineRange is { } fallbackLineRange)
            {
                lineNumbersProvider = new LineRangeIndexedLinesProvider(
                    lineNumbersProvider, fallbackLineRange.From, fallbackLineRange.To);
            }

            return (new ItemProviderMapper<(int index, string str)>(lineNumbersProvider, _lineProvider),
                value => lineNumbersProvider.GetIndexByValue(value));
        }
    }
}