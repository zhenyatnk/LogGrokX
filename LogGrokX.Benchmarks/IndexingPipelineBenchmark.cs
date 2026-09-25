using System;
using System.IO;
using System.Text;
using System.Threading;
using BenchmarkDotNet.Attributes;
using LogGrokX.Data;
using LogGrokX.Data.Index;

namespace LogGrokX.Benchmarks
{
    /// <summary>
    /// End-to-end loading: split into lines, decode, parse and index.
    /// This is the benchmark that shows the effect of parallel parsing, so it is
    /// also the one that catches regressions in loading throughput and allocations.
    /// </summary>
    [MemoryDiagnoser]
    public class IndexingPipelineBenchmark
    {
        private const int LineCount = 200_000;

        private const string LineRegex =
            @"^(?<Time>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}) \[(?<Level>[A-Z]+)\] \[(?<Thread>\d+)\] (?<Message>.*)$";

        private string _path = null!;
        private LogMetaInformation _meta = null!;

        [GlobalSetup]
        public void Setup()
        {
            var levels = new[] { "INFO", "WARN", "ERROR", "DEBUG" };
            var builder = new StringBuilder(LineCount * 100);
            for (var i = 0; i < LineCount; i++)
            {
                builder.Append("2024-01-15 08:32:")
                    .Append((i % 60).ToString("00"))
                    .Append('.')
                    .Append((i % 1000).ToString("000"))
                    .Append(" [")
                    .Append(levels[i % levels.Length])
                    .Append("] [")
                    .Append(i % 16)
                    .Append("] Request completed in ")
                    .Append(i % 500)
                    .Append(" ms for /api/orders/")
                    .Append(i)
                    .Append("\r\n");
            }

            _path = Path.Combine(Path.GetTempPath(), $"loggrokx-bench-{Guid.NewGuid():N}.log");
            File.WriteAllText(_path, builder.ToString(), new UTF8Encoding(false));

            _meta = new LogMetaInformation(new LogFormat
            {
                Regex = LineRegex,
                IndexedFields = new[] { "Level", "Thread" },
                TimeField = "Time",
                TimeFormat = "yyyy-MM-dd HH:mm:ss.fff"
            });
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            if (File.Exists(_path))
                File.Delete(_path);
        }

        [Benchmark]
        public int LoadAndIndex()
        {
            var logFile = new LogFile(_path, 0);
            var lineIndex = new LineIndex();
            var indexer = new Indexer();
            var stringPool = new StringPool();
            var timeIndex = new TimeIndex();
            var parsedBufferConsumer = new ParsedBufferConsumer(lineIndex, indexer, _meta, stringPool);
            var parser = new RegexBasedLineParser(_meta, onlyIndexed: true);
            var lineProcessor = new LineProcessor(logFile, _meta, parser, parsedBufferConsumer, stringPool, timeIndex);
            var loaderImpl = new LoaderImpl(1024 * 1024, lineProcessor);

            var encoding = logFile.Encoding;
            using (var stream = logFile.OpenForSequentialRead())
            {
                loaderImpl.Load(stream, encoding.GetBytes("\r"), encoding.GetBytes("\n"),
                    CancellationToken.None);
            }

            while (!lineIndex.IsFinished)
                Thread.Sleep(1);

            return lineIndex.Count;
        }
    }
}
