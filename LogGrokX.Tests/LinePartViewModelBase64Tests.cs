using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
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
            var token = Encode("{\"alg\":\"HS256\",\"typ\":\"JWT\",\"ser\":\"empty\"}");
            var part = new LinePartViewModel(1, $"{{\"token\":\"{token}\",\"n\":1}}");

            Assert.IsTrue(part.IsBase64);
            part.IsBase64Decoded = true;

            Assert.IsNotNull(part.TextModel.CollapsibleRanges);
            StringAssert.Contains(part.TextModel.GetDisplayedText(null), "\"alg\": \"HS256\"");
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
            using var certificate = CreateCertificate();
            var certificateBase64 = Convert.ToBase64String(certificate.RawData);
            var publicKeyBlob = Convert.ToBase64String(CreateCryptoApiRsaPublicKeyBlob());
            var segment = Encode("{\"alg\":\"HS256\",\"typ\":\"JWT\",\"ser\":\"empty\"}");
            var source = "Request for discovery service finished with resultCode=0x00000000 (No error); statusCode=200; response={\n" +
                         $"  \"segment\": \"{segment}\",\n" +
                         "  \"serviceBindings\": [\n" +
                         $"    {{\"certificates\": [{{\"data\": \"{certificateBase64}\"}}]," +
                         $"\"publicKey\": {{\"data\": \"{publicKeyBlob}\",\"keyId\": 29}}\n" +
                         "  ]\n}";
            var part = new LinePartViewModel(1, source);

            Assert.IsTrue(part.IsPem);
            Assert.IsTrue(part.IsBase64);
            part.IsPemDecoded = true;

            Assert.IsNotNull(part.TextModel.CollapsibleRanges);
            var text = part.TextModel.GetDisplayedText(null);
            StringAssert.Contains(text, "Subject: CN=loggrokx.test, O=LogGrokX");
            StringAssert.Contains(text, "RSA public key (CryptoAPI PUBLICKEYBLOB)");
        }

        private static X509Certificate2 CreateCertificate()
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=loggrokx.test, O=LogGrokX", rsa,
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            return request.CreateSelfSigned(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
                new DateTimeOffset(2027, 1, 2, 3, 4, 5, TimeSpan.Zero));
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
