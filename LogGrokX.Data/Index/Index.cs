using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace LogGrokX.Data.Index
{
    public interface IIndex<T>
    {
        void Add(T value);

        int Count { get; }

        IEnumerable<T> GetEnumerableFromValue(T value);
    }

    public class Index : IIndex<int>, IDisposable
    {
        private const int DefaultStartChunkSize = 1024;

        // Arrays come from ArrayPool and may be larger than requested, so the
        // number of valid items has to be stored explicitly - otherwise
        // enumeration walks the rented tail and yields garbage.
        private readonly record struct Chunk(int MaxValue, int[] Data, int Count);

        private int _chunkSize;
        private int[]? _currentChunk;
        private int _currentIndexInChunk;
        private readonly ReaderWriterLockSlim _chunksLock = new();

        private readonly List<Chunk> _chunks = new(16);

        public Index(int chunkSize)
        {
            Debug.Assert(chunkSize > 0);
            _chunkSize = chunkSize;
        }

        public Index() : this(DefaultStartChunkSize)
        {
        }

        public bool IsEmpty => Volatile.Read(ref _currentChunk) == null;

        public void Add(int value)
        {
            if (_currentChunk == null || _currentIndexInChunk + 1 > _currentChunk.Length)
            {
                CreateNextChunk();
            }

            _currentChunk![_currentIndexInChunk] = value;
            // Single writer, many readers: publish the new length with a volatile
            // write so readers never observe a half-written item.
            Volatile.Write(ref _currentIndexInChunk, _currentIndexInChunk + 1);
            Volatile.Write(ref _count, _count + 1);
        }

        private int _count;

        public int Count => Volatile.Read(ref _count);

        public IEnumerable<int> GetEnumerableFromValue(int from)
        {
            Chunk[] chunks;
            int[] lastChunk;
            int lastChunkCount;
            int lastChunkMaxValue;

            _chunksLock.EnterReadLock();
            try
            {
                if (_currentChunk == null || Volatile.Read(ref _currentIndexInChunk) == 0)
                {
                    if (_chunks.Count == 0)
                        yield break;
                }

                chunks = _chunks.ToArray();
                lastChunk = _currentChunk ?? Array.Empty<int>();
                lastChunkCount = Volatile.Read(ref _currentIndexInChunk);
                lastChunkMaxValue = lastChunkCount > 0 ? lastChunk[lastChunkCount - 1] : int.MinValue;
            }
            finally
            {
                _chunksLock.ExitReadLock();
            }

            var (foundChunkIndex, foundIndex) =
                FindStart(from, chunks, lastChunk, lastChunkMaxValue, lastChunkCount);

            if (foundChunkIndex < 0)
                yield break;

            var chunkIndex = foundChunkIndex;
            var chunkStartIndex = foundIndex;
            while (chunkIndex < chunks.Length)
            {
                var chunk = chunks[chunkIndex];
                for (var idx = chunkStartIndex; idx < chunk.Count; idx++)
                    yield return chunk.Data[idx];
                chunkIndex++;
                chunkStartIndex = 0;
            }

            var startIndex = foundChunkIndex == chunks.Length ? foundIndex : 0;
            for (var idx = startIndex; idx < lastChunkCount; idx++)
                yield return lastChunk[idx];
        }
        
        // Find index of smallest stored value which is greater or equals then argument
        private static (int chunk, int index) FindStart(int value,
            Chunk[] chunks,
            int[] currentChunk,
            int currentChunkMaxValue,
            int currentIndexInChunk)
        {
            int GetChunkIndex(int val)
            {
                for (var i = 0; i < chunks.Length; i++)
                {
                    if (chunks[i].MaxValue >= val)
                        return i;
                }

                if (currentIndexInChunk > 0 && currentChunkMaxValue >= val)
                    return chunks.Length;
                return -1;
            }

            var chunkIndex = GetChunkIndex(value);
            if (chunkIndex < 0)
                return (-1, 0);

            var spanToSearch =
                chunkIndex == chunks.Length
                    ? new Span<int>(currentChunk, 0, currentIndexInChunk)
                    : new Span<int>(chunks[chunkIndex].Data, 0, chunks[chunkIndex].Count);

            var foundIndex = spanToSearch.BinarySearch(value);
            return (chunkIndex, foundIndex >= 0 ? foundIndex : ~foundIndex);
        }

        private void CreateNextChunk()
        {
            _chunksLock.EnterWriteLock();
            try
            {
                if (_currentChunk != null)
                {
                    _chunks.Add(new Chunk(_currentChunk[_currentIndexInChunk - 1], _currentChunk,
                        _currentIndexInChunk));
                }

                _currentChunk = ArrayPool<int>.Shared.Rent(_chunkSize);
                _currentIndexInChunk = 0;
                _chunkSize *= 2;
            }
            finally
            {
                _chunksLock.ExitWriteLock();
            }
        }

        public void Dispose()
        {
            _chunksLock.EnterWriteLock();
            try
            {
                foreach (var chunk in _chunks)
                {
                    ArrayPool<int>.Shared.Return(chunk.Data);
                }

                _chunks.Clear();

                if (_currentChunk != null)
                {
                    ArrayPool<int>.Shared.Return(_currentChunk);
                    _currentChunk = null;
                }

                _currentIndexInChunk = 0;
            }
            finally
            {
                _chunksLock.ExitWriteLock();
            }
        }
    }
}
