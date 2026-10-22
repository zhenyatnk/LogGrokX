using System;
using System.Collections.Generic;
using LogGrokX.Controls;
using LogGrokX.Data;

namespace LogGrokX.MergedView;

internal sealed record MergedDataSnapshot(MergedLineRef[] Lines, MergedDocumentItem[] Sources,
    TimeIndex TimeIndex, IReadOnlyList<TimelineSegment> TimelineSegments, bool AllSourcesHaveTime)
{
    public static MergedDataSnapshot Empty { get; } = new([], [], new TimeIndex(), Array.Empty<TimelineSegment>(), false);
}

internal sealed record MergedViewSnapshot(MergedDataSnapshot Data, MergedLineRef[] Lines, int[]? FullIndexMap)
{
    public static MergedViewSnapshot Empty { get; } = new(MergedDataSnapshot.Empty, [], null);

    public MergedDocumentItem[] Sources => Data.Sources;
}
