namespace LogGrokX.Platform;

public sealed record UpdateRestartSearch(string Pattern, bool IsCaseSensitive, bool UseRegex);

public sealed record UpdateRestartDocument(string DocumentId)
{
    public UpdateRestartSearch[] Searches { get; init; } = [];
    public int CurrentSearchIndex { get; init; } = -1;
    public int[] MarkedLines { get; init; } = [];
    public int CurrentLine { get; init; } = -1;
    public int ScrollPosition { get; init; } = -1;
    public bool IsActive { get; init; }
}
