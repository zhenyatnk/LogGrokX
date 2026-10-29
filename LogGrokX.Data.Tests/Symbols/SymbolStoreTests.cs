using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LogGrokX.Data.Symbols;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Data.Tests.Symbols;

[TestClass]
public class SymbolStoreTests
{
    private static readonly Guid Signature = Guid.Parse("38B3DC8D-3D1D-FADD-E67B-593D48ABEC14");
    private static readonly PdbIdentity Pdb = new("test_.pdb", Signature, 1);
    private string _root = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "LogGrokX.SymbolStoreTests." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_root, recursive: true);

    [TestMethod]
    public async Task FindsPdbInSymbolStoreLayout()
    {
        var expected = Write(Path.Combine("store", "test_.pdb", Pdb.Index, "test_.pdb"), PdbBuilder.Build(Signature, 1));
        var store = new SymbolStore(SymbolPath.Parse(Path.Combine(_root, "store"), _root), null);

        var result = await store.FindAsync("test_.pdb", Pdb, CancellationToken.None);

        Assert.AreEqual(expected, result.Path);
    }

    [TestMethod]
    public async Task FlatPdbMustMatchIdentity()
    {
        Write(Path.Combine("flat", "test_.pdb"), PdbBuilder.Build(Guid.NewGuid(), 1));
        var store = new SymbolStore(SymbolPath.Parse(Path.Combine(_root, "flat"), _root), null);

        var result = await store.FindAsync("test_.pdb", Pdb, CancellationToken.None);

        Assert.IsNull(result.Path);
        Assert.IsTrue(result.Attempts.Count > 0);
    }

    [TestMethod]
    public async Task FlatPdbWithoutIdentityIsAccepted()
    {
        var expected = Write(Path.Combine("flat", "kernel32.pdb"), new byte[] { 1 });
        var store = new SymbolStore(SymbolPath.Parse(Path.Combine(_root, "flat"), _root), null);

        var result = await store.FindAsync("kernel32.pdb", null, CancellationToken.None);

        Assert.AreEqual(expected, result.Path);
    }

    [TestMethod]
    public async Task DownloadsPdbFromHttpServerIntoCache()
    {
        var content = PdbBuilder.Build(Signature, 1);
        var handler = new StubHandler(request => request.RequestUri!.AbsoluteUri ==
                                                 $"https://symbols.example.com/test_.pdb/{Pdb.Index}/test_.pdb"
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) }
            : new HttpResponseMessage(HttpStatusCode.NotFound));
        var cache = Path.Combine(_root, "cache");
        var store = new SymbolStore(SymbolPath.Parse($"srv*{cache}*https://symbols.example.com", _root), new HttpClient(handler));

        var result = await store.FindAsync("test_.pdb", Pdb, CancellationToken.None);

        var expected = Path.Combine(cache, "test_.pdb", Pdb.Index, "test_.pdb");
        Assert.AreEqual(expected, result.Path);
        CollectionAssert.AreEqual(content, File.ReadAllBytes(expected));

        var cached = await new SymbolStore(SymbolPath.Parse($"srv*{cache}*https://symbols.example.com", _root), null)
            .FindAsync("test_.pdb", Pdb, CancellationToken.None);
        Assert.AreEqual(expected, cached.Path);
    }

    [TestMethod]
    public async Task FollowsFilePointerOnHttpServer()
    {
        var target = Write(Path.Combine("share", "test_.pdb"), PdbBuilder.Build(Signature, 1));
        var requests = new List<string>();
        var handler = new StubHandler(request =>
        {
            requests.Add(request.RequestUri!.AbsolutePath);
            return request.RequestUri.AbsolutePath.EndsWith("/file.ptr", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("PATH:" + target) }
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var store = new SymbolStore(SymbolPath.Parse($"srv*{Path.Combine(_root, "cache")}*https://symbols.example.com", _root),
            new HttpClient(handler));

        var result = await store.FindAsync("test_.pdb", Pdb, CancellationToken.None);

        Assert.AreEqual(target, result.Path);
        Assert.AreEqual(2, requests.Count);
    }

    [TestMethod]
    public async Task ServerErrorsAreReportedInAttempts()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("offline"));
        var store = new SymbolStore(SymbolPath.Parse($"srv*{Path.Combine(_root, "cache")}*https://symbols.example.com", _root),
            new HttpClient(handler));

        var result = await store.FindAsync("test_.pdb", Pdb, CancellationToken.None);

        Assert.IsNull(result.Path);
        Assert.IsTrue(result.Attempts[^1].EndsWith("offline", StringComparison.Ordinal), result.Attempts[^1]);
    }

    private string Write(string relative, byte[] content)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        return path;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
