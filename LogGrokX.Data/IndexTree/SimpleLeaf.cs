using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace LogGrokX.Data.IndexTree
{
    public sealed class SimpleLeaf<T> :
        LeafOrNode<T, SimpleLeaf<T>>,
        ILeaf<T, SimpleLeaf<T>>, ITreeNode<T>
    {
        private readonly List<T>? _storage;
        private readonly LineNumberDeltas? _lineNumbers;
        private readonly T _firstValue;
        private readonly int _firstValueIndex;
        private int _count;
        private SimpleLeaf<T>? _next;
        private const int LeafCapacity = 1024;
        private const int InitialLeafCapacity = 16;

        public SimpleLeaf(T firstValue, int valueIndex)
        {
            _firstValue = firstValue;
            if (typeof(T) == typeof(int))
                _lineNumbers = new LineNumberDeltas(Unsafe.As<T, int>(ref firstValue), LeafCapacity);
            else
                _storage = new List<T>(InitialLeafCapacity) {firstValue};

            _firstValueIndex = valueIndex;
            Volatile.Write(ref _count, 1);
        }

        public SimpleLeaf<T>? Add(T value, int valueIndex)
        {
            if (_count < LeafCapacity)
            {
                if (_lineNumbers != null)
                    _lineNumbers.Add(_count, Unsafe.As<T, int>(ref value));
                else
                    _storage!.Add(value);

                Volatile.Write(ref _count, _count + 1);
                return null;
            }

            var next = new SimpleLeaf<T>(value, valueIndex);
            Volatile.Write(ref _next, next);
            return next;
        }

        public override T FirstValue => _firstValue;

        public override int MinIndex => _firstValueIndex;

        public T this[int index]
        {
            get
            {
                if (_lineNumbers == null)
                    return _storage![index];

                if ((uint)index >= (uint)Count)
                    throw new ArgumentOutOfRangeException(nameof(index));

                var value = _lineNumbers.Read(index, _lineNumbers.CaptureDeltas());
                return Unsafe.As<int, T>(ref value);
            }
        }

        public int Count => Volatile.Read(ref _count);

        public override IEnumerable<T> GetEnumerableFromIndex(int index)
        {
            return _lineNumbers != null
                ? EnumerateLineNumbers(index >= MinIndex ? index - MinIndex : 0, true)
                : this.GetEnumerableFromIndex<T, SimpleLeaf<T>>(index);
        }
        
        public override T GetValue(int index)
        {
            return this.GetValue<T, SimpleLeaf<T>>(index);
        }

        public override (int index, SimpleLeaf<T> leaf) FindByValue(T value)
        {
            var count = Count;
            if (_lineNumbers != null)
                return (_lineNumbers.Find(Unsafe.As<T, int>(ref value), count) + _firstValueIndex, this);

            var index = _storage!.BinarySearch(0, count, value, null);
            return ((index >= 0 ? index : ~index) + _firstValueIndex, this);
        }

        public SimpleLeaf<T>? Next => Volatile.Read(ref _next);

        public IEnumerator<T> GetEnumerator() => _lineNumbers != null
            ? EnumerateLineNumbers(0, false).GetEnumerator()
            : _storage!.GetEnumerator();

        private IEnumerable<T> EnumerateLineNumbers(int start, bool includeFollowingLeaves)
        {
            SimpleLeaf<T>? leaf = this;
            while (leaf != null)
            {
                var next = includeFollowingLeaves ? leaf.Next : null;
                var count = leaf.Count;
                if (start < count)
                {
                    var storage = leaf._lineNumbers!;
                    var deltas = storage.CaptureDeltas();
                    var value = storage.Read(start, deltas);
                    yield return Unsafe.As<int, T>(ref value);
                    for (var index = start + 1; index < count; index++)
                    {
                        value = storage.ReadNext(index, value, deltas);
                        yield return Unsafe.As<int, T>(ref value);
                    }
                }

                start = 0;
                leaf = next;
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
