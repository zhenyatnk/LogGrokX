using System.Collections.Generic;
using System.Threading;

namespace LogGrokX.Data
{
    public static class MergedLineOrder
    {
        private readonly struct Cursor
        {
            public Cursor(int source, int line, long ticks)
            {
                Source = source;
                Line = line;
                Ticks = ticks;
            }

            public int Source { get; }

            public int Line { get; }

            public long Ticks { get; }
        }

        public static List<MergedLineRef> Build(IReadOnlyList<MergeSource> sources) =>
            Build(sources, new List<MergedLineRef>());

        public static List<MergedLineRef> Build(IReadOnlyList<MergeSource> sources, List<MergedLineRef> result,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.Clear();

            var total = 0;
            for (var i = 0; i < sources.Count; i++)
                total += sources[i].LineCount;

            result.EnsureCapacity(total);
            var queue = new PriorityQueue<Cursor, (long, int, int)>();

            for (var i = 0; i < sources.Count; i++)
            {
                if (sources[i].LineCount > 0)
                    Enqueue(queue, sources, i, 0);
            }

            while (queue.TryDequeue(out var cursor, out _))
            {
                if ((result.Count & 1023) == 0)
                    cancellationToken.ThrowIfCancellationRequested();
                result.Add(new MergedLineRef(cursor.Source, cursor.Line, cursor.Ticks));

                var next = cursor.Line + 1;
                if (next < sources[cursor.Source].LineCount)
                    Enqueue(queue, sources, cursor.Source, next);
            }

            return result;
        }

        public static MergedLineRef[] BuildArray(IReadOnlyList<MergeSource> sources,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var total = 0;
            for (var i = 0; i < sources.Count; i++)
                total += sources[i].LineCount;

            var result = new MergedLineRef[total];
            var resultIndex = 0;

            if (sources.Count == 1)
            {
                var source = sources[0];
                var hasTime = source.HasTime;
                var timeIndex = source.TimeIndex;
                for (var line = 0; line < source.LineCount; line++)
                {
                    if ((resultIndex & 1023) == 0)
                        cancellationToken.ThrowIfCancellationRequested();
                    result[resultIndex++] = new MergedLineRef(0, line, hasTime ? timeIndex.GetTicksAt(line) : -1);
                }

                return result;
            }

            var queue = new PriorityQueue<Cursor, (long, int, int)>();
            for (var i = 0; i < sources.Count; i++)
            {
                if (sources[i].LineCount > 0)
                    Enqueue(queue, sources, i, 0);
            }

            while (queue.TryDequeue(out var cursor, out _))
            {
                if ((resultIndex & 1023) == 0)
                    cancellationToken.ThrowIfCancellationRequested();
                result[resultIndex++] = new MergedLineRef(cursor.Source, cursor.Line, cursor.Ticks);

                var next = cursor.Line + 1;
                if (next < sources[cursor.Source].LineCount)
                    Enqueue(queue, sources, cursor.Source, next);
            }

            return result;
        }

        public static TimeIndex BuildTimeIndex(IReadOnlyList<MergedLineRef> lines) =>
            BuildTimeIndex(lines, new TimeIndex());

        public static TimeIndex BuildTimeIndex(IReadOnlyList<MergedLineRef> lines, TimeIndex target,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            target.Clear();
            target.AddRange(lines, static line => line.Ticks, cancellationToken);

            return target;
        }

        private static void Enqueue(PriorityQueue<Cursor, (long, int, int)> queue,
            IReadOnlyList<MergeSource> sources, int source, int line)
        {
            var mergeSource = sources[source];
            long ticks;
            long key;
            if (mergeSource.HasTime)
            {
                ticks = mergeSource.TimeIndex.GetTicksAt(line);
                key = ticks;
            }
            else
            {
                ticks = -1;
                key = long.MaxValue;
            }

            queue.Enqueue(new Cursor(source, line, ticks), (key, source, line));
        }
    }
}
