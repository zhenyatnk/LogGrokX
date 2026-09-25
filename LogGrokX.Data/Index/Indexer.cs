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
        var index = Indices.GetOrAdd(keyNumber, static _ => CreateIndexTree());
            
        index.Add(lineNumber);
        CountIndex.Add(lineNumber, Indices);
    }
}

public class Indexer : IndexerBase, IComponentIndexer
{
    /// <summary>
    /// Per-component registry: component value -> keys containing that value.
    /// Maintained incrementally when a new index key appears (rare), so that
    /// neither <see cref="GetAllComponents"/> nor
    /// <see cref="IndexerBase.GetIndexCountForComponent"/> has to walk all keys.
    /// </summary>
    private sealed class ComponentRegistry
    {
        private readonly object _locker = new();
        private readonly Dictionary<string, List<IndexKeyNum>> _valuesToKeys = new(StringComparer.Ordinal);
        private List<string>? _cachedValues;

        public bool TryAdd(ReadOnlySpan<char> value, IndexKeyNum keyNumber)
        {
            lock (_locker)
            {
                var lookup = _valuesToKeys.GetAlternateLookup<ReadOnlySpan<char>>();
                if (lookup.TryGetValue(value, out var keys))
                {
                    if (!keys.Contains(keyNumber))
                        keys.Add(keyNumber);
                    return false;
                }

                _valuesToKeys.Add(value.ToString(), new List<IndexKeyNum> { keyNumber });
                _cachedValues = null;
                return true;
            }
        }

        public IReadOnlyList<string> Values
        {
            get
            {
                lock (_locker)
                {
                    return _cachedValues ??= _valuesToKeys.Keys.ToList();
                }
            }
        }

        public IndexKeyNum[] GetKeys(string value)
        {
            lock (_locker)
            {
                return _valuesToKeys.TryGetValue(value, out var keys)
                    ? keys.ToArray()
                    : Array.Empty<IndexKeyNum>();
            }
        }
    }

    private readonly ConcurrentDictionary<int, ComponentRegistry> _components = new();

    private readonly ChunkedList<IndexKeyNum> _lineAndKeyIndex = new(16384);

    private int _currentCount = 0;
    public Indexer() 
        : base(new ConcurrentDictionary<IndexKey, IndexKeyNum>(Environment.ProcessorCount, 1024), 
            new ConcurrentDictionary<IndexKeyNum, IndexKey>(Environment.ProcessorCount, 1024))
    {
    }

    public SubIndexer CreateSubIndexer()
    {
        return new SubIndexer(KeysToNumbers, NumbersToKeys);
    }

    public void Add(IndexKey key, int lineNumber)
    {
        var (keyNumber, index) = ResolveKey(key, null);
        Append(keyNumber, index, lineNumber);
    }

    /// <summary>
    /// Resolves a key to its number and index tree. Thread-safe: this is the
    /// expensive, order-independent part of indexing (hashing the key, dictionary
    /// lookups, registering new components) and is done by parallel workers.
    /// <para>
    /// When <paramref name="pendingNotifications"/> is provided, new-component
    /// notifications are collected instead of being raised, so that the merge
    /// thread can raise them in line order, from a single thread.
    /// </para>
    /// </summary>
    internal (IndexKeyNum KeyNumber, IndexTree<int, SimpleLeaf<int>> Index) ResolveKey(IndexKey key,
        List<(int componentNumber, IndexKey key)>? pendingNotifications)
    {
        if (!KeysToNumbers.TryGetValue(key, out var keyNumber))
            keyNumber = AddNewKey(key, pendingNotifications);

        var index = Indices.GetOrAdd(keyNumber, static _ => CreateIndexTree());
        return (keyNumber, index);
    }

    /// <summary>
    /// Appends one line to the index. Must be called in line order, from a single
    /// thread (the merge thread of the loading pipeline).
    /// </summary>
    internal void Append(IndexKeyNum keyNumber, IndexTree<int, SimpleLeaf<int>> index, int lineNumber)
    {
        _lineAndKeyIndex.Add(keyNumber);
        index.Add(lineNumber);
        CountIndex.Add(lineNumber, Indices);
    }

    internal void RaiseComponentNotifications(List<(int componentNumber, IndexKey key)> notifications)
    {
        foreach (var (componentNumber, key) in notifications)
            NewComponentAdded?.Invoke((componentNumber, key));
    }

    private IndexKeyNum AddNewKey(IndexKey key, List<(int componentNumber, IndexKey key)>? pendingNotifications)
    {
        var localKey = key.MakeLocalCopy();
        var keyNumber = new IndexKeyNum { KeyNum = Interlocked.Increment(ref _currentCount) };
        if (KeysToNumbers.TryAdd(localKey, keyNumber))
        {
            NumbersToKeys.TryAdd(keyNumber, localKey);
            UpdateComponents(localKey, keyNumber, pendingNotifications);
            return keyNumber;
        }

        if (KeysToNumbers.TryGetValue(localKey, out var existing))
            return existing;

        NumbersToKeys.TryAdd(keyNumber, localKey);
        return keyNumber;
    }

    private void UpdateComponents(IndexKey key, IndexKeyNum keyNumber,
        List<(int componentNumber, IndexKey key)>? pendingNotifications)
    {
        for (var componentIndex = 0; componentIndex < key.ComponentCount; componentIndex++)
        {
            var registry = _components.GetOrAdd(componentIndex, static _ => new ComponentRegistry());

            if (!registry.TryAdd(key.GetComponent(componentIndex), keyNumber))
                continue;

            if (pendingNotifications != null)
                pendingNotifications.Add((componentIndex, key));
            else
                NewComponentAdded?.Invoke((componentIndex, key));
        }
    }

    public IEnumerable<string> GetAllComponents(int componentNumber)
    {
        return _components.TryGetValue(componentNumber, out var registry)
            ? registry.Values
            : Enumerable.Empty<string>();
    }

    public override int GetIndexCountForComponent(int componentIndex, string componentValue)
    {
        if (!_components.TryGetValue(componentIndex, out var registry))
            return 0;

        var count = 0;
        foreach (var keyNumber in registry.GetKeys(componentValue))
        {
            if (Indices.TryGetValue(keyNumber, out var index))
                count += index.Count;
        }

        return count;
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
