namespace LogGrokX.Data
{
    public readonly struct MergeSource
    {
        public MergeSource(int lineCount, TimeIndex timeIndex)
        {
            LineCount = lineCount;
            TimeIndex = timeIndex.CapturePrefix(lineCount);
            HasTime = TimeIndex.HasTime && TimeIndex.IsMonotonic && TimeIndex.Count >= lineCount;
        }

        public int LineCount { get; }

        public TimeIndexPrefix TimeIndex { get; }

        public bool HasTime { get; }
    }
}
