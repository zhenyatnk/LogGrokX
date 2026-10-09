using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LogGrokX.Data.IndexTree;

namespace LogGrokX.Data.Index;

public class SubIndexer : IndexerBase
{
    public SubIndexer(
        ConcurrentDictionary<IndexKey, IndexKeyNum> keysToNumbers, ConcurrentDictionary<IndexKeyNum, IndexKey> numbersToKeys) 
        : base(keysToNumbers, numbersToKeys)
    {
    }

    public void Add(IndexKeyNum keyNumber, int lineNumber)
    {
        var index = GetOrCreateIndex(keyNumber);
            
        index.Add(lineNumber);
        CountIndex.Add(lineNumber, Indices);
    }
}

public class Indexer : IndexerBase, IComponentIndexer
{
    private readonly object _componentsLocker = new();

    private readonly ConcurrentDictionary<int, HashSet<IndexKey>> _components = new();

    private readonly ChunkedList<IndexKeyNum> _lineAndKeyIndex = new(16384);

    private int _currentCount = 0;
    public Indexer() 
        : base(new ConcurrentDictionary<IndexKey, IndexKeyNum>(), 
            new ConcurrentDictionary<IndexKeyNum, IndexKey>())
    {
    }

    public SubIndexer CreateSubIndexer()
    {
        return new SubIndexer(KeysToNumbers, NumbersToKeys);
    }

    public void Add(IndexKey key, int lineNumber)
    {
        if (!KeysToNumbers.TryGetValue(key, out var keyNumber))
            keyNumber = AddNewKey(key);

        _lineAndKeyIndex.Add(keyNumber);
            
        var index = GetOrCreateIndex(keyNumber);
            
        index.Add(lineNumber);
        CountIndex.Add(lineNumber, Indices);
    }

    private IndexKeyNum AddNewKey(IndexKey key)
    {
        var localKey = key.MakeLocalCopy();
        var keyNumber = new IndexKeyNum { KeyNum = Interlocked.Increment(ref _currentCount) };
        if (KeysToNumbers.TryAdd(localKey, keyNumber))
        {
            NumbersToKeys.TryAdd(keyNumber, localKey);
            UpdateComponents(localKey);
            return keyNumber;
        }

        if (KeysToNumbers.TryGetValue(localKey, out var existing))
            return existing;

        NumbersToKeys.TryAdd(keyNumber, localKey);
        return keyNumber;
    }

    private class ComponentComparer : IEqualityComparer<IndexKey>
    {
        private readonly int _index;

        public ComponentComparer(int index) => _index = index;

        public bool Equals(IndexKey x, IndexKey y) =>
            x.GetComponent(_index).SequenceEqual(y.GetComponent(_index));

        public int GetHashCode(IndexKey obj) => string.GetHashCode(obj.GetComponent(_index));
    }
    
    private void UpdateComponents(IndexKey key)
    {
        for (var componentIndex = 0; componentIndex < key.ComponentCount; componentIndex++)
        {
            var componentSet = _components.GetOrAdd(componentIndex,
                static index => new HashSet<IndexKey>(new ComponentComparer(index)));

            bool isAdded;
            lock (_componentsLocker)
            {
                isAdded = componentSet.Add(key);
            }

            if (isAdded)
                NewComponentAdded?.Invoke((componentIndex, key));
        }
    }

    public IEnumerable<string> GetAllComponents(int componentNumber)
    {
        if (!_components.TryGetValue(componentNumber, out var componentSet))
            return Enumerable.Empty<string>();

        lock (_componentsLocker)
        {
            return componentSet
                .Select(key => key.GetComponent(componentNumber).ToString()).ToList();
        }
    }

    public event Action<(int compnentNumber, IndexKey key)>? NewComponentAdded;

    private readonly Dictionary<Action<int, string>, Action<(int compnentNumber, IndexKey key)>> _componentAddedHandlers = new();

    event Action<int, string>? IComponentIndexer.ComponentAdded
    {
        add
        {
            if (value == null)
                return;

            void Handler((int compnentNumber, IndexKey key) added) =>
                value(added.compnentNumber, added.key.GetComponent(added.compnentNumber).ToString());

            _componentAddedHandlers[value] = Handler;
            NewComponentAdded += Handler;
        }
        remove
        {
            if (value != null && _componentAddedHandlers.Remove(value, out var handler))
                NewComponentAdded -= handler;
        }
    }

    public IndexKeyNum GetIndexKeyNum(int index) => _lineAndKeyIndex[index];

    public bool IsLineComponentEqual(int lineNumber, int componentIndex, ReadOnlySpan<char> value)
    {
        if (lineNumber < 0 || lineNumber >= _lineAndKeyIndex.Count)
            return false;

        var key = NumbersToKeys[_lineAndKeyIndex[lineNumber]];
        return componentIndex >= 0 && componentIndex < key.ComponentCount
               && key.GetComponent(componentIndex).SequenceEqual(value);
    }

    public bool IsLineIncluded(int lineNumber, IReadOnlyDictionary<int, IEnumerable<string>> excludedComponents)
    {
        if (excludedComponents.Count == 0)
            return true;

        if (lineNumber < 0 || lineNumber >= _lineAndKeyIndex.Count)
            return true;

        var key = NumbersToKeys[_lineAndKeyIndex[lineNumber]];
        foreach (var (componentIndex, componentValues) in excludedComponents)
        {
            var keyComponent = key.GetComponent(componentIndex);
            foreach (var componentValue in componentValues)
            {
                if (keyComponent.SequenceEqual(componentValue))
                    return false;
            }
        }

        return true;
    }
}
