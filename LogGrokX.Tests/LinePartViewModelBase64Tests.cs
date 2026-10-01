using System;
using System.ComponentModel;
using System.Collections.Generic;
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
    }
}
