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
    }
}
