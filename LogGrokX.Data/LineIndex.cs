using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using LogGrokX.Data.Virtualization;

namespace LogGrokX.Data
{
    public class LineIndex : ILineIndex, IItemProvider<(long offset, int length)>
    {
        private const int MinRangeSize = 256;
        private const int OffsetBlockSize = 512;
        private readonly List<OffsetBlock> _lineStarts = new();
        private int _storedCount;
        private int? _lastLineLength;

        public (long offset, int length) GetLine(int index)
        {
            lock (_lineStarts)
            {
                if ((uint)index >= (uint)PublishedCount)
                    throw new IndexOutOfRangeException();

                var lineStart = GetOffset(index);
                return index < _storedCount - 1
                    ? (lineStart, (int)(GetOffset(index + 1) - lineStart))
                    : (lineStart, _lastLineLength!.Value);
            }
        }

        public void Fetch(int start, Span<(long offset, int length)> values)
        {
            lock (_lineStarts)
            {
                if ((uint)start >= (uint)_storedCount || values.Length > PublishedCount - start)
                    throw new IndexOutOfRangeException();

                var offset = GetOffset(start);
                var availableWithNext = _storedCount - start - 1;
                var consecutive = Math.Min(values.Length, availableWithNext);
                for (var i = 0; i < consecutive; i++)
                {
                    var next = GetOffset(start + i + 1);
                    values[i] = (offset, (int)(next - offset));
                    offset = next;
                }

                if (consecutive < values.Length)
                    values[consecutive] = (offset, _lastLineLength!.Value);
            }
        }

        private long GetOffset(int index) => _lineStarts[index / OffsetBlockSize][index % OffsetBlockSize];

        private int PublishedCount => _storedCount > 0 && !_lastLineLength.HasValue ? _storedCount - 1 : _storedCount;

        public async IAsyncEnumerable<(int start, int count)> FetchRanges(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var currentIndex = 0;
            var currentCount = Count;

            while (currentIndex < currentCount || !IsFinished)
            {
                try
                {
                    while (currentIndex + MinRangeSize > currentCount && !IsFinished)
                    {
                        var changed = _dataChangedSignal.Task;
                        currentCount = Count;
                        if (currentIndex + MinRangeSize <= currentCount || IsFinished)
                            break;
                        await changed.WaitAsync(cancellationToken);
                        ResetDataChangedSignal();
                        currentCount = Count;
                    }
                }
                catch (OperationCanceledException)
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

        private volatile TaskCompletionSource _dataChangedSignal =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private void NotifyDataChanged() => _dataChangedSignal.TrySetResult();

        private void ResetDataChangedSignal()
        {
            var current = _dataChangedSignal;
            if (current.Task.IsCompleted)
                _ = Interlocked.CompareExchange(ref _dataChangedSignal,
                    new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously), current);
        }

        public int Count
        {
            get
            {
                lock (_lineStarts)
                    return PublishedCount;
            }
        }

        public int Add(long lineStart)
        {
            int lineNum;
            lock (_lineStarts)
            {
                lineNum = _storedCount;
                var blockIndex = lineNum % OffsetBlockSize;
                if (blockIndex == 0)
                    _lineStarts.Add(new OffsetBlock(lineStart));
                else
                    _lineStarts[^1].Add(blockIndex, lineStart);
                _storedCount++;
            }
            if (lineNum > 0 && lineNum % MinRangeSize == 0)
                NotifyDataChanged();
            return lineNum;
        }

        public void Finish(int lastLength)
        {
            lock (_lineStarts)
                _lastLineLength = lastLength;
            NotifyDataChanged();
        }
        
        public bool IsFinished
        {
            get
            {
                lock (_lineStarts)
                    return _lastLineLength.HasValue;
            }
        }

        private sealed class OffsetBlock(long firstOffset)
        {
            private Array _storage = new ushort[OffsetBlockSize];

            public long this[int index] => _storage switch
            {
                ushort[] narrow => firstOffset + narrow[index],
                uint[] medium => firstOffset + medium[index],
                _ => ((long[])_storage)[index]
            };

            public void Add(int index, long offset)
            {
                var delta = offset >= firstOffset ? unchecked((ulong)(offset - firstOffset)) : ulong.MaxValue;
                switch (_storage)
                {
                    case ushort[] narrow when delta <= ushort.MaxValue:
                        narrow[index] = (ushort)delta;
                        return;
                    case ushort[] narrow when delta <= uint.MaxValue:
                        var expanded = new uint[OffsetBlockSize];
                        for (var i = 0; i < index; i++)
                            expanded[i] = narrow[i];
                        expanded[index] = (uint)delta;
                        _storage = expanded;
                        return;
                    case uint[] medium when delta <= uint.MaxValue:
                        medium[index] = (uint)delta;
                        return;
                    case long[] wide:
                        wide[index] = offset;
                        return;
                }

                var offsets = new long[OffsetBlockSize];
                for (var i = 0; i < index; i++)
                    offsets[i] = this[i];
                offsets[index] = offset;
                _storage = offsets;
            }
        }
    }
}
