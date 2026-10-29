using System.Collections.Generic;
using System.Threading;
using LogGrokX.Data;

namespace LogGrokX;

public readonly record struct DecodedPartState(Base64Content Base64, bool Hex, bool Symbols = false)
{
    public bool IsEmpty => Base64 == Base64Content.None && !Hex && !Symbols;
}

public readonly record struct DecodedPartKey(DecodedPartsState State, object? Source, int Line, int Field)
{
    public DecodedPartState Get() => State.Get(Source, Line, Field);

    public void Set(DecodedPartState state) => State.Set(Source, Line, Field, state);
}

// Row view models are re-created by virtualization and collection resets, so the BIN / "Decode ..." / SYM state of a
// cell lives here, per view, keyed by the source document, the original line number and the field (#78).
public sealed class DecodedPartsState
{
    private readonly Dictionary<(object? source, int line, int field), DecodedPartState> _states = new();
    private readonly object _sync = new();
    private int _version;

    public int Version => Volatile.Read(ref _version);

    public int Count
    {
        get
        {
            lock (_sync)
                return _states.Count;
        }
    }

    public DecodedPartKey For(int line, int field) => new(this, null, line, field);

    public DecodedPartKey For(object source, int line, int field) => new(this, source, line, field);

    public DecodedPartState Get(object? source, int line, int field)
    {
        lock (_sync)
            return _states.TryGetValue((source, line, field), out var state) ? state : default;
    }

    public void Set(object? source, int line, int field, DecodedPartState state)
    {
        lock (_sync)
        {
            var key = (source, line, field);
            if (_states.TryGetValue(key, out var current) ? current == state : state.IsEmpty)
                return;

            if (state.IsEmpty)
                _states.Remove(key);
            else
                _states[key] = state;
            Interlocked.Increment(ref _version);
        }
    }
}
