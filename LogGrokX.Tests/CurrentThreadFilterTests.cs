using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Tests;

[TestClass]
public class CurrentThreadFilterTests
{
    private static readonly IReadOnlyDictionary<int, IEnumerable<string>> NoExclusions =
        new Dictionary<int, IEnumerable<string>>();

    [TestMethod]
    public void SetThreadRaisesChangedOnlyOnChange()
    {
        var filter = new CurrentThreadFilter();
        var raised = 0;
        filter.Changed += () => raised++;

        filter.SetThread("T1");
        filter.SetThread("T1");
        filter.SetThread("T2");
        filter.SetThread(null);
        filter.SetThread(null);

        Assert.AreEqual(3, raised);
        Assert.IsNull(filter.Thread);
    }

    [TestMethod]
    public void AddThreadExclusionsExcludesOtherThreads()
    {
        var result = CurrentThreadFilter.AddThreadExclusions(NoExclusions, 1, "T1", new[] { "T1", "T2", "T3" });

        CollectionAssert.AreEquivalent(new[] { "T2", "T3" }, result[1].ToList());
    }

    [TestMethod]
    public void AddThreadExclusionsKeepsExistingExclusions()
    {
        var exclusions = new Dictionary<int, IEnumerable<string>>
        {
            [0] = new[] { "DEBUG" },
            [1] = new[] { "T3" }
        };

        var result = CurrentThreadFilter.AddThreadExclusions(exclusions, 1, "T1", new[] { "T1", "T2", "T3" });

        CollectionAssert.AreEquivalent(new[] { "DEBUG" }, result[0].ToList());
        CollectionAssert.AreEquivalent(new[] { "T2", "T3" }, result[1].ToList());
        CollectionAssert.AreEquivalent(new[] { "T3" }, exclusions[1].ToList());
    }

    [TestMethod]
    public void AddThreadExclusionsReturnsSourceWithoutThread()
    {
        var exclusions = new Dictionary<int, IEnumerable<string>> { [0] = new[] { "DEBUG" } };

        Assert.AreSame(exclusions, CurrentThreadFilter.AddThreadExclusions(exclusions, 1, null, new[] { "T1" }));
        Assert.AreSame(exclusions, CurrentThreadFilter.AddThreadExclusions(exclusions, -1, "T1", new[] { "T1", "T2" }));
        Assert.AreSame(exclusions, CurrentThreadFilter.AddThreadExclusions(exclusions, 1, "T1", new[] { "T1" }));
    }

    [TestMethod]
    public void AddThreadExclusionsIsCaseSensitive()
    {
        var result = CurrentThreadFilter.AddThreadExclusions(NoExclusions, 0, "Worker", new[] { "Worker", "worker" });

        CollectionAssert.AreEquivalent(new[] { "worker" }, result[0].ToList());
    }
}
