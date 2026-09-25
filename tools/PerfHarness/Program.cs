// Manual load/search measurement harness for LogGrokX.Data.
// Usage:  dotnet run --project tools/PerfHarness -c Release [-- <logFile> <lineCount> <runs>]
// The parse/index modes are selected with LOGGROKX_PARALLEL_PARSING / LOGGROKX_PARALLEL_INDEXING.
using System;
using System.Diagnostics;
using System.Runtime;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using LogGrokX.Data;
using LogGrokX.Data.Index;

var path = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "loggrokx-perf.log");
var lineCount = args.Length > 1 ? int.Parse(args[1]) : 2_000_000;
var runs = args.Length > 2 ? int.Parse(args[2]) : 4;

if (!File.Exists(path))
{
    Console.WriteLine($"generating {lineCount} lines -> {path}");
    var levels = new[] { "INFO", "WARN", "ERROR", "DEBUG" };
    using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
    for (var i = 0; i < lineCount; i++)
        writer.Write(
            $"2024-01-15 08:32:{i % 60:00}.{i % 1000:000} [{levels[i % 4]}] [{i % 16}] Request completed in {i % 500} ms for /api/orders/{i}\r\n");
}

var meta = new LogMetaInformation(new LogFormat
{
    Regex =
        @"^(?<Time>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}) \[(?<Level>[A-Z]+)\] \[(?<Thread>\d+)\] (?<Message>.*)$",
    IndexedFields = new[] { "Level", "Thread" },
    TimeField = "Time",
    TimeFormat = "yyyy-MM-dd HH:mm:ss.fff"
});

Console.WriteLine($"ProcessorCount={Environment.ProcessorCount} " +
                  $"parse={Environment.GetEnvironmentVariable("LOGGROKX_PARALLEL_PARSING") ?? "auto"} " +
                  $"index={Environment.GetEnvironmentVariable("LOGGROKX_PARALLEL_INDEXING") ?? "auto"} " +
                  $"file={new FileInfo(path).Length / 1024 / 1024} MB");

LogModelFacade Load(out long elapsedMs)
{
    var logFile = new LogFile(path, 0);
    var lineIndex = new LineIndex();
    var indexer = new Indexer();
    var pool = new StringPool();
    var timeIndex = new TimeIndex();
    var consumer = new ParsedBufferConsumer(lineIndex, indexer, meta, pool);
    var parser = new RegexBasedLineParser(meta, true);
    var lineProcessor = new LineProcessor(logFile, meta, parser, consumer, pool, timeIndex);
    var loader = new LoaderImpl(1024 * 1024, lineProcessor);

    var stopwatch = Stopwatch.StartNew();
    using (var stream = logFile.OpenForSequentialRead())
        loader.Load(stream, logFile.Encoding.GetBytes("\r"), logFile.Encoding.GetBytes("\n"),
            CancellationToken.None);
    while (!lineIndex.IsFinished)
        Thread.Sleep(1);
    stopwatch.Stop();
    elapsedMs = stopwatch.ElapsedMilliseconds;

    return new LogModelFacade(logFile, lineIndex, new LineProvider(lineIndex, logFile),
        new RegexBasedLineParser(meta), indexer, meta);
}

LogModelFacade? model = null;
var bestLoad = long.MaxValue;
for (var run = 0; run < runs; run++)
{
    model = Load(out var elapsed);
    Console.WriteLine($"  load run {run}: {elapsed} ms" + (run == 0 ? " (warm-up, ignored)" : string.Empty));
    if (run > 0)
        bestLoad = Math.Min(bestLoad, elapsed);
}

var peakWorkingSetMb = Process.GetCurrentProcess().PeakWorkingSet64 / 1024 / 1024;
var retainedMb = GC.GetTotalMemory(forceFullCollection: true) / 1024 / 1024;
var gcInfo = GC.GetGCMemoryInfo(GCKind.Any);
Console.WriteLine($"RESULT load: best={bestLoad} ms peakWorkingSet={peakWorkingSetMb} MB " +
                  $"retainedAfterFullGC={retainedMb} MB committedHeap={gcInfo.TotalCommittedBytes / 1024 / 1024} MB " +
                  $"gcHeaps={GCSettings.IsServerGC switch { true => "server", false => "workstation" }}");

foreach (var (name, pattern) in new[]
         {
             ("literal-rare", $"orders/{lineCount - 1}"),
             ("literal-none", "zzz-not-present"),
             ("regex-hits", @"in 4\d\d ms")
         })
{
    var best = long.MaxValue;
    var hits = 0;
    for (var run = 0; run < runs; run++)
    {
        var stopwatch = Stopwatch.StartNew();
        var (progress, _, lines) =
            LogGrokX.Data.Search.Search.CreateSearchIndex(model!, new Regex(pattern), CancellationToken.None);
        progress.Completion.Wait();
        stopwatch.Stop();
        hits = lines.Count;
        if (run > 0)
            best = Math.Min(best, stopwatch.ElapsedMilliseconds);
    }

    Console.WriteLine($"RESULT search[{name}]: hits={hits} best={best} ms");
}
