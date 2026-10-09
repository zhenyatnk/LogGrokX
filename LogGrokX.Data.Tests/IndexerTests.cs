using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LogGrokX.Data.Index;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Data.Tests;

[TestClass]
public class IndexerTests
{
    private const string Format = @"^(?'A'\w+) (?'B'\w+) (?'C'.*)$";

    private static LogMetaInformation Meta() =>
        TestHelpers.CreateMeta(Format, new[] { "A", "C" });

    private static IndexKey Key(string line) =>
        TestHelpers.CreateIndexKey(Meta(), line, out _);

    [TestMethod]
    public void AddRegistersKeysAndLines()
    {
        using var indexer = new Indexer();
        indexer.Add(Key("alpha beta payload"), 0);
        indexer.Add(Key("gamma delta payload"), 1);
        indexer.Add(Key("alpha beta payload"), 2);

        Assert.AreEqual(2, indexer.GetAllComponents(0).Count());
        Assert.AreEqual(2, indexer.GetIndexCountForComponent(0, "alpha"));
    }

    [TestMethod]
    public void GetAllComponentsReturnsDistinctComponentValues()
    {
        using var indexer = new Indexer();
        indexer.Add(Key("alpha beta one"), 0);
        indexer.Add(Key("gamma delta two"), 1);
        indexer.Add(Key("alpha beta three"), 2);

        CollectionAssert.AreEquivalent(new[] { "alpha", "gamma" }, indexer.GetAllComponents(0).ToList());
        CollectionAssert.AreEquivalent(new[] { "one", "two", "three" }, indexer.GetAllComponents(1).ToList());
    }

    [TestMethod]
    public void GetAllComponentsForUnknownComponentIsEmpty()
    {
        using var indexer = new Indexer();
        indexer.Add(Key("alpha beta one"), 0);

        Assert.AreEqual(0, indexer.GetAllComponents(5).Count());
    }

    [TestMethod]
    public void NewComponentAddedIsRaisedOncePerKey()
    {
        using var indexer = new Indexer();
        var added = new List<(int, string)>();
        indexer.NewComponentAdded += item => added.Add((item.compnentNumber, item.key.GetComponent(item.compnentNumber).ToString()));

        indexer.Add(Key("alpha beta one"), 0);
        indexer.Add(Key("alpha beta two"), 1);

        CollectionAssert.AreEquivalent(new[] { (0, "alpha"), (1, "one"), (1, "two") }, added);
    }

    [TestMethod]
    public void IsLineComponentEqualComparesIndexedComponentOfLine()
    {
        using var indexer = new Indexer();
        indexer.Add(Key("alpha beta one"), 0);
        indexer.Add(Key("gamma delta two"), 1);

        Assert.IsTrue(indexer.IsLineComponentEqual(0, 0, "alpha"));
        Assert.IsFalse(indexer.IsLineComponentEqual(0, 0, "gamma"));
        Assert.IsTrue(indexer.IsLineComponentEqual(1, 0, "gamma"));
        Assert.IsTrue(indexer.IsLineComponentEqual(1, 1, "two"));
    }

    [TestMethod]
    public void IsLineComponentEqualIsFalseForOutOfRangeArguments()
    {
        using var indexer = new Indexer();
        indexer.Add(Key("alpha beta one"), 0);

        Assert.IsFalse(indexer.IsLineComponentEqual(-1, 0, "alpha"));
        Assert.IsFalse(indexer.IsLineComponentEqual(1, 0, "alpha"));
        Assert.IsFalse(indexer.IsLineComponentEqual(0, -1, "alpha"));
        Assert.IsFalse(indexer.IsLineComponentEqual(0, 5, "alpha"));
    }

