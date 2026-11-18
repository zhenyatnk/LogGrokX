using System;
using LogGrokX.Data.Index;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Data.Tests;

[TestClass]
public class LineKeyIndexTests
{
    [TestMethod]
    public void WidensKeysAndPreservesPreviousChunks()
    {
        var values = new int[16386];
        for (var index = 0; index < values.Length; index++)
            values[index] = index % 100;
        values[16380] = byte.MaxValue + 1;
        values[16382] = ushort.MaxValue + 1;
        values[16383] = int.MaxValue;
        values[16385] = byte.MaxValue + 1;

        var lineKeys = new LineKeyIndex();
        foreach (var value in values)
            lineKeys.Add(new IndexKeyNum { KeyNum = value });

        Assert.AreEqual(values.Length, lineKeys.Count);
        for (var index = 0; index < values.Length; index++)
            Assert.AreEqual(values[index], lineKeys[index].KeyNum);

        Assert.Throws<IndexOutOfRangeException>(() => _ = lineKeys[values.Length]);
    }
}
