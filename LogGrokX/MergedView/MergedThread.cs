namespace LogGrokX.MergedView;

public readonly record struct MergedThread(MergedDocumentItem Source, string Thread)
{
    public bool Equals(MergedThread other) => ReferenceEquals(Source?.Document, other.Source?.Document)
        && Source?.ColorIndex == other.Source?.ColorIndex && Thread == other.Thread;

    public override int GetHashCode() => System.HashCode.Combine(Source?.Document, Source?.ColorIndex, Thread);
}
