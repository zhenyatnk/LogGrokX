using System;
using LogGrokX.Data.Symbols;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Data.Tests.Symbols;

[TestClass]
public class CallStackParserTests
{
    private const string Message =
        "Unhandled exception\r\n" +
        "Call stack:\r\n" +
        " 0# test_.dll!38B3DC8D-3D1D-FADD-E67B-593D48ABEC14,1!86da0\r\n" +
        " 1# core.dll!5C1F0D2A-0B6E-4E57-9A43-0D1E8F2C3B4A,a!0x1f2\r\n" +
        " 2# kernel32.dll!14ad\r\n" +
        " 3# ntdll.dll+0x5aa58\r\n";

    [TestMethod]
    public void ParsesFramesWithPdbIdentity()
    {
        var frames = CallStackParser.Parse(Message);

        Assert.AreEqual(4, frames.Count);
        Assert.AreEqual(0, frames[0].Number);
        Assert.AreEqual("test_.dll", frames[0].Module);
        Assert.AreEqual(0x86da0UL, frames[0].Offset);
        Assert.AreEqual(new PdbIdentity("test_.pdb", Guid.Parse("38B3DC8D-3D1D-FADD-E67B-593D48ABEC14"), 1), frames[0].Pdb);
        Assert.AreEqual("38B3DC8D3D1DFADDE67B593D48ABEC141", frames[0].Pdb!.Index);
        Assert.AreEqual(10, frames[1].Pdb!.Age);
        Assert.AreEqual(0x1f2UL, frames[1].Offset);
    }

    [TestMethod]
    public void ParsesFramesWithoutPdbIdentity()
    {
        var frames = CallStackParser.Parse(Message);

        Assert.IsNull(frames[2].Pdb);
        Assert.AreEqual("kernel32.pdb", frames[2].PdbFileName);
        Assert.AreEqual(0x14adUL, frames[2].Offset);
        Assert.AreEqual("ntdll.dll", frames[3].Module);
        Assert.AreEqual(0x5aa58UL, frames[3].Offset);
    }

    [TestMethod]
    public void FrameRangeCoversFrameTextOnly()
    {
        var frame = CallStackParser.Parse(Message)[0];

        Assert.AreEqual("0# test_.dll!38B3DC8D-3D1D-FADD-E67B-593D48ABEC14,1!86da0",
            Message.Substring(frame.Start, frame.Length));
    }

    [TestMethod]
    [DataRow("Call stack: none")]
    [DataRow("Item #1 done")]
    [DataRow("0# test_.dll!not-an-offset")]
    [DataRow("prefix 0# test_.dll!86da0")]
    public void IgnoresTextWithoutFrames(string text)
    {
        Assert.IsFalse(CallStackParser.ContainsCallStack(text));
        Assert.AreEqual(0, CallStackParser.Parse(text).Count);
    }

    [TestMethod]
    public void DetectsCallStack()
    {
        Assert.IsTrue(CallStackParser.ContainsCallStack(Message));
    }

    [TestMethod]
    [DataRow("test_.dll", "test_.pdb")]
    [DataRow(@"C:\bin\App.exe", "App.pdb")]
    [DataRow("driver.sys", "driver.pdb")]
    public void PdbFileNameIsDerivedFromModule(string module, string expected)
    {
        Assert.AreEqual(expected, PdbIdentity.GetPdbFileName(module));
    }
}
