using System;
using System.IO;
using LogGrokX.Data.Symbols;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Data.Tests.Symbols;

[TestClass]
public class SourcePathResolverTests
{
    [TestMethod]
    public void ParseRootsSkipsSourceServerToken()
    {
        CollectionAssert.AreEqual(new[] { @"C:\src", @"D:\other" },
            (System.Collections.ICollection)SourcePathResolver.ParseRoots(@"srv*; C:\src ;""D:\other"""));
        Assert.IsTrue(SourcePathResolver.UsesSourceServer(@"C:\src;srv*"));
        Assert.IsFalse(SourcePathResolver.UsesSourceServer(@"C:\src"));
        Assert.IsFalse(SourcePathResolver.UsesSourceServer(null));
        CollectionAssert.AreEqual(new[] { @"C:\src", @"D:\other" },
            (System.Collections.ICollection)SourcePathResolver.ParseRoots("C:\\src\r\nsrv*\r\n\r\nD:\\other\n"));
        Assert.IsTrue(SourcePathResolver.UsesSourceServer("C:\\src\r\nsrv*"));
    }

    [TestMethod]
    public void FindsFileByLongestPathSuffix()
    {
        var root = Path.Combine(Path.GetTempPath(), "LogGrokX.SourcePathResolverTests." + Guid.NewGuid().ToString("N"));
        var expected = Path.Combine(root, "Lib", "File.cpp");
        Directory.CreateDirectory(Path.GetDirectoryName(expected)!);
        File.WriteAllText(expected, "int main() {}");
        File.WriteAllText(Path.Combine(root, "File.cpp"), "wrong");
        try
        {
            Assert.AreEqual(expected, SourcePathResolver.Find(@"D:\a\1\s\Lib\File.cpp", new[] { root }));
            Assert.IsNull(SourcePathResolver.Find(@"D:\a\1\s\Lib\Missing.cpp", new[] { root }));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
