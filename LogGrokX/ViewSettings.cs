
namespace LogGrokX
{
    public class ViewSettings
    {
        public enum ViewBigLine
        {
            Prune = 0,
            Break
        }

        public ViewBigLine BigLine { get; set; } = ViewBigLine.Break;
        public int BigLineSize { get; set; } = 9728;

        public bool TimelineAtTop { get; set; }

        public double LogFontSize { get; set; } = 12;

        public bool GroupByThread { get; set; }

        public bool MergedFilesView { get; set; }

        public bool CheckForUpdates { get; set; } = true;
    }
}