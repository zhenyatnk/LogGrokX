using System;
using System.Text;
using LogGrokX.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Tests;

[TestClass]
public class BinaryDetectionOptionsTests
{
    private const string Pem = "-----BEGIN MESSAGE-----\nUmVhZGFibGUgUEVNIHBheWxvYWQgdGV4dA==\n-----END MESSAGE-----";
    private const string Hex = "48656C6C6F20776F726C64";

    private static string Encode(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

    private static string Mixed => $"hex {Hex} b64 {Encode("Hello, World!")}\n{Pem}";

    [TestMethod]
    public void AllKindsAreDetectedByDefault()
    {
        var part = new LinePartViewModel(1, Mixed, detection: BinaryDetectionOptions.All);

        Assert.IsTrue(part.IsPem);
        Assert.IsTrue(part.IsBase64);
        Assert.IsTrue(part.IsHex);
    }

    [TestMethod]
    public void NothingIsDetectedWhenDisabled()
    {
        var part = new LinePartViewModel(1, Mixed, detection: BinaryDetectionOptions.None);

        Assert.IsFalse(part.IsPem);
        Assert.IsFalse(part.IsBase64);
        Assert.IsFalse(part.IsHex);
        part.IsDecoded = true;
        Assert.IsFalse(part.IsDecoded);
        Assert.AreEqual(part.OriginalText, part.TextModel.GetDisplayedText(null).Replace("\r\n", "\n"));
    }

    [TestMethod]
    [DataRow(false, true, true)]
    [DataRow(true, false, true)]
    [DataRow(true, true, false)]
    [DataRow(false, false, true)]
    [DataRow(true, false, false)]
    public void OnlySelectedKindsAreDetected(bool pem, bool base64, bool hex)
    {
        var part = new LinePartViewModel(1, Mixed, detection: new BinaryDetectionOptions(pem, base64, hex));

        Assert.AreEqual(pem, part.IsPem);
        Assert.AreEqual(base64, part.IsBase64);
        Assert.AreEqual(hex, part.IsHex);
    }

    [TestMethod]
    public void BinToggleDecodesOnlySelectedKinds()
    {
        var part = new LinePartViewModel(1, Mixed, detection: new BinaryDetectionOptions(false, false, true));

        part.IsDecoded = true;

        var displayed = part.TextModel.GetDisplayedText(null).Replace("\r\n", "\n");
        StringAssert.Contains(displayed, "hex Hello world");
        StringAssert.Contains(displayed, Encode("Hello, World!"));
        StringAssert.Contains(displayed, "UmVhZGFibGUgUEVNIHBheWxvYWQgdGV4dA==");
    }

    [TestMethod]
    public void DisabledIndexDetectionIgnoresOptions()
    {
        var part = new LinePartViewModel(1, Hex, detectBase64: false, detection: BinaryDetectionOptions.All);

        Assert.IsFalse(part.IsHex);
        Assert.IsFalse(part.IsBase64);
    }

    [TestMethod]
    public void OptionsAreBuiltFromSettings()
    {
        var settings = new ViewSettings { DetectPem = false, DetectBase64 = true, DetectHex = false };

        Assert.AreEqual(new BinaryDetectionOptions(false, true, false), BinaryDetectionOptions.FromSettings(settings));
        Assert.AreEqual(Base64Content.Base64, BinaryDetectionOptions.FromSettings(settings).Base64Kinds);

        settings.DetectBinary = false;
        Assert.AreEqual(BinaryDetectionOptions.None, BinaryDetectionOptions.FromSettings(settings));
    }

    [TestMethod]
    public void DetectionIsEnabledInDefaultSettings()
    {
        Assert.AreEqual(BinaryDetectionOptions.All, BinaryDetectionOptions.FromSettings(new ViewSettings()));
    }
}
