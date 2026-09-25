using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using LogGrokX.Data.IndexTree;
using LogGrokX.Data.Virtualization;

namespace LogGrokX.Data
{
    public class LineIndex : ILineIndex, IItemProvider<(long offset, int length)>
    {
        public (long offset, int length) GetLine(int index)
        {
            Debug.Assert(index < Count);
            lock (_lineStarts)
            {
                var lineStart = _lineStarts[index];
                if (index < _lineStarts.Count - 1)
                    return (lineStart, (int)(_lineStarts[index + 1] - lineStart));
                
                if (!_lastLineLength.HasValue)
                    throw new IndexOutOfRangeException();
                return (lineStart, _lastLineLength!.Value);
            }
        }

        public void Fetch(int start, Span<(long offset, int length)> values)
        {
            lock (_lineStarts)
            {
                var lineStartsEnumerable = _lineStarts.GetEnumerableFromIndex(start);
                using var enumerator = lineStartsEnumerable.GetEnumerator();
                if (!enumerator.MoveNext())
                    throw new IndexOutOfRangeException();
                var first = enumerator.Current;

                var index = 0;
                while (enumerator.MoveNext() && index < values.Length)
                {
                    var second = enumerator.Current;
                    values[index] = (first, (int) (second - first));
                    first = second;
                    index++;
                }

                if (index >= values.Length) return;
                
                if (start + index == Count - 1 &&_lastLineLength.HasValue)
                    values[index] = (first, _lastLineLength!.Value);
                else
                    throw new IndexOutOfRangeException();
            }
        }


        public async IAsyncEnumerable<(int start, int count)> FetchRanges(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            const int minRangeSize = 256;
            var currentIndex = 0;
            var currentCount = Count;

            while (currentIndex < currentCount || !IsFinished)
            {
                try
                {
                    while (currentIndex + minRangeSize > currentCount && !IsFinished)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
                        currentCount = Count;
                    }
                }
                catch (TaskCanceledException)
                {
                    yield break;
                }

                if (cancellationToken.IsCancellationRequested)
                    yield break;

                
                var rangeSize = currentCount - currentIndex;
                yield return (currentIndex, rangeSize);
                currentIndex += rangeSize;
                currentCount = Count;
            }
        }

        // Count is polled in tight loops (search progress, UI virtualization),
        // so it is served from a volatile counter instead of taking the lock.
        public int Count
        {
            get
            {
                var lineStartCount = Volatile.Read(ref _lineStartCount);
                if (lineStartCount == 0)
                    return 0;
                return Volatile.Read(ref _lastLineLengthValue) >= 0 ? lineStartCount : lineStartCount - 1;
            }
        }

        /// <summary>
        /// Appends a batch of line starts under a single lock and returns the line
        /// number of the first appended line.
        /// </summary>
        public int AddRange(ReadOnlySpan<long> lineStarts)
        {
            if (lineStarts.IsEmpty)
                return Count;

            lock (_lineStarts)
            {
                var firstLineNum = _lineStarts.Count;
                foreach (var lineStart in lineStarts)
                    _lineStarts.Add(lineStart);
                Volatile.Write(ref _lineStartCount, _lineStarts.Count);
                return firstLineNum;
            }
        }

        public int Add(long lineStart)
        {
            lock (_lineStarts)
            {
                var lineNum = _lineStarts.Count;
                _lineStarts.Add(lineStart);
                Volatile.Write(ref _lineStartCount, _lineStarts.Count);
                return lineNum;
            }
        }

        public void Finish(int lastLength)
        {
            _lastLineLength = lastLength;
            Volatile.Write(ref _lastLineLengthValue, lastLength);
        }
        
        public bool IsFinished => Volatile.Read(ref _lastLineLengthValue) >= 0;

        private readonly IndexTree<long, LongsLeaf> _lineStarts 
            = new(16, l => new LongsLeaf(l, 0));
        private int? _lastLineLength;
        private int _lineStartCount;
        private int _lastLineLengthValue = -1;
    }
}
