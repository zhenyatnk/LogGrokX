using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Tests;

[TestClass]
public sealed class BulkSelectionTests
{
    [TestMethod]
    public async Task CopyUsesBoundedWorkAndPreservesLineFormatting()
    {
        var clipboard = string.Empty;

        var result = await BulkSelection.CopyAsync(new[] { "one  ", "two\0" },
            text => clipboard = text, CancellationToken.None);

        Assert.AreEqual(BulkCopyResult.Copied, result);
        Assert.AreEqual("one\r\ntwo\r\n", clipboard);
    }

    [TestMethod]
    public async Task OversizedClipboardTextIsRejectedBeforeClipboardAccess()
    {
        var clipboardCalls = 0;
        var lines = new[] { new string('x', BulkSelection.MaxClipboardCharacters) };

        var result = await BulkSelection.CopyAsync(lines, _ => clipboardCalls++, CancellationToken.None);

        Assert.AreEqual(BulkCopyResult.TooLarge, result);
        Assert.AreEqual(0, clipboardCalls);
    }

    [TestMethod]
    public async Task ProcessingReportsCompletedBatches()
    {
        var reports = new List<long>();

        await BulkSelection.ProcessAsync(Enumerable.Range(0, 300), _ => { }, CancellationToken.None,
            reports.Add);

        CollectionAssert.AreEqual(new long[] { 128, 256 }, reports);
    }

    [TestMethod]
    public async Task SavingWritesAllLinesInBatches()
    {
        var path = Path.Combine(Path.GetTempPath(), $"loggrokx-selection-{Guid.NewGuid():N}.txt");
        try
        {
            await BulkSelection.SaveAsync(
                Enumerable.Range(0, 300).Select(index => $"line {index}  "), path, CancellationToken.None);

            var lines = File.ReadAllLines(path);
            Assert.AreEqual(300, lines.Length);
            Assert.AreEqual("line 0", lines[0]);
            Assert.AreEqual("line 299", lines[^1]);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
