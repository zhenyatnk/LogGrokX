using System;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Data.Tests;

[TestClass]
public class Base64DetectorTests
{
    private static string Encode(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

    private static string EncodeUrlSafe(string text) =>
        Encode(text).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [TestMethod]
    public void WholeTextIsDecoded()
    {
        Assert.IsTrue(Base64Detector.TryDecode(Encode("Hello, World!"), out var decoded));
        Assert.AreEqual("Hello, World!", decoded);
    }

    [TestMethod]
    public void QuotedWholeTextIsDecodedAndSurroundingIsKept()
    {
        var source = $"  \"{Encode("{\"a\":1}")}\"  ";

        Assert.IsTrue(Base64Detector.TryDecode(source, out var decoded));
        Assert.AreEqual("  \"{\"a\":1}\"  ", decoded);
    }

    [TestMethod]
    public void ShortWholeTextIsDecoded()
    {
        Assert.IsTrue(Base64Detector.TryDecode(Encode("test"), out var decoded));
        Assert.AreEqual("test", decoded);
    }

    [TestMethod]
    public void FragmentInsideMessageIsDecoded()
    {
        var payload = "{\"user\":\"admin\",\"id\":42}";
        var source = $"Request payload={Encode(payload)} accepted";

        Assert.IsTrue(Base64Detector.TryDecode(source, out var decoded));
        Assert.AreEqual($"Request payload={payload} accepted", decoded);
    }

    [TestMethod]
    public void DocumentedExampleIsDecoded()
    {
        Assert.IsTrue(Base64Detector.TryDecode("payload=eyJpZCI6NDIsIm9rIjp0cnVlfQ== accepted", out var decoded));
        Assert.AreEqual("payload={\"id\":42,\"ok\":true} accepted", decoded);
    }

    [TestMethod]
    public void ShortFragmentIsNotDecoded()
    {
        Assert.IsFalse(Base64Detector.TryDecode("id=eyJpZCI6NDJ9 accepted", out _));
    }

    [TestMethod]
    public void SeveralFragmentsAreDecoded()
    {
        var source = $"a={Encode("first fragment")}; b={Encode("second fragment")}";

        Assert.IsTrue(Base64Detector.TryDecode(source, out var decoded));
        Assert.AreEqual("a=first fragment; b=second fragment", decoded);
    }

    [TestMethod]
    public void UrlSafeUnpaddedFragmentIsDecoded()
    {
        var header = "{\"alg\":\"HS256\",\"typ\":\"JWT\"}";
        var payload = "{\"sub\":\"1234567890\",\"name\":\"John Doe?\"}";
        var signature = Convert.ToBase64String(new byte[] { 0xFF, 0x00, 0x81, 0x9C, 0xFE, 0x01, 0x02, 0x03, 0xF0, 0x80, 0x90, 0xAA })
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var source = $"Bearer {EncodeUrlSafe(header)}.{EncodeUrlSafe(payload)}.{signature}";

        Assert.IsTrue(Base64Detector.TryDecode(source, out var decoded));
        Assert.AreEqual($"Bearer {header}.{payload}.{signature}", decoded);
    }

    [TestMethod]
    public void BinaryContentIsNotDetected()
    {
        var binary = Convert.ToBase64String(new byte[] { 0x00, 0x01, 0x02, 0xFF, 0xFE, 0x80, 0x10, 0x20, 0x30, 0x40, 0x50, 0x60 });

        Assert.IsFalse(Base64Detector.TryDecode(binary, out _));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("Information")]
    [DataRow("Database")]
    [DataRow("Warning")]
    [DataRow("0x00001a2b")]
    [DataRow("12345678")]
    [DataRow("1234567890123456")]
    [DataRow("2024-01-01 12:00:00.123")]
    [DataRow("3f2504e0-4f89-11d3-9a0c-0305e82c3301")]
    [DataRow("deadbeefdeadbeefdeadbeefdeadbeef")]
    [DataRow("C:/Users/zhenya/AppData/Local/LogGrokX/logs")]
    [DataRow("AbstractSingletonProxyFactoryBean")]
    [DataRow("Connection to server established successfully")]
    [DataRow("LogGrokX.Data.Base64DetectorTests.SomeVeryLongMethodName")]
    public void PlainTextIsNotDetected(string source)
    {
        Assert.IsFalse(Base64Detector.TryDecode(source, out _), source);
    }

    [TestMethod]
    public void MixedAlphabetIsRejected()
    {
        Assert.IsFalse(Base64Detector.TryDecodeToken("SGVsbG8-V29y/GQ=", out _));
    }

    [TestMethod]
    public void InvalidPaddingIsRejected()
    {
        Assert.IsFalse(Base64Detector.TryDecodeToken("SGVsbG8===", out _));
        Assert.IsFalse(Base64Detector.TryDecodeToken("SGVsbG8gV29ybGQ=x", out _));
    }

    [TestMethod]
    public void TooLongSourceIsSkipped()
    {
        var source = Encode(new string('a', Base64Detector.MaxSourceLength));

        Assert.IsFalse(Base64Detector.TryDecode(source, out _));
    }
}
