
namespace LogGrokX.Controls
{
    public sealed class TimelineSegment
    {
        public TimelineSegment(int startLine, int endLine, string color)
        {
            StartLine = startLine;
            EndLine = endLine;
            Color = color;
        }

        public int StartLine { get; }

        public int EndLine { get; }

        public string Color { get; }
    }
}