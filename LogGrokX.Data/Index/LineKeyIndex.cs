using System;
using System.Threading;

namespace LogGrokX.Data.Index;

internal sealed class LineKeyIndex
{
    private const int ChunkSize = 16384;
    private Chunk[] _chunks = Array.Empty<Chunk>();
    private int _count;

    private sealed class Chunk
    {
        public Array Keys = new byte[ChunkSize];
    }

    public int Count => Volatile.Read(ref _count);

    public void Add(IndexKeyNum key)
    {
        var chunkIndex = _count / ChunkSize;
        var index = _count % ChunkSize;
        if (index == 0)
        {
            if (chunkIndex >= _chunks.Length)
            {
                var expanded = new Chunk[Math.Max(4, chunkIndex * 2)];
                _chunks.CopyTo(expanded, 0);
                Volatile.Write(ref _chunks, expanded);
            }
            _chunks[chunkIndex] = new Chunk();
        }

        PackedUInt32.Set(ref _chunks[chunkIndex].Keys, index, unchecked((uint)key.KeyNum));
        Volatile.Write(ref _count, _count + 1);
    }

    public IndexKeyNum this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count)
                throw new IndexOutOfRangeException();

            var chunk = Volatile.Read(ref _chunks)[index / ChunkSize];
            var keys = Volatile.Read(ref chunk.Keys);
            return new IndexKeyNum { KeyNum = unchecked((int)PackedUInt32.Get(keys, index % ChunkSize)) };
        }
    }
}
