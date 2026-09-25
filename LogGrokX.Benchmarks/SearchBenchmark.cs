using System;
using System.Text;
using System.Text.RegularExpressions;
using BenchmarkDotNet.Attributes;
using LogGrokX.Data.Search;

namespace LogGrokX.Benchmarks
{
    /// <summary>
    /// Compares the per-line cost of the search hot loop with and without the
    /// byte-level prefilter: "decode every line and run the regex" versus
    /// "skip lines that cannot contain the pattern".
    /// </summary>
    [MemoryDiagnoser]
    public class SearchBenchmark
    {
        private const int LineCount = 50_000;

        private byte[][] _lines = null!;
        private Regex _regex = null!;
        private SearchPrefilter _prefilter = null!;
        private char[] _charBuffer = null!;

        [GlobalSetup]
        public void Setup()
        {
            _lines = new byte[LineCount][];
            for (var i = 0; i < LineCount; i++)
            {
                var text = i % 1000 == 0
                    ? $"2024-01-15 08:32:11.482 [ERROR] unexpected failure in transaction {i}"
                    : $"2024-01-15 08:32:11.482 [INFO] request completed in {i % 500} ms";
                _lines[i] = Encoding.UTF8.GetBytes(text);
            }

            _regex = new Regex("unexpected failure", RegexOptions.Compiled);
            _prefilter = SearchPrefilter.TryCreate(_regex, Encoding.UTF8)
                         ?? throw new InvalidOperationException("prefilter is expected to be available");
            _charBuffer = new char[4096];
        }

        [Benchmark(Baseline = true)]
        public int DecodeEveryLine()
        {
            var matches = 0;
            foreach (var line in _lines)
            {
                var length = Encoding.UTF8.GetChars(line, _charBuffer);
                if (_regex.IsMatch(_charBuffer.AsSpan(0, length)))
                    matches++;
            }

            return matches;
        }

        [Benchmark]
        public int PrefilterThenDecode()
        {
            var matches = 0;
            foreach (var line in _lines)
            {
                if (!_prefilter.MayContainMatch(line))
                    continue;

                var length = Encoding.UTF8.GetChars(line, _charBuffer);
                if (_regex.IsMatch(_charBuffer.AsSpan(0, length)))
                    matches++;
            }

            return matches;
        }
    }
}
