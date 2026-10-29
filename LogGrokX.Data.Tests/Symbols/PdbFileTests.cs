using System;
using System.IO;
using System.Linq;
using System.Text;
using LogGrokX.Data.Symbols;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Data.Tests.Symbols;

[TestClass]
public class PdbFileTests
{
    private static readonly Guid Signature = Guid.Parse("38B3DC8D-3D1D-FADD-E67B-593D48ABEC14");

    [TestMethod]
    public void OpenReadsIdentityFromInfoAndDbiStreams()
    {
        using var stream = new MemoryStream(PdbBuilder.Build(Signature, 2, dbiAge: 1));

        var pdb = PdbFile.Open(stream);

        Assert.IsNotNull(pdb);
        Assert.AreEqual(Signature, pdb.Signature);
        Assert.AreEqual(2, pdb.Age);
        Assert.AreEqual(1, pdb.DbiAge);
        Assert.IsTrue(pdb.Matches(new PdbIdentity("test_.pdb", Signature, 1)));
        Assert.IsFalse(pdb.Matches(new PdbIdentity("test_.pdb", Guid.NewGuid(), 1)));
    }

    [TestMethod]
    public void OpenReturnsNullForNonMsfFile()
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes(new string('x', 100)));

        Assert.IsNull(PdbFile.Open(stream));
    }

    [TestMethod]
    public void ReadsSourceServerStreamSpanningSeveralBlocks()
    {
        var content = "SRCSRV: ini ------------------------------------------------\r\n" +
                      string.Concat(Enumerable.Repeat("VERSION=2\r\n", 100));
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdb");
        File.WriteAllBytes(path, PdbBuilder.Build(Signature, 1, sourceServer: content));
        try
        {
            Assert.AreEqual(content, PdbFile.ReadSourceServerStream(path));
            Assert.IsTrue(PdbFile.TryReadIdentity(path, out var signature, out var age, out var dbiAge));
            Assert.AreEqual(Signature, signature);
            Assert.AreEqual(1, age);
            Assert.AreEqual(1, dbiAge);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void ReadSourceServerStreamReturnsNullWithoutStream()
    {
        using var stream = new MemoryStream(PdbBuilder.Build(Signature, 1));

        Assert.IsNull(PdbFile.Open(stream)!.ReadNamedStream("srcsrv"));
    }

    [TestMethod]
    public void TryReadIdentityFailsForCorruptedFile()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdb");
        File.WriteAllBytes(path, PdbBuilder.Build(Signature, 1)[..600]);
        try
        {
            Assert.IsFalse(PdbFile.TryReadIdentity(path, out _, out _, out _));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
