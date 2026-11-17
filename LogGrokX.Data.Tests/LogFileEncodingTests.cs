using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Data.Tests;

[TestClass]
public class LogFileEncodingTests
{
    [TestMethod]
    public void DetectsUtf8BomInSmallFile()
    {
        var path = WriteWithPreamble(Encoding.UTF8, "small");
        try
        {
            Assert.AreEqual(Encoding.UTF8, new LogFile(path, 0).Encoding);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void DetectsUtf32BomBeforeUtf16()
    {
        var path = WriteWithPreamble(Encoding.UTF32, "small");
        try
        {
            Assert.AreEqual(Encoding.UTF32, new LogFile(path, 0).Encoding);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void DetectsMaskedUtf8Bom()
    {
        var preamble = Encoding.UTF8.GetPreamble();
        var content = Encoding.UTF8.GetBytes("masked bom");
        var all = new byte[preamble.Length + content.Length];
        preamble.CopyTo(all, 0);
        content.CopyTo(all, preamble.Length);
        for (var i = 0; i < all.Length; i++)
            all[i] ^= 0x5A;

        var path = TestHelpers.WriteTempBytes(all);
        try
        {
            Assert.AreEqual(Encoding.UTF8, new LogFile(path, 0x5A).Encoding);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void DetectsBomlessUnicodeByCrlf()
    {
        var path = TestHelpers.WriteTempBytes(Encoding.Unicode.GetBytes("\u0100\r\n"));
        try
        {
            Assert.AreEqual(Encoding.Unicode, new LogFile(path, 0).Encoding);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void DetectsBomlessUnicodeWhenFileIsShorterThanProbe()
    {
        var content = string.Join("\r\n", Enumerable.Range(1, 5).Select(index =>
            $"2026-05-14 10:00:00.000 INF Thread-01 Enc [E0{index}] привет ü umlaut №{index}"));
        var path = TestHelpers.WriteTempBytes(Encoding.Unicode.GetBytes(content));
        try
        {
            Assert.AreEqual(Encoding.Unicode, new LogFile(path, 0).Encoding);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void DetectsBomlessUnicodeWithLineFeedsOnly()
    {
        var content = string.Join("\n", Enumerable.Range(1, 5).Select(index =>
            $"2026-05-14 10:00:00.000 INF Thread-01 Enc [E0{index}] привет ü umlaut №{index}"));
        var path = TestHelpers.WriteTempBytes(Encoding.Unicode.GetBytes(content));
        try
        {
            Assert.AreEqual(Encoding.Unicode, new LogFile(path, 0).Encoding);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void DetectsBomlessBigEndianByCrlf()
    {
        var path = TestHelpers.WriteTempBytes(Encoding.BigEndianUnicode.GetBytes("\u0100\r\n\u0101"));
        try
        {
            Assert.AreEqual(Encoding.BigEndianUnicode, new LogFile(path, 0).Encoding);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string WriteWithPreamble(Encoding encoding, string content)
    {
        var preamble = encoding.GetPreamble();
        var bytes = encoding.GetBytes(content);
        var all = new byte[preamble.Length + bytes.Length];
        preamble.CopyTo(all, 0);
        bytes.CopyTo(all, preamble.Length);
        return TestHelpers.WriteTempBytes(all);
    }
}
