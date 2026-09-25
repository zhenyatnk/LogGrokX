using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using LogGrokX.Data.IndexTree;

namespace LogGrokX.Data.Index;

public abstract class IndexerBase : IDisposable
{
    protected readonly ConcurrentDictionary<IndexKeyNum, IndexTree<int, SimpleLeaf<int>>> Indices =
        // concurrencyLevel = 1 serialized every write on a single lock;
        // indexing now happens from several threads.
        new(Environment.ProcessorCount, 16384);

    protected readonly CountIndex<IndexTree<int, SimpleLeaf<int>>> CountIndex;

    protected readonly ConcurrentDictionary<IndexKey, IndexKeyNum> KeysToNumbers;
    protected readonly ConcurrentDictionary<IndexKeyNum, IndexKey> NumbersToKeys;

    public IndexerBase(ConcurrentDictionary<IndexKey, IndexKeyNum> keysToNumbers,
        ConcurrentDictionary<IndexKeyNum, IndexKey> numbersToKeys)
    {
        KeysToNumbers = keysToNumbers;
        NumbersToKeys = numbersToKeys;
        
        CountIndex = new CountIndex<IndexTree<int, SimpleLeaf<int>>>(Indices);
    }

    public IIndexedLinesProvider GetIndexedLinesProvider(
        IReadOnlyDictionary<int, IEnumerable<string>> excludedComponents)
    {
        var updatableCounts =
            UpdatableValue.Create(() => CountIndex.Counts);
        
        return new IndexedLinesProvider(this, updatableCounts,
            CountIndex<IndexTree<int, SimpleLeaf<int>>>.Granularity, excludedComponents, NumbersToKeys);
    }

    public IndexTree<int, SimpleLeaf<int>> GetIndex(IndexKeyNum key) => Indices[key];
    
    
    public virtual int GetIndexCountForComponent(int componentIndex, string componentValue)
    {
        return Indices
            .Where(keyValuePair =>
                NumbersToKeys[keyValuePair.Key].GetComponent(componentIndex).SequenceEqual(componentValue.AsSpan()))
            .Sum(kv => kv.Value.Count);
    }

    private protected static IndexTree<int, SimpleLeaf<int>> CreateIndexTree()
    {
        return new IndexTree<int, SimpleLeaf<int>>(16,
            static value => new SimpleLeaf<int>(value, 0));
    }

    public void Dispose()
    {
        Indices.Clear();
    }

    public void Finish()
    {
        CountIndex.Finish(Indices);
    }
}