using System.Linq;
using LogGrokX.Data.Symbols;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Data.Tests.Symbols;

[TestClass]
public class SymbolPathTests
{
    private const string DefaultCache = @"D:\cache";

    [TestMethod]
    public void ParsesServerWithCache()
    {
        var path = SymbolPath.Parse(@"srv*C:\Symbols*https://msdl.microsoft.com/download/symbols", DefaultCache);

        var server = (SymbolServer)path.Elements.Single();
        Assert.AreEqual("https://msdl.microsoft.com/download/symbols", server.Server);
        CollectionAssert.AreEqual(new[] { @"C:\Symbols" }, server.Caches.ToArray());
        Assert.IsTrue(server.IsHttp);
    }

    [TestMethod]
    public void SrvWithoutServerUsesMicrosoftServerAndDefaultCache()
    {
        var server = (SymbolServer)SymbolPath.Parse("srv*", DefaultCache).Elements.Single();

        Assert.AreEqual(SymbolPath.MicrosoftSymbolServer, server.Server);
        CollectionAssert.AreEqual(new[] { DefaultCache }, server.Caches.ToArray());
    }

    [TestMethod]
    public void ParsesDirectoriesAndCacheElements()
    {
        var path = SymbolPath.Parse(@"C:\Build\Symbols; cache*E:\cache ;srv*\\share\symbols;symsrv*symsrv.dll*https://symbols.example.com", DefaultCache);

        Assert.AreEqual(3, path.Elements.Count);
        Assert.AreEqual(new SymbolDirectory(@"C:\Build\Symbols"), path.Elements[0]);
        var share = (SymbolServer)path.Elements[1];
        Assert.AreEqual(@"\\share\symbols", share.Server);
        Assert.IsFalse(share.IsHttp);
        CollectionAssert.AreEqual(new[] { @"E:\cache" }, share.Caches.ToArray());
        var http = (SymbolServer)path.Elements[2];
        Assert.AreEqual("https://symbols.example.com", http.Server);
        CollectionAssert.AreEqual(new[] { @"E:\cache" }, http.Caches.ToArray());
    }

    [TestMethod]
    public void ElementsCanBeWrittenOnSeparateLines()
    {
        var path = SymbolPath.Parse("srv*C:\\Symbols*https://msdl.microsoft.com/download/symbols\r\n\r\n  C:\\Build\nsrv*\\\\share\\symbols", DefaultCache);

        Assert.AreEqual(3, path.Elements.Count);
        Assert.AreEqual("https://msdl.microsoft.com/download/symbols", ((SymbolServer)path.Elements[0]).Server);
        Assert.AreEqual(new SymbolDirectory(@"C:\Build"), path.Elements[1]);
        Assert.AreEqual(@"\\share\symbols", ((SymbolServer)path.Elements[2]).Server);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow(" ; ")]
    public void EmptyPathHasNoElements(string? text)
    {
        Assert.AreEqual(0, SymbolPath.Parse(text, DefaultCache).Elements.Count);
    }
}