    [TestMethod]
    public void GetIndexCountForComponentCountsLines()
    {
        using var indexer = new Indexer();
        indexer.Add(Key("alpha beta one"), 0);
        indexer.Add(Key("gamma delta two"), 1);
        indexer.Add(Key("alpha beta three"), 2);

        Assert.AreEqual(2, indexer.GetIndexCountForComponent(0, "alpha"));
        Assert.AreEqual(1, indexer.GetIndexCountForComponent(0, "gamma"));
        Assert.AreEqual(0, indexer.GetIndexCountForComponent(0, "missing"));
    }

    [TestMethod]
    public void GetIndexCountForComponentRefreshesCachedKeysAndLiveCounts()
    {
        using var indexer = new Indexer();
        indexer.Add(Key("alpha beta one"), 0);
        Assert.AreEqual(1, indexer.GetIndexCountForComponent(0, "alpha"));

        indexer.Add(Key("alpha beta one"), 1);
        Assert.AreEqual(2, indexer.GetIndexCountForComponent(0, "alpha"));

        indexer.Add(Key("gamma delta two"), 2);
        Assert.AreEqual(2, indexer.GetIndexCountForComponent(0, "alpha"));

        indexer.Add(Key("alpha beta three"), 3);
        Assert.AreEqual(3, indexer.GetIndexCountForComponent(0, "alpha"));
        Assert.AreEqual(1, indexer.GetIndexCountForComponent(0, "gamma"));
    }

    [TestMethod]
    public void SubIndexerRefreshesCachedComponentsWhenItsOwnKeysAreAdded()
    {
        using var indexer = new Indexer();
        indexer.Add(Key("alpha beta one"), 0);
        indexer.Add(Key("alpha beta two"), 1);
        using var subIndexer = indexer.CreateSubIndexer();
        Assert.AreEqual(0, subIndexer.GetIndexCountForComponent(0, "alpha"));

        subIndexer.Add(indexer.GetIndexKeyNum(0), 0);
        Assert.AreEqual(1, subIndexer.GetIndexCountForComponent(0, "alpha"));
        subIndexer.Add(indexer.GetIndexKeyNum(0), 1);
        Assert.AreEqual(2, subIndexer.GetIndexCountForComponent(0, "alpha"));
        subIndexer.Add(indexer.GetIndexKeyNum(1), 2);
        Assert.AreEqual(3, subIndexer.GetIndexCountForComponent(0, "alpha"));
    }

    [TestMethod]
    public void GetIndexKeyNumAndIndexRoundTrip()
    {
        using var indexer = new Indexer();
        indexer.Add(Key("alpha beta one"), 0);
        indexer.Add(Key("gamma delta two"), 1);

        var keyNum = indexer.GetIndexKeyNum(0);
        var index = indexer.GetIndex(keyNum);

        Assert.AreEqual(1, index.Count);
        Assert.AreEqual(0, index[0]);
    }

    [TestMethod]
    public void IsLineIncludedHonoursExclusions()
    {
        using var indexer = new Indexer();
        indexer.Add(Key("alpha beta one"), 0);
        indexer.Add(Key("gamma delta two"), 1);

        var excluded = new Dictionary<int, IEnumerable<string>> { [0] = new[] { "alpha" } };

        Assert.IsFalse(indexer.IsLineIncluded(0, excluded));
        Assert.IsTrue(indexer.IsLineIncluded(1, excluded));
        Assert.IsTrue(indexer.IsLineIncluded(0, new Dictionary<int, IEnumerable<string>>()));
        Assert.IsTrue(indexer.IsLineIncluded(-1, excluded));
        Assert.IsTrue(indexer.IsLineIncluded(100, excluded));
    }

    [TestMethod]
    public void SubIndexerSharesKeySpace()
    {
        using var indexer = new Indexer();
        indexer.Add(Key("alpha beta one"), 0);

        var subIndexer = indexer.CreateSubIndexer();
        var keyNum = indexer.GetIndexKeyNum(0);
        subIndexer.Add(keyNum, 1);
        subIndexer.Finish();

        Assert.AreEqual(1, subIndexer.GetIndex(keyNum).Count);
        Assert.AreEqual(1, indexer.GetIndex(keyNum).Count);
        Assert.AreEqual(1, indexer.GetAllComponents(0).Count());
    }

