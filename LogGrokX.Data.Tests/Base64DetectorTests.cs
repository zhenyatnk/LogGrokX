using System;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
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

    private const string SampleJwtSignature = "8V4Y1ruXGUpmAM335npYmA==";
    private static readonly string SampleJwtHeader = Encode("{\"alg\":\"HS256\",\"typ\":\"JWT\",\"ser\":\"empty\"}");
    private static readonly string SampleJwt = SampleJwtHeader + "." + SampleJwtSignature + ".";
    private static readonly string SampleJwtDecoded =
        "{\"alg\":\"HS256\",\"typ\":\"JWT\",\"ser\":\"empty\"}." + SampleJwtSignature + ".";

    [TestMethod]
    [DataRow("", "")]
    [DataRow("Authorization: Bearer ", "")]
    [DataRow("token: \"", "\"")]
    [DataRow("GET https://example.com/api/v1/", " HTTP/1.1")]
    [DataRow("X-Token-", "")]
    [DataRow("token_", "")]
    [DataRow("payload+", "")]
    public void JwtGluedToSurroundingTextIsDecoded(string prefix, string suffix)
    {
        Assert.IsTrue(Base64Detector.TryDecode(prefix + SampleJwt + suffix, out var decoded), prefix + SampleJwt + suffix);
        Assert.AreEqual(prefix + SampleJwtDecoded + suffix, decoded);
    }

    [TestMethod]
    public void TokenGluedToTrailingSuffixIsDecoded()
    {
        var token = Encode("{\"user\":\"admin\",\"id\":42}");

        Assert.IsTrue(Base64Detector.TryDecode($"value={token}_v2", out var decoded));
        Assert.AreEqual("value={\"user\":\"admin\",\"id\":42}_v2", decoded);
    }

    [TestMethod]
    public void DetectReportsPemAndBase64Separately()
    {
        using var certificate = CreateCertificate();
        var pem = certificate.ExportCertificatePem();

        Assert.AreEqual(Base64Content.Pem, Base64Detector.Detect(pem));
        Assert.AreEqual(Base64Content.Base64, Base64Detector.Detect(SampleJwt));
        Assert.AreEqual(Base64Content.All, Base64Detector.Detect($"{SampleJwt}\n{pem}"));
        Assert.AreEqual(Base64Content.None, Base64Detector.Detect("plain text"));
    }

    [TestMethod]
    public void OnlySelectedContentIsDecoded()
    {
        using var certificate = CreateCertificate();
        var pem = certificate.ExportCertificatePem();
        var source = $"{SampleJwt}\n{pem}";

        Assert.IsTrue(Base64Detector.TryDecode(source, Base64Content.Pem, out var pemOnly, out var found));
        Assert.AreEqual(Base64Content.All, found);
        StringAssert.StartsWith(pemOnly, SampleJwt + "\n-----BEGIN CERTIFICATE-----\nSubject: ");

        Assert.IsTrue(Base64Detector.TryDecode(source, Base64Content.Base64, out var base64Only, out _));
        Assert.AreEqual($"{SampleJwtDecoded}\n{pem}", base64Only);
    }

    [TestMethod]
    public void PemBodyLinesAreNotTreatedAsBase64Fragments()
    {
        var text = string.Join("\n", Enumerable.Range(1, 5).Select(i => $"Readable line number {i} of the message"));
        var source = WrapPem("MESSAGE", Encoding.UTF8.GetBytes(text));

        Assert.AreEqual(Base64Content.Pem, Base64Detector.Detect(source));
        Assert.IsFalse(Base64Detector.TryDecode(source, Base64Content.Base64, out _, out _));
    }

    private static StructuredSpan[] Json(string source) =>
        new[] { new StructuredSpan(source.IndexOf('{'), source.LastIndexOf('}') - source.IndexOf('{') + 1, false) };

    private static StructuredSpan[] Xml(string source) =>
        new[] { new StructuredSpan(source.IndexOf('<'), source.LastIndexOf('>') - source.IndexOf('<') + 1, true) };

    private static string DecodeAll(string source, StructuredSpan[] spans)
    {
        Assert.IsTrue(Base64Detector.TryDecode(source, Base64Content.All, out var decoded, out _, spans), source);
        return decoded;
    }

    [TestMethod]
    public void JsonValueWithEncodedJsonBecomesNestedObject()
    {
        var source = $"request {{\"token\":\"{SampleJwtHeader}\",\"n\":1}}";

        var decoded = DecodeAll(source, Json(source));

        Assert.AreEqual("request {\"token\":{\"alg\":\"HS256\",\"typ\":\"JWT\",\"ser\":\"empty\"},\"n\":1}", decoded);
    }

    [TestMethod]
    public void JsonValueWithJwtStaysValidJson()
    {
        var source = $"{{\"token\":\"{SampleJwt}\"}}";

        var decoded = DecodeAll(source, Json(source));

        using var document = JsonDocument.Parse(decoded);
        Assert.AreEqual(SampleJwtDecoded, document.RootElement.GetProperty("token").GetString());
    }

    [TestMethod]
    public void JsonEscapedBase64IsDetected()
    {
        var text = "a>>>b, c>>>d and some more text";
        var base64 = Encode(text);
        Assert.IsTrue(base64.Contains('+'), base64);
        var source = $"{{\"data\":\"{base64.Replace("+", "\\u002B").Replace("/", "\\/")}\"}}";

        Assert.AreEqual(Base64Content.None, Base64Detector.Detect(source));
        Assert.AreEqual(Base64Content.Base64, Base64Detector.Detect(source, Json(source)));
        using var document = JsonDocument.Parse(DecodeAll(source, Json(source)));
        Assert.AreEqual(text, document.RootElement.GetProperty("data").GetString());
    }

    [TestMethod]
    public void JsonPemValueBecomesArrayOfLines()
    {
        using var certificate = CreateCertificate();
        var source = $"{{\"certificate\":{JsonSerializer.Serialize(certificate.ExportCertificatePem())},\"id\":7}}";

        Assert.AreEqual(Base64Content.Pem, Base64Detector.Detect(source, Json(source)));
        var decoded = DecodeAll(source, Json(source));

        using var document = JsonDocument.Parse(decoded);
        var lines = document.RootElement.GetProperty("certificate").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.AreEqual("-----BEGIN CERTIFICATE-----", lines[0]);
        Assert.AreEqual("-----END CERTIFICATE-----", lines[^1]);
        CollectionAssert.Contains(lines, "Subject: CN=loggrokx.test, O=LogGrokX");
        Assert.AreEqual(7, document.RootElement.GetProperty("id").GetInt32());
    }

    [TestMethod]
    public void JsonKeysAreNotDecoded()
    {
        var key = Encode("readable key text");
        var source = $"{{\"{key}\":1}}";

        Assert.AreEqual(Base64Content.None, Base64Detector.Detect(source, Json(source)));
    }

    [TestMethod]
    public void JsonDecodesOnlySelectedKinds()
    {
        using var certificate = CreateCertificate();
        var source = $"{{\"jwt\":\"{SampleJwt}\",\"pem\":{JsonSerializer.Serialize(certificate.ExportCertificatePem())}}}";

        Assert.AreEqual(Base64Content.All, Base64Detector.Detect(source, Json(source)));
        Assert.IsTrue(Base64Detector.TryDecode(source, Base64Content.Base64, out var decoded, out _, Json(source)));

        using var document = JsonDocument.Parse(decoded);
        Assert.AreEqual(SampleJwtDecoded, document.RootElement.GetProperty("jwt").GetString());
        Assert.AreEqual(certificate.ExportCertificatePem(), document.RootElement.GetProperty("pem").GetString());
    }

    [TestMethod]
    public void XmlElementTextIsDecodedAndEscaped()
    {
        var source = $"<root><data>{Encode("<b>bold</b> & {\"a\":1}")}</data></root>";

        var decoded = DecodeAll(source, Xml(source));

        Assert.AreEqual("<b>bold</b> & {\"a\":1}", XDocument.Parse(decoded).Root!.Element("data")!.Value);
    }

    [TestMethod]
    public void XmlAttributeValueIsDecodedAndEscaped()
    {
        var source = $"<root><item token=\"{SampleJwt}\" id='{Encode("it's readable")}'/></root>";

        var decoded = DecodeAll(source, Xml(source));

        var item = XDocument.Parse(decoded).Root!.Element("item")!;
        Assert.AreEqual(SampleJwtDecoded, item.Attribute("token")!.Value);
        Assert.AreEqual("it's readable", item.Attribute("id")!.Value);
    }

    [TestMethod]
    public void XmlPemWithEncodedLineBreaksIsDecoded()
    {
        using var certificate = CreateCertificate();
        var pem = certificate.ExportCertificatePem().Replace("\n", "&#xA;");
        var source = $"<Signature><Cert>{pem}</Cert><Key>{WrapPem("PRIVATE KEY", new byte[] { 1, 2, 3 }, "&#13;&#10;")}</Key></Signature>";

        Assert.AreEqual(Base64Content.Pem, Base64Detector.Detect(source, Xml(source)));
        var root = XDocument.Parse(DecodeAll(source, Xml(source))).Root!;
        StringAssert.Contains(root.Element("Cert")!.Value, "Subject: CN=loggrokx.test, O=LogGrokX");
        StringAssert.Contains(root.Element("Key")!.Value, "00000000  01 02 03");
    }

    [TestMethod]
    public void XmlCdataIsDecoded()
    {
        var source = $"<root><![CDATA[{Encode("{\"in\":\"cdata\"}")}]]></root>";

        var decoded = DecodeAll(source, Xml(source));

        Assert.AreEqual("{\"in\":\"cdata\"}", XDocument.Parse(decoded).Root!.Value);
    }

    [TestMethod]
    public void TextAroundStructuredSpanIsStillDecoded()
    {
        var source = $"auth={SampleJwt} body={{\"data\":\"{SampleJwtHeader}\"}}";

        var decoded = DecodeAll(source, Json(source));

        Assert.AreEqual($"auth={SampleJwtDecoded} body={{\"data\":{{\"alg\":\"HS256\",\"typ\":\"JWT\",\"ser\":\"empty\"}}}}", decoded);
    }

    private static byte[] CreateCryptoApiRsaPublicKeyBlob(int modulusBytes = 256)
    {
        var bytes = new byte[20 + modulusBytes];
        bytes[0] = 0x06;
        bytes[1] = 0x02;
        bytes[5] = 0xA4;
        bytes[8] = (byte)'R';
        bytes[9] = (byte)'S';
        bytes[10] = (byte)'A';
        bytes[11] = (byte)'1';
        BitConverter.GetBytes(modulusBytes * 8).CopyTo(bytes, 12);
        BitConverter.GetBytes(65537u).CopyTo(bytes, 16);
        for (var i = 20; i < bytes.Length; i++)
            bytes[i] = (byte)(i * 7 + 1);
        return bytes;
    }

    [TestMethod]
    public void DerCertificateInJsonIsDetectedAsPem()
    {
        using var certificate = CreateCertificate();
        var source = $"response={{\"certificates\": [{{\"data\": \"{Convert.ToBase64String(certificate.RawData)}\"}}]}}";

        Assert.AreEqual(Base64Content.Pem, Base64Detector.Detect(source, Json(source)));
        Assert.IsTrue(Base64Detector.TryDecode(source, Base64Content.Pem, out var decoded, out _, Json(source)));

        using var document = JsonDocument.Parse(decoded["response=".Length..]);
        var lines = document.RootElement.GetProperty("certificates")[0].GetProperty("data")
            .EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.AreEqual("X.509 certificate", lines[0]);
        CollectionAssert.Contains(lines, "Subject: CN=loggrokx.test, O=LogGrokX");
        CollectionAssert.Contains(lines, "Not after: 2027-01-02 03:04:05 UTC");
        CollectionAssert.Contains(lines, "Thumbprint (SHA-1): " + certificate.Thumbprint);
        CollectionAssert.Contains(lines, "Public key: RSA 2048 bits");
    }

    [TestMethod]
    public void DerCertificateInPlainTextIsDetectedAsPem()
    {
        using var certificate = CreateCertificate();
        var source = $"server certificate {Convert.ToBase64String(certificate.RawData)} accepted";

        Assert.AreEqual(Base64Content.Pem, Base64Detector.Detect(source));
        Assert.IsTrue(Base64Detector.TryDecode(source, out var decoded));
        StringAssert.StartsWith(decoded, "server certificate X.509 certificate\nSubject: CN=loggrokx.test, O=LogGrokX");
        StringAssert.EndsWith(decoded, " accepted");
    }

    [TestMethod]
    public void CryptoApiPublicKeyBlobIsDescribed()
    {
        var blob = Convert.ToBase64String(CreateCryptoApiRsaPublicKeyBlob());
        var source = $"{{\"publicKey\": {{\"data\": \"{blob}\",\"keyId\": 29}}}}";

        Assert.AreEqual(Base64Content.Pem, Base64Detector.Detect(source, Json(source)));
        using var document = JsonDocument.Parse(DecodeAll(source, Json(source)));
        var lines = document.RootElement.GetProperty("publicKey").GetProperty("data")
            .EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.AreEqual("RSA public key (CryptoAPI PUBLICKEYBLOB)", lines[0]);
        CollectionAssert.Contains(lines, "Algorithm: CALG_RSA_KEYX");
        CollectionAssert.Contains(lines, "Key size: 2048 bits");
        CollectionAssert.Contains(lines, "Public exponent: 65537");
        Assert.AreEqual(29, document.RootElement.GetProperty("publicKey").GetProperty("keyId").GetInt32());
    }

    [TestMethod]
    public void CryptoApiPublicKeyBytesAreDescribed()
    {
        var data = CreateCryptoApiRsaPublicKeyBlob();

        Assert.IsTrue(Base64Detector.TryDescribeBinaryKeyFromBytes(data, out var description));
        StringAssert.StartsWith(description, "RSA public key (CryptoAPI PUBLICKEYBLOB)");
    }

    [TestMethod]
    public void RandomBinaryBytesAreNotDescribed()
    {
        var data = new byte[256];
        new Random(7).NextBytes(data);
        data[0] = 0x99;

        Assert.IsFalse(Base64Detector.TryDescribeBinaryKeyFromBytes(data, out var description));
        Assert.AreEqual(string.Empty, description);
    }

    [TestMethod]
    public void SubjectPublicKeyInfoIsDescribed()
    {
        using var rsa = RSA.Create(2048);
        var source = Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo());

        Assert.IsTrue(Base64Detector.TryDecode(source, out var decoded));
        Assert.AreEqual("Public key (SubjectPublicKeyInfo): RSA 2048 bits", decoded);
    }

    [TestMethod]
    public void RandomBinaryStartingLikeDerIsNotDetected()
    {
        var data = new byte[300];
        new Random(42).NextBytes(data);
        data[0] = 0x30;
        data[1] = 0x82;

        Assert.AreEqual(Base64Content.None, Base64Detector.Detect(Convert.ToBase64String(data)));
    }
}
