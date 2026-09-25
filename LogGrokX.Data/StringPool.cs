using System;
using System.Collections.Concurrent;
using System.Threading;

namespace LogGrokX.Data
{
    public class StringPool
    {
        // Buffers are large (64 KB and up) and live on the LOH, so an unbounded
        // pool used to retain hundreds of megabytes for the whole session.
        // Retention is capped per bucket, oversized buffers are not pooled at all.
        private const long MaxRetainedChars = 32L * 1024 * 1024;
        private const int MaxPooledStringSize = 8 * 1024 * 1024;

        private class StringPoolBucket
        {
            private readonly int _stringSize;
            private readonly int _maxCount;
            private readonly ConcurrentBag<string> _pool = new();
            private int _count;

            public StringPoolBucket(int stringSize)
            {
                _stringSize = stringSize;
                _maxCount = (int)Math.Clamp(MaxRetainedChars / Math.Max(stringSize, 1), 2, 1024);
            }

            public string Rent()
            {
                if (_pool.TryTake(out var result))
                {
                    Interlocked.Decrement(ref _count);
                    return result;
                }

                return new string('\0', _stringSize);
            }

            public void Return(string returned)
            {
                if (Volatile.Read(ref _count) >= _maxCount)
                    return;

                Interlocked.Increment(ref _count);
                _pool.Add(returned);
            }
        }

        private readonly ConcurrentDictionary<int, StringPoolBucket> _buckets = new();

        private readonly Func<int, StringPoolBucket> _bucketFactory = size => new StringPoolBucket(size);

        public string Rent(int size)
        {
            var pooledStringSize = size < 32 ? 32 : Pow2Roundup(size);
            if (pooledStringSize > MaxPooledStringSize)
                return new string('\0', pooledStringSize);

            var bucket = _buckets.GetOrAdd(pooledStringSize, _bucketFactory);
            return bucket.Rent();
        }

        public void Return(string returned)
        {
            if (returned.Length > MaxPooledStringSize)
                return;

            if (!_buckets.TryGetValue(returned.Length, out var bucket))
                throw new InvalidOperationException();

            bucket.Return(returned);
        }

        private static int Pow2Roundup (int x)
        {
            --x;
            x |= x >> 1;
            x |= x >> 2;
            x |= x >> 4;
            x |= x >> 8;
            x |= x >> 16;
            return x+1;
        }
    }
}