    [TestMethod]
    public void IndexedLinesProviderMergesKeysInOrder()
    {
        using var indexer = new Indexer();
        indexer.Add(Key("alpha beta one"), 0);
        indexer.Add(Key("gamma delta two"), 1);
        indexer.Add(Key("alpha beta three"), 2);
        indexer.Add(Key("gamma delta four"), 3);
        indexer.Finish();

        var provider = indexer.GetIndexedLinesProvider(new Dictionary<int, IEnumerable<string>>());

        Assert.AreEqual(4, provider.Count);
        var values = new int[provider.Count];
        provider.Fetch(0, values);
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, values);
        Assert.AreEqual(2, provider.GetIndexByValue(2));
    }

    [TestMethod]
    public void IndexedLinesProviderFetchBeyondRange()
    {
        using var indexer = new Indexer();
        indexer.Add(Key("alpha beta one"), 0);
        indexer.Finish();

        var provider = indexer.GetIndexedLinesProvider(new Dictionary<int, IEnumerable<string>>());
        var values = new int[3];
        provider.Fetch(0, values);

        Assert.AreEqual(0, values[0]);
    }

    [TestMethod]
    public void IndexedLinesProviderExcludesKeys()
    {
        using var indexer = new Indexer();
        indexer.Add(Key("alpha beta one"), 0);
        indexer.Add(Key("gamma delta two"), 1);
        indexer.Finish();

        var provider = indexer.GetIndexedLinesProvider(
            new Dictionary<int, IEnumerable<string>> { [1] = new[] { "two" } });

        Assert.AreEqual(1, provider.Count);
    }

    [TestMethod]
    public void IndexKeyEqualityAndHash()
    {
        var meta = Meta();
        var key1 = TestHelpers.CreateIndexKey(meta, "alpha beta one", out _);
        var key2 = TestHelpers.CreateIndexKey(meta, "alpha beta one", out _);
        var key3 = TestHelpers.CreateIndexKey(meta, "gamma delta two", out _);

        Assert.AreEqual(key1, key2);
        Assert.AreEqual(key1.GetHashCode(), key2.GetHashCode());
        Assert.AreNotEqual(key1, key3);
        Assert.AreEqual(2, key1.ComponentCount);
        Assert.IsFalse(key1.HasLocalBuffer);
    }

    [TestMethod]
    public void IndexKeyComponentsAndToString()
    {
        var key = TestHelpers.CreateIndexKey(Meta(), "alpha beta one", out _);

        Assert.AreEqual("alpha", key.GetComponent(0).ToString());
        Assert.AreEqual("one", key.GetComponent(1).ToString());
        Assert.AreEqual("{alpha,one}", key.ToString());
    }

    [TestMethod]
    public void IndexKeyMakeLocalCopyIsSelfContained()
    {
        var key = TestHelpers.CreateIndexKey(Meta(), "alpha beta one", out _);
        var copy = key.MakeLocalCopy();

        Assert.IsTrue(copy.HasLocalBuffer);
        Assert.AreEqual(key, copy);
        Assert.AreEqual(key.GetHashCode(), copy.GetHashCode());
        Assert.AreEqual("alpha", copy.GetComponent(0).ToString());
    }

    [TestMethod]
    public void IndexKeyNumEqualityAndComparison()
    {
        var a = new IndexKeyNum { KeyNum = 1 };
        var b = new IndexKeyNum { KeyNum = 1 };
        var c = new IndexKeyNum { KeyNum = 2 };

        Assert.AreEqual(a, b);
        Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        Assert.AreNotEqual(a, c);
        Assert.IsTrue(a.CompareTo(c) < 0);
        Assert.IsTrue(c.CompareTo(a) > 0);
        Assert.IsTrue(a.CompareTo(b) == 0);
    }

    [TestMethod]
    public void CountIndexTracksSnapshots()
    {
        var indices = new Dictionary<IndexKeyNum, LogGrokX.Data.Index.Index>
            { [new IndexKeyNum { KeyNum = 1 }] = new LogGrokX.Data.Index.Index(4) };
        var countIndex = new CountIndex<LogGrokX.Data.Index.Index>(indices);
        var key = new IndexKeyNum { KeyNum = 1 };

        indices[key].Add(0);
        countIndex.Add(0, indices);
        Assert.AreEqual(1, countIndex.Counts.Count);

        countIndex.Finish(indices);
        Assert.AreEqual(1, countIndex.Counts.Count);
        Assert.AreEqual(1, countIndex.Counts[0].Single().Item2);
    }

    [TestMethod]
    public void UpdatableValueCachesAndRecomputes()
    {
        var source = 1;
        var computations = 0;
        var value = UpdatableValue.Create(() => source, i =>
        {
            computations++;
            return i * 10;
        });

        Assert.AreEqual(10, value.Value);
        Assert.AreEqual(10, value.Value);
        Assert.AreEqual(1, computations);

        source = 2;
        Assert.AreEqual(20, value.Value);
        Assert.AreEqual(2, computations);
    }

    [TestMethod]
    public void UpdatableValueMapAndIdentity()
    {
        var source = 3;
        var identity = UpdatableValue.Create(() => source);
        var mapped = identity.Map(i => i.ToString());

        Assert.AreEqual("3", mapped.Value);
        source = 4;
        Assert.AreEqual("4", mapped.Value);
    }

    [TestMethod]
    public void CollectionUtilsMergeSortedDisjoint()
    {
        var source = new[] { new[] { 0, 2, 4 }, new[] { 1, 3 }, new[] { 10 } };
        var result = CollectionUtils.MergeSorted(source).ToList();

        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4, 10 }, result);
    }

    [TestMethod]
    public void CollectionUtilsMergeSortedEmpty()
    {
        Assert.AreEqual(0, CollectionUtils.MergeSorted(Array.Empty<int[]>()).Count());
    }

    [TestMethod]
    public void CollectionUtilsMergeSortedGeneric()
    {
        IEnumerator<int>[] cursors =
        {
            new[] { 1, 2, 3 }.ToList().GetEnumerator(),
            new[] { 10, 11 }.ToList().GetEnumerator()
        };
        foreach (var cursor in cursors)
            cursor.MoveNext();

        var result = CollectionUtils.MergeSorted(cursors, (a, b) => b == a + 1).ToList();

        CollectionAssert.AreEqual(new[] { 1, 2, 3, 10, 11 }, result);
    }

    [TestMethod]
    public void ChunkedListAddsAcrossChunks()
    {
        var list = new ChunkedList<int>(2);
        for (var i = 0; i < 5; i++)
            list.Add(i);

        Assert.AreEqual(5, list.Count);
        Assert.AreEqual(0, list[0]);
        Assert.AreEqual(4, list[4]);

        var values = new List<int>();
        foreach (var value in list)
            values.Add(value);
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4 }, values);
    }

    [TestMethod]
    public void ChunkedListIndexOutOfRangeThrows()
    {
        var list = new ChunkedList<int>(2);
        list.Add(1);

        Assert.Throws<IndexOutOfRangeException>(() => { _ = list[5]; });

        var empty = new ChunkedList<int>(2);
        Assert.Throws<IndexOutOfRangeException>(() =>
        {
            using var enumerator = empty.GetEnumerator();
            enumerator.MoveNext();
        });
    }

    [TestMethod]
    public void ReaderWriterLockOwnersAreReentrant()
    {
        using var slim = new ReaderWriterLockSlim();
        using (slim.GetReadLockOwner())
        {
        }

        using (slim.GetUpgradableReadLockOwner())
        {
        }

        using (slim.GetWriteLockOwner())
        {
        }

        Assert.IsFalse(slim.IsReadLockHeld);
        Assert.IsFalse(slim.IsWriteLockHeld);
    }
}
