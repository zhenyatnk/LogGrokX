using System;
using System.Threading;
using LogGrokX.Data.Index;

namespace LogGrokX.Data.IndexTree;

internal sealed class LineNumberDeltas
{
    private const int CheckpointInterval = 32;
    private readonly int _firstValue;
    private readonly int _capacity;
    private int[]? _checkpoints;
    private Array _deltas = Array.Empty<byte>();
    private int _lastValue;

    public LineNumberDeltas(int firstValue, int capacity)
    {
        _firstValue = _lastValue = firstValue;
        _capacity = capacity;
    }

    public void Add(int index, int value)
    {
        if (index % CheckpointInterval == 0)
        {
            _checkpoints ??= new int[(_capacity - 1) / CheckpointInterval];
            _checkpoints[index / CheckpointInterval - 1] = value;
        }
        else
        {
            PackedUInt32.Set(ref _deltas, index, unchecked((uint)(value - _lastValue)));
        }

        _lastValue = value;
    }

    public Array CaptureDeltas() => Volatile.Read(ref _deltas);

    public int Read(int index, Array deltas)
    {
        var checkpoint = index / CheckpointInterval;
        var value = GetCheckpoint(checkpoint);
        for (var i = checkpoint * CheckpointInterval + 1; i <= index; i++)
            value = unchecked(value + (int)PackedUInt32.Get(deltas, i));

        return value;
    }

    public int ReadNext(int index, int previous, Array deltas) =>
        index % CheckpointInterval == 0
            ? GetCheckpoint(index / CheckpointInterval)
            : unchecked(previous + (int)PackedUInt32.Get(deltas, index));

    public int Find(int value, int count)
    {
        if (value <= _firstValue)
            return 0;

        var checkpointCount = (count - 1) / CheckpointInterval;
        var checkpoint = checkpointCount == 0 ? -1 : Array.BinarySearch(_checkpoints!, 0, checkpointCount, value);
        if (checkpoint >= 0)
            return (checkpoint + 1) * CheckpointInterval;

        checkpoint = ~checkpoint;
        var index = checkpoint * CheckpointInterval;
        var last = Math.Min(index + CheckpointInterval, count);
        var current = GetCheckpoint(checkpoint);
        var deltas = CaptureDeltas();
        while (current < value && ++index < last)
            current = unchecked(current + (int)PackedUInt32.Get(deltas, index));

        return index;
    }

    private int GetCheckpoint(int checkpoint) => checkpoint == 0 ? _firstValue : _checkpoints![checkpoint - 1];
}
