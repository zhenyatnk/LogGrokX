using System;
using System.Collections.Generic;
using System.Linq;
using LogGrokX.Data.IndexTree;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Data.Tests;

[TestClass]
public class IndexTreeLeafTests
{
    [TestMethod]
    public void SimpleLeafStoresValuesAndChainsWhenFull()
    {
        var first = new SimpleLeaf<int>(0, 0);
        var current = first;
        for (var i = 1; i <= 1024; i++)
        {
            var next = current.Add(i, i);
            if (next != null)
                current = next;
        }

        Assert.IsNotNull(first.Next);
        Assert.AreEqual(1, current.Count);
        Assert.AreEqual(1024, first.Count);
        Assert.AreEqual(1024, current.MinIndex);
    }

    [TestMethod]
    public void SimpleLeafEnumeratesFromIndex()
    {
        var leaf = new SimpleLeaf<int>(10, 0);
        leaf.Add(11, 1);
        leaf.Add(12, 2);

        CollectionAssert.AreEqual(new[] { 10, 11, 12 }, leaf.GetEnumerableFromIndex(0).ToList());
        CollectionAssert.AreEqual(new[] { 11, 12 }, leaf.GetEnumerableFromIndex(1).ToList());
        CollectionAssert.AreEqual(new[] { 10, 11, 12 }, leaf.GetEnumerableFromIndex(-5).ToList());
    }

    [TestMethod]
    public void SimpleLeafGetValueAndFindByValue()
    {
        var leaf = new SimpleLeaf<int>(10, 0);
        leaf.Add(11, 1);
        leaf.Add(12, 2);

        Assert.AreEqual(10, leaf.FirstValue);
        Assert.AreEqual(0, leaf.MinIndex);
        Assert.AreEqual(3, leaf.Count);
        Assert.AreEqual(11, leaf.GetValue(1));
        Assert.AreEqual(10, leaf.GetValue(-1));

        var (index, foundLeaf) = leaf.FindByValue(12);
        Assert.AreEqual(2, index);
        Assert.AreSame(leaf, foundLeaf);
    }

    [TestMethod]
    public void SimpleLeafEnumeratorYieldsValues()
    {
        var leaf = new SimpleLeaf<int>(1, 0);
        leaf.Add(2, 1);

        var values = new List<int>();
        foreach (var value in leaf)
            values.Add(value);

        CollectionAssert.AreEqual(new[] { 1, 2 }, values);
    }

    [TestMethod]
    public void SimpleLeafPreservesWideGapsAndSeeksAcrossCheckpoints()
    {
        var tree = new IndexTree<int, SimpleLeaf<int>>(16, value => new SimpleLeaf<int>(value, 0));
        var values = Enumerable.Range(0, 1059).Select(index => index switch
        {
            < 17 => int.MinValue + index * 5,
            < 31 => int.MinValue + 300 + index * 5,
            < 63 => int.MinValue + 70_000 + index * 5,
            _ => int.MaxValue - 10_000 + index * 5
        }).ToArray();

        foreach (var value in values)
            tree.Add(value);

        for (var index = 0; index < values.Length; index++)
        {
            Assert.AreEqual(values[index], tree[index]);
            Assert.AreEqual(index, tree.FindIndexByValue(values[index]));
        }

        Assert.AreEqual(17, tree.FindIndexByValue(values[16] + 1));
        Assert.AreEqual(63, tree.FindIndexByValue(0));
        Assert.AreEqual(values.Length, tree.FindIndexByValue(int.MaxValue));
        CollectionAssert.AreEqual(values[1000..], tree.GetEnumerableFromIndex(1000).ToArray());
    }

    [TestMethod]
    public void LongsLeafStoresDeltas()
    {
        var leaf = new LongsLeaf(1000, 0);
        leaf.Add(1001, 1);
        leaf.Add(1005, 2);

        Assert.AreEqual(1000, leaf.FirstValue);
        Assert.AreEqual(0, leaf.MinIndex);
        Assert.AreEqual(3, leaf.Count);
        Assert.AreEqual(1000, leaf[0]);
        Assert.AreEqual(1005, leaf[2]);
        Assert.AreEqual(1001, leaf.GetValue(1));
        Assert.IsNull(leaf.Next);
    }

    [TestMethod]
    public void LongsLeafEnumeratesFromIndex()
    {
        var leaf = new LongsLeaf(1000, 0);
        leaf.Add(1001, 1);
        leaf.Add(1005, 2);

        CollectionAssert.AreEqual(new[] { 1000L, 1001L, 1005L }, leaf.GetEnumerableFromIndex(0).ToList());
        CollectionAssert.AreEqual(new[] { 1005L }, leaf.GetEnumerableFromIndex(2).ToList());
    }

    [TestMethod]
    public void LongsLeafEnumeratorYieldsValues()
    {
        var leaf = new LongsLeaf(1000, 0);
        leaf.Add(1001, 1);

        var values = new List<long>();
        foreach (var value in leaf)
            values.Add(value);

        CollectionAssert.AreEqual(new[] { 1000L, 1001L }, values);
    }

    [TestMethod]
    public void IndexTreeGetEnumerableFromValueAndFindIndex()
    {
        var tree = new IndexTree<int, TestIndexTreeLeaf>(16, i => new TestIndexTreeLeaf(i, 0));
        foreach (var value in Enumerable.Range(0, 100))
            tree.Add(value);

        CollectionAssert.AreEqual(Enumerable.Range(50, 50).ToList(), tree.GetEnumerableFromValue(50).ToList());
        Assert.AreEqual(50, tree.FindIndexByValue(50));
    }

    [TestMethod]
    public void IndexTreeEmptyBehaviour()
    {
        var tree = new IndexTree<int, TestIndexTreeLeaf>(16, i => new TestIndexTreeLeaf(i, 0));

        Assert.AreEqual(0, tree.Count);
        Assert.AreEqual(0, tree.GetEnumerableFromIndex(0).Count());
        Assert.AreEqual(0, tree.GetEnumerableFromValue(0).Count());
        Assert.AreEqual(0, tree.FindIndexByValue(0));
        Assert.Throws<InvalidOperationException>(() => { _ = tree[0]; });
    }

    [TestMethod]
    public void LeafOrNodeExtensionsReturnValueIndex()
    {
        var leaf = new TestIndexTreeLeaf(0, 0);
        var current = leaf;
        for (var i = 1; i < 10; i++)
        {
            var next = current.Add(i, i);
            if (next != null)
                current = next;
        }

        Assert.AreEqual(1, leaf.GetIndexByValue(1));
        CollectionAssert.AreEqual(Enumerable.Range(1, 9).ToList(), leaf.GetEnumerableFromValue(1).ToList());
    }
}
