using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Tests;

[TestClass]
public class HexTextTests
{
    [TestMethod]
    [DataRow("48656C6C6F20776F726C64", "Hello world")]
    [DataRow("48 65 6c 6c 6f", "Hello")]
    [DataRow("48-65-6C-6C-6F", "Hello")]
    [DataRow("48:65:6C:6C:6F", "Hello")]
    [DataRow("0x48656C6C6F", "Hello")]
    [DataRow("0x48 0x65 0x6C 0x6C 0x6F", "Hello")]
    [DataRow("0x48, 0x65, 0x6C, 0x6C, 0x6F", "Hello")]
    [DataRow("D09FD180D0B8D0B2D0B5D182", "Привет")]
    [DataRow("1F04400438043204350442040000", "Привет")]
    [DataRow("48656C6C6F00", "Hello")]
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
    [DataRow("")]
    [DataRow("Hello world")]
    [DataRow("4865")]
    [DataRow("48656C6C6F2")]
    [DataRow("x48656C6C6F")]
    [DataRow("12345678")]
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
        var part = new LinePartViewModel(1, "data 48656C6C6F");
        var original = part.TextModel;

        Assert.IsTrue(part.IsHexDetected);
        part.IsHexDecoded = true;

        Assert.AreNotSame(original, part.TextModel);
        Assert.AreEqual("data Hello", part.TextModel.GetDisplayedText(null));

        part.IsHexDecoded = false;

        Assert.AreSame(original, part.TextModel);
    }

    [TestMethod]
    public void LinePartWithoutHexCannotBeDecoded()
    {
        var part = new LinePartViewModel(1, "plain message");

        Assert.IsFalse(part.IsHexDetected);
        part.IsHexDecoded = true;

        Assert.IsFalse(part.IsHexDecoded);
    }

    [TestMethod]
    public void IndexPartDoesNotDetectHex()
    {
        var part = new LinePartViewModel(1, "41424344", detectHex: false);

        Assert.IsFalse(part.IsHexDetected);
    }
}
