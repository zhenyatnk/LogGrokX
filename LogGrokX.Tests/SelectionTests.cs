using System.Linq;
using LogGrokX.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Tests;

[TestClass]
public sealed class SelectionTests
{
    [TestMethod]
    public void HugeRangeUsesConstantStorageAndSupportsLookupWithoutEnumeration()
    {
        var selection = new Selection();
        selection.Set(0);
        selection.AddRangeToValue(13_662_285);

        Assert.AreEqual(13_662_286L, selection.Count);
        Assert.AreEqual((0, 13_662_285), selection.Bounds!.Value);
        Assert.IsTrue(selection.Contains(0));
        Assert.IsTrue(selection.Contains(6_000_000));
        Assert.IsTrue(selection.Contains(13_662_285));
        Assert.IsFalse(selection.Contains(13_662_286));
    }

    [TestMethod]
    public void AdjacentRangesMergeAndRemovingAnIndexSplitsThem()
    {
        var selection = new Selection();
        selection.Set(5);
        selection.AddRangeToValue(10);
        selection.Add(2);
        selection.Add(3);
        selection.Add(4);
        selection.Add(20);
        selection.AddRangeToValue(25);

        CollectionAssert.AreEqual(
            new[] { 2, 3, 4, 5, 6, 7, 8, 9, 10, 20, 21, 22, 23, 24, 25 },
            selection.ToArray());

        selection.Remove(5);
        CollectionAssert.AreEqual(
            new[] { 2, 3, 4, 6, 7, 8, 9, 10, 20, 21, 22, 23, 24, 25 },
            selection.ToArray());
    }

    [TestMethod]
    public void UnchangedSelectionDoesNotRaiseChangedAgain()
    {
        var selection = new Selection();
        var changes = 0;
        selection.Changed += () => changes++;

        selection.Set(1);
        selection.Add(1);
        selection.AddRangeToValue(3);
        selection.Add(2);
        selection.Set(3);
        selection.Set(3);
        selection.Clear();
        selection.Clear();

        Assert.AreEqual(4, changes);
    }

    [TestMethod]
    public void BulkUpdatePublishesOneChangeOnDispose()
    {
        var selection = new Selection();
        var changes = 0;
        selection.Changed += () => changes++;

        using (selection.BeginBulkUpdate())
        {
            selection.Set(1);
            selection.AddRangeToValue(1000);
            selection.Remove(500);
            Assert.AreEqual(0, changes);
        }

        Assert.AreEqual(1, changes);
    }

    [TestMethod]
    public void SnapshotDoesNotChangeWithLaterSelectionUpdates()
    {
        var selection = new Selection();
        selection.Set(1);
        selection.AddRangeToValue(3);
        var snapshot = selection.Snapshot();

        selection.Clear();
        selection.Add(10);

        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, snapshot.ToArray());
    }
}
