using System.Text.RegularExpressions;
using BenchmarkDotNet.Attributes;
using LogGrokX.Data;

namespace LogGrokX.Benchmarks;

[MemoryDiagnoser]
public class SearchHighlightBenchmark
{
    private static readonly Regex WildcardRegex = new(@"error.*timeout", RegexOptions.Compiled);
    private static readonly Regex AlternationRegex = new(@"error|timeout", RegexOptions.Compiled);

    [Benchmark]
    public SearchHighlightPattern ConstructWildcard() => new(WildcardRegex);

    [Benchmark]
    public SearchHighlightPattern ConstructAlternation() => new(AlternationRegex);

    [Benchmark]
    public SearchHighlightPattern CachedWildcard() => SearchHighlightPattern.For(WildcardRegex);

    [Benchmark]
    public SearchHighlightPattern CachedAlternation() => SearchHighlightPattern.For(AlternationRegex);
}
