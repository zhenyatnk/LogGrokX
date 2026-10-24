namespace LogGrokX.MergedView;

internal static class MergedViewPalette
{
    private static readonly string[] Colors =
    [
        "4C8BF0", "3CB37A", "E08A3C", "B06CD8", "D85C6C", "3CA8C0", "C0A83C", "8A8A8A"
    ];

    public static int Count => Colors.Length;
    public static string GetRowColor(int index) => "#30" + Colors[Normalize(index)];
    public static string GetTimelineColor(int index) => "#66" + Colors[Normalize(index)];
    private static int Normalize(int index) => ((index % Colors.Length) + Colors.Length) % Colors.Length;
}
