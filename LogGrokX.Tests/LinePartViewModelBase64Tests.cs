using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Tests
{
    [TestClass]
    public class LinePartViewModelBase64Tests
    {
        private static string Encode(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

        [TestMethod]
        public void Base64ContentIsDetected()
        {
            var part = new LinePartViewModel(1, Encode("Hello, World!"));

            Assert.IsTrue(part.IsBase64);
            Assert.IsFalse(part.IsBase64Decoded);
        }

        [TestMethod]
        public void PlainContentIsNotDetectedAndCannotBeDecoded()
        {
            var part = new LinePartViewModel(1, "Connection established");

            Assert.IsFalse(part.IsBase64);
            part.IsBase64Decoded = true;
            Assert.IsFalse(part.IsBase64Decoded);
        }

        [TestMethod]
        public void DetectionCanBeDisabled()
        {
            var part = new LinePartViewModel(1, Encode("Hello, World!"), detectBase64: false);

            Assert.IsFalse(part.IsBase64);
        }

        [TestMethod]
        public void TogglingSwitchesTextModelAndRaisesNotifications()
        {
            var source = Encode("Hello, World!");
            var part = new LinePartViewModel(1, source);
            var original = part.TextModel;
            var changed = new List<string>();
            ((INotifyPropertyChanged)part).PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? string.Empty);

            part.IsBase64Decoded = true;

            Assert.AreNotSame(original, part.TextModel);
            Assert.AreNotEqual(original.UniqueId, part.TextModel.UniqueId);
            Assert.AreEqual("Hello, World!", part.TextModel.GetDisplayedText(null));
            CollectionAssert.Contains(changed, nameof(LinePartViewModel.IsBase64Decoded));
            CollectionAssert.Contains(changed, nameof(LinePartViewModel.TextModel));

            part.IsBase64Decoded = false;

            Assert.AreSame(original, part.TextModel);
            Assert.AreEqual(source, part.OriginalText);
        }

        [TestMethod]
        public void DecodedJsonBecomesFoldable()
        {
            var part = new LinePartViewModel(1, Encode("{\"a\":1,\"b\":{\"c\":[1,2,3]}}"));

            part.IsBase64Decoded = true;

            Assert.IsNotNull(part.TextModel.CollapsibleRanges);
            Assert.IsTrue(part.TextModel.Count > 1);
        }

        private const string Pem = "-----BEGIN MESSAGE-----\nUmVhZGFibGUgUEVNIHBheWxvYWQgdGV4dA==\n-----END MESSAGE-----";

        [TestMethod]
        public void PemIsDetectedSeparatelyFromBase64()
        {
            var pem = new LinePartViewModel(1, Pem);
            var base64 = new LinePartViewModel(2, Encode("Hello, World!"));
            var both = new LinePartViewModel(3, $"{Encode("Hello, World!")}\n{Pem}");

            Assert.IsTrue(pem.IsPem);
            Assert.IsFalse(pem.IsBase64);
            Assert.IsFalse(base64.IsPem);
            Assert.IsTrue(base64.IsBase64);
            Assert.IsTrue(both.IsPem);
            Assert.IsTrue(both.IsBase64);
        }

        [TestMethod]
        public void PemAndBase64AreToggledIndependently()
        {
            var part = new LinePartViewModel(1, $"{Encode("Hello, World!")}\n{Pem}");

            part.IsPemDecoded = true;
            Assert.IsTrue(part.IsDecoded);
            StringAssert.Contains(part.TextModel.GetDisplayedText(null), "Readable PEM payload text");
            StringAssert.Contains(part.TextModel.GetDisplayedText(null), Encode("Hello, World!"));

            part.IsBase64Decoded = true;
            StringAssert.Contains(part.TextModel.GetDisplayedText(null), "Hello, World!");
            StringAssert.Contains(part.TextModel.GetDisplayedText(null), "Readable PEM payload text");

            part.IsPemDecoded = false;
            StringAssert.Contains(part.TextModel.GetDisplayedText(null), "UmVhZGFibGUgUEVNIHBheWxvYWQgdGV4dA==");

            part.IsBase64Decoded = false;
            Assert.IsFalse(part.IsDecoded);
            Assert.AreEqual(part.OriginalText, part.TextModel.GetDisplayedText(null).Replace("\r\n", "\n"));
        }

        [TestMethod]
        public void PemCannotBeDecodedWithoutPem()
        {
            var part = new LinePartViewModel(1, Encode("Hello, World!"));

            part.IsPemDecoded = true;

            Assert.IsFalse(part.IsPemDecoded);
            Assert.IsFalse(part.IsDecoded);
        }

        [TestMethod]
        public void Base64InsideJsonIsDecodedAndJsonStaysFoldable()
        {
            var part = new LinePartViewModel(1,
                "{\"token\":\"eyJhbGciOiJLU04iLCJ0eXAiOiJKV1QiLCJzZXIiOiJlbXB0eSJ9\",\"n\":1}");

            Assert.IsTrue(part.IsBase64);
            part.IsBase64Decoded = true;

            Assert.IsNotNull(part.TextModel.CollapsibleRanges);
            StringAssert.Contains(part.TextModel.GetDisplayedText(null), "\"alg\": \"KSN\"");
        }

        [TestMethod]
        public void PemInsideXmlIsDetected()
        {
            var part = new LinePartViewModel(1, $"<root><cert>{Pem.Replace("\n", "&#xA;")}</cert></root>");

            Assert.IsTrue(part.IsPem);
            part.IsPemDecoded = true;

            StringAssert.Contains(part.TextModel.GetDisplayedText(null), "Readable PEM payload text");
        }

        [TestMethod]
        public void DerCertificatesInMultiLineJsonResponseGetPemToggle()
        {
            var source = "Request for discovery service finished with resultCode=0x00000000 (No error); statusCode=200; response={\n" +
                         "  \"segment\": \"eyJhbGciOiJLU04iLCJ0eXAiOiJKV1QiLCJzZXIiOiJlbXB0eSJ9.8V4Y1ruXGUpmAM335npYmA==\",\n" +
                         "  \"serviceBindings\": [\n" +
                         "    {\"certificates\": [{\"data\": \"MIICUjCCAbSgAwIBAgIQFGnEabbVTpBNa4IBTv+SkTAKBggqhkjOPQQDAzA+MQswCQYDVQQGEwJSVTESMBAGA1UEChMJS2FzcGVyc2t5MRswGQYDVQQDExJLU04gR2xvYmFsIFJvb3QgQ0EwHhcNMjAwNjEyMDk1MjM2WhcNMzUwNjEyMTAwMjM1WjA+MQswCQYDVQQGEwJSVTESMBAGA1UEChMJS2FzcGVyc2t5MRswGQYDVQQDExJLU04gR2xvYmFsIFJvb3QgQ0EwgZswEAYHKoZIzj0CAQYFK4EEACMDgYYABACobUHA+DeovYTLxlLi0QckBTV3YFt+qsn+2gc4T7ewoF/Rp5acBePD3FBjumPZAA0KrkwMkKSedxHGi3/MuVHWRgEdItNnQegL7sfWqs26e5MCqZP9jG5+pgTXkit3n6vNDYPDLl6a1DqfchbzLKQkm2Zl2y0tBslFfxkBCGiup5hLn6NRME8wCwYDVR0PBAQDAgGGMA8GA1UdEwEB/wQFMAMBAf8wHQYDVR0OBBYEFEUxxSF7nMy7jf9zbROUM1EhPIvcMBAGCSsGAQQBgjcVAQQDAgEAMAoGCCqGSM49BAMDA4GLADCBhwJCAMIoQUBTAL0Clz6UQZmucONRAEwTPf3DWFq6VPhfgpwsocYFbGGfqUk6E4bbostl3Afx6rsAGHAp8kOl/chUc1PNAkF1QtsIotqqjOyTM78CbLDqzYiSOjcuajBG1SsUqpOd+AUKAzxA6IE/r2Z/Z5Zl5GzDiTC63UVDFoSfsnIxI/rWgA==\"}]," +
                         "\"ksnPublicKey\": {\"data\": \"BgIAAACkAABSU0ExAAgAAAEAAQBnZ7C0i39qekoMzDGj2FsO5IccgwOp2TVK6epf8/P1+jVHG57mFWSL6goJ4t3IJZhBIvRCD2ORHSfQ4ETECsVj6rQQTB8JhdcQ/Z1avNEP37q2XFIg522vRArRC+0vrmNUtTTxuAQ4xW+QFb+6VbcTLRsC+81UnPTuKSq9XShimPvDHY1dCWw6cmFv/FeWoQD0vdKtfkAqAQigni/h78qoHIoGcBPBMucwIFQN9TY6+SouPEdDfBhv1u3DODwFPPU6uWPWN/CWlb+4eW4fiCejtDOA9oPRDRsDMwr3OeA2XRq2sq02PB67Idg56ia/RjhBCan2icTE1TojhzFcz9PY\",\"keyId\": 29}}\n" +
                         "  ]\n}";
            var part = new LinePartViewModel(1, source);

            Assert.IsTrue(part.IsPem);
            Assert.IsTrue(part.IsBase64);
            part.IsPemDecoded = true;

            Assert.IsNotNull(part.TextModel.CollapsibleRanges);
            var text = part.TextModel.GetDisplayedText(null);
            StringAssert.Contains(text, "Subject: CN=KSN Global Root CA, O=Kaspersky, C=RU");
            StringAssert.Contains(text, "RSA public key (CryptoAPI PUBLICKEYBLOB)");
        }

        private sealed class TestLine : BaseLogLineViewModel
        {
            private readonly LinePartViewModel[] _parts;

            public TestLine(params string[] fields) : base(0, new LogGrokX.Controls.Selection())
            {
                _parts = fields.Select((f, i) => new LinePartViewModel(i, f)).ToArray();
            }

            public LinePartViewModel this[int index] => _parts[index];

            protected override IEnumerable<LinePartViewModel> GetDecodableParts() => _parts;
        }

        [TestMethod]
        public void RowTogglesDecodeAllMatchingParts()
        {
            var line = new TestLine("INFO", Encode("Hello, World!"), Pem, $"x={Encode("second fragment")}");

            Assert.IsTrue(line.IsPem);
            Assert.IsTrue(line.IsBase64);

            line.IsBase64Decoded = true;
            Assert.IsTrue(line[1].IsBase64Decoded);
            Assert.IsTrue(line[3].IsBase64Decoded);
            Assert.IsFalse(line[2].IsPemDecoded);
            Assert.IsFalse(line.IsPemDecoded);

            line.IsPemDecoded = true;
            Assert.IsTrue(line[2].IsPemDecoded);
            Assert.AreEqual("INFO", line[0].TextModel.GetDisplayedText(null));
        }

        [TestMethod]
        public void RowStateFollowsPartToggles()
        {
            var line = new TestLine(Pem);
            var changed = new List<string>();
            Assert.IsTrue(line.IsPem);
            ((INotifyPropertyChanged)line).PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? string.Empty);

            line[0].IsPemDecoded = true;

            Assert.IsTrue(line.IsPemDecoded);
            CollectionAssert.Contains(changed, nameof(BaseLogLineViewModel.IsPemDecoded));
        }

        [TestMethod]
        public void RowWithoutEncodedPartsHasNoToggles()
        {
            var line = new TestLine("INFO", "Connection established");

            Assert.IsFalse(line.IsPem);
            Assert.IsFalse(line.IsBase64);
            line.IsBase64Decoded = true;
            Assert.IsFalse(line.IsBase64Decoded);
        }
    }
}
