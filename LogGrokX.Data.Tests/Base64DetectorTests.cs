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

    [TestMethod]
    public void DetectReportsPemAndBase64Separately()
    {
        using var certificate = CreateCertificate();
        var pem = certificate.ExportCertificatePem();

        Assert.AreEqual(Base64Content.Pem, Base64Detector.Detect(pem));
        Assert.AreEqual(Base64Content.Base64, Base64Detector.Detect(KsnJwt));
        Assert.AreEqual(Base64Content.All, Base64Detector.Detect($"{KsnJwt}\n{pem}"));
        Assert.AreEqual(Base64Content.None, Base64Detector.Detect("plain text"));
    }

    [TestMethod]
    public void OnlySelectedContentIsDecoded()
    {
        using var certificate = CreateCertificate();
        var pem = certificate.ExportCertificatePem();
        var source = $"{KsnJwt}\n{pem}";

        Assert.IsTrue(Base64Detector.TryDecode(source, Base64Content.Pem, out var pemOnly, out var found));
        Assert.AreEqual(Base64Content.All, found);
        StringAssert.StartsWith(pemOnly, KsnJwt + "\n-----BEGIN CERTIFICATE-----\nSubject: ");

        Assert.IsTrue(Base64Detector.TryDecode(source, Base64Content.Base64, out var base64Only, out _));
        Assert.AreEqual($"{KsnJwtDecoded}\n{pem}", base64Only);
    }

    [TestMethod]
    public void PemBodyLinesAreNotTreatedAsBase64Fragments()
    {
        var text = string.Join("\n", Enumerable.Range(1, 5).Select(i => $"Readable line number {i} of the message"));
        var source = WrapPem("MESSAGE", Encoding.UTF8.GetBytes(text));

        Assert.AreEqual(Base64Content.Pem, Base64Detector.Detect(source));
        Assert.IsFalse(Base64Detector.TryDecode(source, Base64Content.Base64, out _, out _));
    }

    private const string KsnJwtHeader = "eyJhbGciOiJLU04iLCJ0eXAiOiJKV1QiLCJzZXIiOiJlbXB0eSJ9";

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
        var source = $"request {{\"token\":\"{KsnJwtHeader}\",\"n\":1}}";

        var decoded = DecodeAll(source, Json(source));

        Assert.AreEqual("request {\"token\":{\"alg\":\"KSN\",\"typ\":\"JWT\",\"ser\":\"empty\"},\"n\":1}", decoded);
    }

    [TestMethod]
    public void JsonValueWithJwtStaysValidJson()
    {
        var source = $"{{\"token\":\"{KsnJwt}\"}}";

        var decoded = DecodeAll(source, Json(source));

        using var document = JsonDocument.Parse(decoded);
        Assert.AreEqual(KsnJwtDecoded, document.RootElement.GetProperty("token").GetString());
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
        var source = $"{{\"jwt\":\"{KsnJwt}\",\"pem\":{JsonSerializer.Serialize(certificate.ExportCertificatePem())}}}";

        Assert.AreEqual(Base64Content.All, Base64Detector.Detect(source, Json(source)));
        Assert.IsTrue(Base64Detector.TryDecode(source, Base64Content.Base64, out var decoded, out _, Json(source)));

        using var document = JsonDocument.Parse(decoded);
        Assert.AreEqual(KsnJwtDecoded, document.RootElement.GetProperty("jwt").GetString());
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
        var source = $"<root><item token=\"{KsnJwt}\" id='{Encode("it's readable")}'/></root>";

        var decoded = DecodeAll(source, Xml(source));

        var item = XDocument.Parse(decoded).Root!.Element("item")!;
        Assert.AreEqual(KsnJwtDecoded, item.Attribute("token")!.Value);
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
        var source = $"auth={KsnJwt} body={{\"data\":\"{KsnJwtHeader}\"}}";

        var decoded = DecodeAll(source, Json(source));

        Assert.AreEqual($"auth={KsnJwtDecoded} body={{\"data\":{{\"alg\":\"KSN\",\"typ\":\"JWT\",\"ser\":\"empty\"}}}}", decoded);
    }

    private const string KsnRootCertificate = "MIICUjCCAbSgAwIBAgIQFGnEabbVTpBNa4IBTv+SkTAKBggqhkjOPQQDAzA+MQswCQYDVQQGEwJSVTESMBAGA1UEChMJS2FzcGVyc2t5MRswGQYDVQQDExJLU04gR2xvYmFsIFJvb3QgQ0EwHhcNMjAwNjEyMDk1MjM2WhcNMzUwNjEyMTAwMjM1WjA+MQswCQYDVQQGEwJSVTESMBAGA1UEChMJS2FzcGVyc2t5MRswGQYDVQQDExJLU04gR2xvYmFsIFJvb3QgQ0EwgZswEAYHKoZIzj0CAQYFK4EEACMDgYYABACobUHA+DeovYTLxlLi0QckBTV3YFt+qsn+2gc4T7ewoF/Rp5acBePD3FBjumPZAA0KrkwMkKSedxHGi3/MuVHWRgEdItNnQegL7sfWqs26e5MCqZP9jG5+pgTXkit3n6vNDYPDLl6a1DqfchbzLKQkm2Zl2y0tBslFfxkBCGiup5hLn6NRME8wCwYDVR0PBAQDAgGGMA8GA1UdEwEB/wQFMAMBAf8wHQYDVR0OBBYEFEUxxSF7nMy7jf9zbROUM1EhPIvcMBAGCSsGAQQBgjcVAQQDAgEAMAoGCCqGSM49BAMDA4GLADCBhwJCAMIoQUBTAL0Clz6UQZmucONRAEwTPf3DWFq6VPhfgpwsocYFbGGfqUk6E4bbostl3Afx6rsAGHAp8kOl/chUc1PNAkF1QtsIotqqjOyTM78CbLDqzYiSOjcuajBG1SsUqpOd+AUKAzxA6IE/r2Z/Z5Zl5GzDiTC63UVDFoSfsnIxI/rWgA==";

    private const string KsnPublicKeyBlob = "BgIAAACkAABSU0ExAAgAAAEAAQBnZ7C0i39qekoMzDGj2FsO5IccgwOp2TVK6epf8/P1+jVHG57mFWSL6goJ4t3IJZhBIvRCD2ORHSfQ4ETECsVj6rQQTB8JhdcQ/Z1avNEP37q2XFIg522vRArRC+0vrmNUtTTxuAQ4xW+QFb+6VbcTLRsC+81UnPTuKSq9XShimPvDHY1dCWw6cmFv/FeWoQD0vdKtfkAqAQigni/h78qoHIoGcBPBMucwIFQN9TY6+SouPEdDfBhv1u3DODwFPPU6uWPWN/CWlb+4eW4fiCejtDOA9oPRDRsDMwr3OeA2XRq2sq02PB67Idg56ia/RjhBCan2icTE1TojhzFcz9PY";

    [TestMethod]
    public void DerCertificateInJsonIsDetectedAsPem()
    {
        var source = $"response={{\"certificates\": [{{\"data\": \"{KsnRootCertificate}\"}}]}}";

        Assert.AreEqual(Base64Content.Pem, Base64Detector.Detect(source, Json(source)));
        Assert.IsTrue(Base64Detector.TryDecode(source, Base64Content.Pem, out var decoded, out _, Json(source)));

        using var document = JsonDocument.Parse(decoded["response=".Length..]);
        var lines = document.RootElement.GetProperty("certificates")[0].GetProperty("data")
            .EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.AreEqual("X.509 certificate", lines[0]);
        CollectionAssert.Contains(lines, "Subject: CN=KSN Global Root CA, O=Kaspersky, C=RU");
        CollectionAssert.Contains(lines, "Not after: 2035-06-12 10:02:35 UTC");
        CollectionAssert.Contains(lines, "Thumbprint (SHA-1): 7C889985F2A6DFB89943DCA23E7F4B4D1E6CE799");
        CollectionAssert.Contains(lines, "Public key: ECC 521 bits");
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
        var source = $"{{\"ksnPublicKey\": {{\"data\": \"{KsnPublicKeyBlob}\",\"keyId\": 29}}}}";

        Assert.AreEqual(Base64Content.Pem, Base64Detector.Detect(source, Json(source)));
        using var document = JsonDocument.Parse(DecodeAll(source, Json(source)));
        var lines = document.RootElement.GetProperty("ksnPublicKey").GetProperty("data")
            .EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.AreEqual("RSA public key (CryptoAPI PUBLICKEYBLOB)", lines[0]);
        CollectionAssert.Contains(lines, "Algorithm: CALG_RSA_KEYX");
        CollectionAssert.Contains(lines, "Key size: 2048 bits");
        CollectionAssert.Contains(lines, "Public exponent: 65537");
        Assert.AreEqual(29, document.RootElement.GetProperty("ksnPublicKey").GetProperty("keyId").GetInt32());
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
