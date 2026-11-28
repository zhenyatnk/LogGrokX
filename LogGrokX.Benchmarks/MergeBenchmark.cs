using System;
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using LogGrokX.Data;

namespace LogGrokX.Benchmarks
{
    [MemoryDiagnoser]
    public class MergeBenchmark
    {
        private const long BaseTicks = 360_000_000_000L;
        private const long TicksPerLine = 10_000L;

        [Params(2, 4, 8)]
        public int SourceCount { get; set; }

        [Params(200_000)]
        public int LinesPerSource { get; set; }

        private MergeSource[] _sources = null!;
        private List<MergedLineRef> _merged = null!;
        private TimeIndex _mergedTimeIndex = null!;
        private long _fromTicks;
        private long _toTicks;

        [GlobalSetup]
        public void Setup()
        {
            _sources = new MergeSource[SourceCount];
            for (var source = 0; source < SourceCount; source++)
            {
                var timeIndex = new TimeIndex();
                for (var line = 0; line < LinesPerSource; line++)
                    timeIndex.Add(BaseTicks + ((long)line * SourceCount + source) * TicksPerLine);

                _sources[source] = new MergeSource(LinesPerSource, timeIndex);
            }

            _merged = MergedLineOrder.Build(_sources);
            _mergedTimeIndex = MergedLineOrder.BuildTimeIndex(_merged);

            var span = _mergedTimeIndex.MaxTicks - _mergedTimeIndex.MinTicks;
            _fromTicks = _mergedTimeIndex.MinTicks + span / 4;
            _toTicks = _mergedTimeIndex.MinTicks + span * 3 / 4;
        }

        [Benchmark]
        public int Build()
        {
            MergedLineOrder.Build(_sources, _merged);
            return _merged.Count;
        }

        [Benchmark]
        public int BuildTimeIndex()
        {
            MergedLineOrder.BuildTimeIndex(_merged, _mergedTimeIndex);
            return _mergedTimeIndex.Count;
        }

        [Benchmark]
        public int BuildAndToArray()
        {
            var lines = MergedLineOrder.Build(_sources).ToArray();
            return lines.Length;
        }

        [Benchmark]
        public int BuildArray()
        {
            var lines = MergedLineOrder.BuildArray(_sources);
            return lines.Length;
        }

        [Benchmark]
        public int FindLineRange()
        {
            var range = _mergedTimeIndex.FindLineRange(_fromTicks, _toTicks);
            return range?.EndLine ?? 0;
        }
    }
}
