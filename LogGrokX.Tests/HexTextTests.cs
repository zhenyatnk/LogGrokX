using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Tests;

[TestClass]
public class HexTextTests
{
    [TestMethod]
    [DataRow("48656C6C6F20776F726C64", "Hello world")]
    [DataRow("48 65 6c 6c 6f 20 77 6f 72 6c 64", "Hello world")]
    [DataRow("48-65-6C-6C-6F-20-77-6F-72-6C-64", "Hello world")]
    [DataRow("48:65:6C:6C:6F:20:77:6F:72:6C:64", "Hello world")]
    [DataRow("0x48656C6C6F20776F726C64", "Hello world")]
    [DataRow("0x48 0x65 0x6C 0x6C 0x6F 0x20 0x77 0x6F 0x72 0x6C 0x64", "Hello world")]
    [DataRow("0x48, 0x65, 0x6C, 0x6C, 0x6F, 0x20, 0x77, 0x6F, 0x72, 0x6C, 0x64", "Hello world")]
    [DataRow("D09FD180D0B8D0B2D0B5D182", "Привет")]
    [DataRow("1F04400438043204350442040000", "Привет")]
    [DataRow("48656C6C6F20776F726C640000", "Hello world")]
    public void DecodesHexToText(string source, string expected)
    {
        Assert.IsTrue(HexText.ContainsDecodableHex(source));
        Assert.IsTrue(HexText.TryDecode(source, out var decoded));
        Assert.AreEqual(expected, decoded);
    }

    [TestMethod]
    public void DecodesOnlyHexPartOfMessage()
    {
        const string source = "Received data: 48656C6C6F20776F726C64 from client";

        Assert.IsTrue(HexText.TryDecode(source, out var decoded));
        Assert.AreEqual("Received data: Hello world from client", decoded);
    }

    [TestMethod]
    [DataRow("{\"data\":\"48656C6C6F20776F726C64\",\"id\":1}", "{\"data\":\"Hello world\",\"id\":1}")]
    [DataRow("payload {\"data\":\"48 65 6C 6C 6F 20 77 6F 72 6C 64\"} sent", "payload {\"data\":\"Hello world\"} sent")]
    [DataRow("{\"data\":\"2248656C6C6F21220A\"}", "{\"data\":\"\\\"Hello!\\\"\\n\"}")]
    public void DecodesHexInsideJsonStrings(string source, string expected)
    {
        Assert.IsTrue(HexText.TryDecode(source, out var decoded));
        Assert.AreEqual(expected, decoded);
        Assert.AreEqual(1, TextOperations.GetStructuredRanges(decoded).Count);
    }

    [TestMethod]
    public void DoesNotDecodeHexOutsideJsonStrings()
    {
        const string source = "{\"a\":41424344}";

        Assert.IsFalse(HexText.TryDecode(source, out var decoded));
        Assert.AreEqual(source, decoded);
    }

    [TestMethod]
    [DataRow("<root><data>48656C6C6F20776F726C64</data></root>", "<root><data>Hello world</data></root>")]
    [DataRow("<root><data value=\"48656C6C6F20776F726C64\"/></root>", "<root><data value=\"Hello world\"/></root>")]
    [DataRow("<root><data>3C613E2026203C2F613E</data></root>", "<root><data>&lt;a&gt; &amp; &lt;/a&gt;</data></root>")]
    public void DecodesHexInsideXml(string source, string expected)
    {
        Assert.IsTrue(HexText.TryDecode(source, out var decoded));
        Assert.AreEqual(expected, decoded);
        Assert.AreEqual(1, TextOperations.GetStructuredRanges(decoded).Count);
    }

    [TestMethod]
    public void DecodesHexCryptoApiPublicKeyBlob()
    {
        var hex = CreateCryptoApiRsaPublicKeyHex();

        Assert.IsTrue(HexText.ContainsDecodableHex(hex));
        Assert.IsTrue(HexText.TryDecode(hex, out var decoded));
        StringAssert.Contains(decoded, "RSA public key (CryptoAPI PUBLICKEYBLOB)");
        StringAssert.Contains(decoded, "Key size: 1024 bits");
        StringAssert.Contains(decoded, "Public exponent: 65537");
    }

    [TestMethod]
    public void DecodesHexCryptoApiPublicKeyBlobInsideXml()
    {
        var source = $"<data>{CreateCryptoApiRsaPublicKeyHex()}</data>";
        var part = new LinePartViewModel(1, source);

        Assert.IsTrue(HexText.ContainsDecodableHex(source));
        Assert.IsTrue(part.IsHex);
        part.IsHexDecoded = true;

        StringAssert.Contains(part.TextModel.GetDisplayedText(null),
            "RSA public key (CryptoAPI PUBLICKEYBLOB)");
    }

