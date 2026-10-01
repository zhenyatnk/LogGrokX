using System;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
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

    private static string WrapPem(string label, byte[] data, string newLine = "\n")
    {
        var body = Convert.ToBase64String(data);
        var lines = Enumerable.Range(0, (body.Length + 63) / 64)
            .Select(i => body.Substring(i * 64, Math.Min(64, body.Length - i * 64)));
        return $"-----BEGIN {label}-----{newLine}{string.Join(newLine, lines)}{newLine}-----END {label}-----";
    }

    private static X509Certificate2 CreateCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=loggrokx.test, O=LogGrokX", rsa,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("loggrokx.test");
        san.AddDnsName("www.loggrokx.test");
        request.CertificateExtensions.Add(san.Build());
        return request.CreateSelfSigned(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
            new DateTimeOffset(2027, 1, 2, 3, 4, 5, TimeSpan.Zero));
    }

    [TestMethod]
    public void MultiLinePemCertificateIsDescribed()
    {
        using var certificate = CreateCertificate();
        var pem = certificate.ExportCertificatePem();
        var source = $"TLS handshake, server certificate:\n{pem}\nverified";

        Assert.IsTrue(Base64Detector.TryDecode(source, out var decoded));

        StringAssert.StartsWith(decoded, "TLS handshake, server certificate:\n-----BEGIN CERTIFICATE-----\n");
        StringAssert.EndsWith(decoded, "-----END CERTIFICATE-----\nverified");
        StringAssert.Contains(decoded, "Subject: CN=loggrokx.test, O=LogGrokX");
        StringAssert.Contains(decoded, "Not before: 2026-01-02 03:04:05 UTC");
        StringAssert.Contains(decoded, "Not after: 2027-01-02 03:04:05 UTC");
        StringAssert.Contains(decoded, $"Thumbprint (SHA-1): {certificate.Thumbprint}");
        StringAssert.Contains(decoded, "Public key: RSA 2048 bits");
        StringAssert.Contains(decoded, "Subject alternative names: loggrokx.test, www.loggrokx.test");
        Assert.IsFalse(decoded.Contains(pem.Split('\n')[1]), decoded);
    }

    [TestMethod]
    public void PemWithCrLfLineBreaksIsDecoded()
    {
        using var certificate = CreateCertificate();
        var source = WrapPem("CERTIFICATE", certificate.RawData, "\r\n");

        Assert.IsTrue(Base64Detector.TryDecode(source, out var decoded));
        StringAssert.Contains(decoded, $"Thumbprint (SHA-1): {certificate.Thumbprint}");
    }

    [TestMethod]
    public void PemWithEscapedLineBreaksInJsonIsDecoded()
    {
        using var certificate = CreateCertificate();
        var escaped = WrapPem("CERTIFICATE", certificate.RawData, "\\n");
        var source = $"{{\"certificate\":\"{escaped}\"}}";

        Assert.IsTrue(Base64Detector.TryDecode(source, out var decoded));
        StringAssert.StartsWith(decoded, "{\"certificate\":\"-----BEGIN CERTIFICATE-----\n");
        StringAssert.Contains(decoded, "Subject: CN=loggrokx.test, O=LogGrokX");
        StringAssert.EndsWith(decoded, "-----END CERTIFICATE-----\"}");
    }

    [TestMethod]
    public void PemWithTextPayloadShowsText()
    {
        var text = string.Join("\n", Enumerable.Range(1, 5).Select(i => $"Readable line number {i} of the message"));
        var source = WrapPem("MESSAGE", Encoding.UTF8.GetBytes(text));

        Assert.IsTrue(Base64Detector.TryDecode(source, out var decoded));
        Assert.AreEqual($"-----BEGIN MESSAGE-----\n{text}\n-----END MESSAGE-----", decoded);
    }

    [TestMethod]
    public void BinaryPemIsShownAsHexDump()
    {
        var data = Enumerable.Range(0, 40).Select(i => (byte)i).ToArray();
        var source = WrapPem("PRIVATE KEY", data);

        Assert.IsTrue(Base64Detector.TryDecode(source, out var decoded));
        var expected = "-----BEGIN PRIVATE KEY-----\n" +
                       "00000000  00 01 02 03 04 05 06 07  08 09 0a 0b 0c 0d 0e 0f  |................|\n" +
                       "00000010  10 11 12 13 14 15 16 17  18 19 1a 1b 1c 1d 1e 1f  |................|\n" +
                       "00000020  20 21 22 23 24 25 26 27                           | !\"#$%&'|\n" +
                       "-----END PRIVATE KEY-----";
        Assert.AreEqual(expected, decoded);
    }

    [TestMethod]
    public void HexDumpIsLimited()
    {
        var dump = Base64Detector.FormatHexDump(new byte[Base64Detector.MaxHexDumpBytes + 100]);

        StringAssert.EndsWith(dump, "... 100 more bytes\n");
        Assert.AreEqual(Base64Detector.MaxHexDumpBytes / 16 + 1, dump.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [TestMethod]
    public void PemAndFragmentInSameTextAreDecoded()
    {
        using var certificate = CreateCertificate();
        var source = $"token={Encode("first fragment")}\n{certificate.ExportCertificatePem()}";

        Assert.IsTrue(Base64Detector.TryDecode(source, out var decoded));
        StringAssert.StartsWith(decoded, "token=first fragment\n-----BEGIN CERTIFICATE-----\n");
        StringAssert.Contains(decoded, "Subject: CN=loggrokx.test, O=LogGrokX");
    }

    [TestMethod]
    public void PemWithMismatchedLabelsIsNotDecoded()
    {
        var source = WrapPem("CERTIFICATE", new byte[] { 1, 2, 3 }).Replace("END CERTIFICATE", "END PRIVATE KEY");

        Assert.IsFalse(Base64Detector.TryDecode(source, out _));
    }

    [TestMethod]
    public void BrokenPemBodyIsNotDecoded()
    {
        Assert.IsFalse(Base64Detector.TryDecode("-----BEGIN CERTIFICATE-----\nMIIB*AAA\n-----END CERTIFICATE-----", out _));
    }

    private const string KsnJwt = "eyJhbGciOiJLU04iLCJ0eXAiOiJKV1QiLCJzZXIiOiJlbXB0eSJ9.8V4Y1ruXGUpmAM335npYmA==.";
    private const string KsnJwtDecoded = "{\"alg\":\"KSN\",\"typ\":\"JWT\",\"ser\":\"empty\"}.8V4Y1ruXGUpmAM335npYmA==.";

    [TestMethod]
    [DataRow("", "")]
    [DataRow("Authorization: Bearer ", "")]
    [DataRow("token: \"", "\"")]
    [DataRow("GET https://ksn.example/api/v1/", " HTTP/1.1")]
    [DataRow("X-KSN-", "")]
    [DataRow("ksn_token_", "")]
    [DataRow("payload+", "")]
    public void JwtGluedToSurroundingTextIsDecoded(string prefix, string suffix)
    {
        Assert.IsTrue(Base64Detector.TryDecode(prefix + KsnJwt + suffix, out var decoded), prefix + KsnJwt + suffix);
        Assert.AreEqual(prefix + KsnJwtDecoded + suffix, decoded);
    }

    [TestMethod]
    public void TokenGluedToTrailingSuffixIsDecoded()
    {
        var token = Encode("{\"user\":\"admin\",\"id\":42}");

        Assert.IsTrue(Base64Detector.TryDecode($"value={token}_v2", out var decoded));
        Assert.AreEqual("value={\"user\":\"admin\",\"id\":42}_v2", decoded);
    }
}
