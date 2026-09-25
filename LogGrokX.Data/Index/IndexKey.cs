using System;
using System.Collections.Generic;
using System.Diagnostics;
using LogGrokX.Data.Monikers;

namespace LogGrokX.Data.Index
{
    public readonly struct IndexKey : IEquatable<IndexKey>
    {
        private readonly string _buffer;
        private readonly int _start;
        private readonly int _componentCount;
        private readonly bool _hasLocalBuffer;

        // Hash is computed once, at construction time: the key is looked up in
        // several dictionaries (and component sets) per log line, and hashing all
        // components again on every lookup used to dominate indexing time.
        private readonly int _hash;

        public IndexKey(string buffer, int start, int componentCount)
        {
            _buffer = buffer;
            _start = start;
            _componentCount = componentCount;
            _hasLocalBuffer = false;
            _hash = ComputeHash(buffer, start, componentCount);
        }

        private IndexKey(string buffer, int componentCount, bool hasLocalBuffer, int hash)
        {
            _buffer = buffer;
            _start = 0;
            _componentCount = componentCount;
            _hasLocalBuffer = hasLocalBuffer;
            _hash = hash;
        }

        public int ComponentCount => _componentCount;

        public bool HasLocalBuffer => _hasLocalBuffer;

        public unsafe IndexKey MakeLocalCopy()
        {
            var bufferSpan = _buffer.AsSpan(_start);
            string local;
            fixed (char* start = bufferSpan)
            {
                var meta = LineMetaInformation.Get(start, _componentCount);
                var size = meta.TotalSizeWithPayloadCharsAligned;
                local = new string(start, 0, size);
            }

            return new IndexKey(local, _componentCount, true, _hash);
        }

        public ReadOnlySpan<char> GetComponent(int index)
        {
            Debug.Assert(index < _componentCount);
            var dataSpan = GetDataSpan();
            var meta = GetComponentsMeta();
            return meta.GetComponent(dataSpan, index);
        }

        public bool Equals(IndexKey other)
        {
            var dataSpan = GetDataSpan();
            var meta = GetComponentsMeta();

            var otherDataSpan = other.GetDataSpan();
            var otherMeta = other.GetComponentsMeta();

            for (var idx = 0; idx < _componentCount; idx++)
            {
                if (!meta.GetComponent(dataSpan, idx).SequenceEqual(otherMeta.GetComponent(otherDataSpan, idx)))
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object? obj)
        {
            return obj is IndexKey other && Equals(other);
        }

        public override int GetHashCode() => _hash;

        private static unsafe int ComputeHash(string buffer, int start, int componentCount)
        {
            unchecked
            {
                var bufferSpan = buffer.AsSpan(start);
                var dataSpan = buffer.AsSpan(start + LineMetaInformation.GetSizeChars(componentCount));
                var result = 17;
                fixed (char* pointer = bufferSpan)
                {
                    var meta = LineMetaInformation.Get(pointer, componentCount).ParsedLineComponents;
                    for (var i = 0; i < componentCount; i++)
                    {
                        result = result * 31 + string.GetHashCode(meta.GetComponent(dataSpan, i));
                    }
                }

                return result;
            }
        }

        public override string ToString()
        {
            var meta = GetComponentsMeta();
            var dataSpan = GetDataSpan();

            var strings = new List<string>(_componentCount);
            for (var i = 0; i < _componentCount; i++)
            {
                strings.Add(meta.GetComponent(dataSpan, i).ToString());
            }

            return $"{{{string.Join(',', strings)}}}";
        }

        private unsafe ParsedLineComponents GetComponentsMeta()
        {
            var bufferSpan = _buffer.AsSpan(_start);
            fixed (char* start = bufferSpan)
            {
                return LineMetaInformation.Get(start, _componentCount).ParsedLineComponents;
            }
        }

        private ReadOnlySpan<char> GetDataSpan()
        {
            var dataSpan = _buffer.AsSpan(_start + LineMetaInformation.GetSizeChars(_componentCount));
            return dataSpan;
        }
    }
}
