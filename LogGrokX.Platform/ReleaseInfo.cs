namespace LogGrokX;

public sealed record ReleaseInfo(string Tag, string PageUrl, string Notes, IReadOnlyDictionary<string, string> Assets);

public sealed record PendingRelease(string Version, string Notes, string PageUrl);