    [TestMethod]
    public void DecodesHexCryptoApiPublicKeyBlobInsideJson()
    {
        var source = $"{{\"data\":\"{CreateCryptoApiRsaPublicKeyHex()}\"}}";
        var part = new LinePartViewModel(1, source);

        Assert.IsTrue(HexText.ContainsDecodableHex(source));
        Assert.IsTrue(part.IsHex);
        part.IsHexDecoded = true;

        StringAssert.Contains(part.TextModel.GetDisplayedText(null),
            "RSA public key (CryptoAPI PUBLICKEYBLOB)");
    }

    private static string CreateCryptoApiRsaPublicKeyHex(int modulusBytes = 128)
    {
        var bytes = new byte[20 + modulusBytes];
        bytes[0] = 0x06;
        bytes[1] = 0x02;
        bytes[5] = 0xA4;
        bytes[8] = (byte)'R';
        bytes[9] = (byte)'S';
        bytes[10] = (byte)'A';
        bytes[11] = (byte)'1';
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), modulusBytes * 8);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), 65537);
        for (var i = 20; i < bytes.Length; i++)
            bytes[i] = (byte)(i * 7 + 1);
        return Convert.ToHexString(bytes);
    }

    [TestMethod]
    public void JsonStaysFoldableAfterHexDecoding()
    {
        var part = new LinePartViewModel(1, "{\"data\":\"48656C6C6F20776F726C64\",\"items\":[1,2]}");

        part.IsHexDecoded = true;

        Assert.IsNotNull(part.TextModel.CollapsibleRanges);
        StringAssert.Contains(part.TextModel.GetDisplayedText(new System.Collections.Generic.HashSet<int>()),
            "\"data\": \"Hello world\"");
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("Hello world")]
    [DataRow("4865")]
    [DataRow("48656C6C6F20776F726C642")]
    [DataRow("x48656C6C6F20776F726C64")]
    [DataRow("48 65 6C 6C 6F")]
    [DataRow("4142434445464748")]
    [DataRow("0x4142434445464748")]
    [DataRow("41424344454647484950")]
    [DataRow("12345678")]
    [DataRow("41424344")]
    [DataRow("0x41424344")]
    [DataRow("0x48656C6C6F")]
    [DataRow("0X80070005")]
    [DataRow("error 0x41424344 at 41424344")]
    [DataRow("{\"code\":\"0x41424344\"}")]
    [DataRow("<r><code>0x48656C6C6F</code></r>")]
    [DataRow("deadbeef")]
    [DataRow("3F2504E0-4F89-11D3-9A0C-0305E82C3301")]
    [DataRow("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")]
    public void DoesNotDetectNonTextHex(string source)
    {
        Assert.IsFalse(HexText.ContainsDecodableHex(source));
        Assert.IsFalse(HexText.TryDecode(source, out var decoded));
        Assert.AreEqual(source, decoded);
    }

    [TestMethod]
    public void LinePartSwitchesTextModelWhenHexDecoded()
    {
        var part = new LinePartViewModel(1, "data 48656C6C6F20776F726C64");
        var original = part.TextModel;

        Assert.IsTrue(part.IsHex);
        part.IsHexDecoded = true;

        Assert.AreNotSame(original, part.TextModel);
        Assert.AreEqual("data Hello world", part.TextModel.GetDisplayedText(null));

        part.IsHexDecoded = false;

        Assert.AreSame(original, part.TextModel);
    }

    [TestMethod]
    public void LinePartWithoutHexCannotBeDecoded()
    {
        var part = new LinePartViewModel(1, "plain message");

        Assert.IsFalse(part.IsHex);
        part.IsHexDecoded = true;

        Assert.IsFalse(part.IsHexDecoded);
    }

    [TestMethod]
    public void RowBinToggleDecodesHex()
    {
        var part = new LinePartViewModel(1, "data 48656C6C6F20776F726C64");

        part.IsDecoded = true;

        Assert.IsTrue(part.IsHexDecoded);
        Assert.AreEqual("data Hello world", part.TextModel.GetDisplayedText(null));

        part.IsDecoded = false;

        Assert.IsFalse(part.IsHexDecoded);
        Assert.AreEqual("data 48656C6C6F20776F726C64", part.TextModel.GetDisplayedText(null));
    }

    [TestMethod]
    public void HexAndBase64AreDecodedTogether()
    {
        var part = new LinePartViewModel(1, "hex 48656C6C6F20776F726C64 b64 eyJpZCI6NDIsIm9rIjp0cnVlfQ==");

        Assert.IsTrue(part.IsHex);
        Assert.IsTrue(part.IsBase64);
        part.IsDecoded = true;

        var displayed = part.TextModel.GetDisplayedText(new System.Collections.Generic.HashSet<int>());
        StringAssert.Contains(displayed, "hex Hello world");
        StringAssert.Contains(displayed, "\"id\": 42");
    }

    [TestMethod]
    public void IndexPartDoesNotDetectHex()
    {
        var part = new LinePartViewModel(1, "41424344", detectBase64: false);

        Assert.IsFalse(part.IsHex);
    }
}
