using System;
using LogGrokX.Controls;
using LogGrokX.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Tests
{
    [TestClass]
    public class LineViewModelThreadTests
    {
        private const string RegexPattern =
            @"^(?<Time>\d{4}-\d{2}-\d{2}\s[^\s]+)\s+(?<Level>[^\s]+)\s+(?<Thread>[^\s]+)\s+(?<Component>[^\s]+)\s+(?<Message>.*)$";

        private const string Source =
            "2024-01-15 08:32:11.482 INFO ThreadA Component1 hello world";

        [TestMethod]
        public void GetComponentSpanReturnsThreadComponent()
        {
            var meta = new LogMetaInformation(new LogFormat { Regex = RegexPattern });
            var threadIndex = Array.IndexOf(meta.FieldNames, "Thread");
            Assert.AreEqual(2, threadIndex);

            var line = CreateLine(meta);

            Assert.AreEqual("ThreadA", line.GetComponentSpan(threadIndex).ToString());
        }

        [TestMethod]
        public void GetComponentSpanReturnsEmptyForOutOfRangeIndex()
        {
            var meta = new LogMetaInformation(new LogFormat { Regex = RegexPattern });
            var line = CreateLine(meta);

            Assert.AreEqual(0, line.GetComponentSpan(-1).Length);
            Assert.AreEqual(0, line.GetComponentSpan(meta.FieldNames.Length).Length);
        }

        [TestMethod]
        public void GetFieldTextReturnsComponentText()
        {
            var meta = new LogMetaInformation(new LogFormat { Regex = RegexPattern });
            var line = CreateLine(meta);

            Assert.AreEqual("ThreadA", line.GetFieldText(Array.IndexOf(meta.FieldNames, "Thread")));
            Assert.AreEqual("hello world", line.GetFieldText(Array.IndexOf(meta.FieldNames, "Message")));
        }

        [TestMethod]
        public void GetComponentSpanDistinguishesDifferentThreads()
        {
            var meta = new LogMetaInformation(new LogFormat { Regex = RegexPattern });
            var threadIndex = Array.IndexOf(meta.FieldNames, "Thread");

            var first = CreateLine(meta);
            var second = CreateLine(meta, Source.Replace("ThreadA", "ThreadB"));

            Assert.IsFalse(first.GetComponentSpan(threadIndex).SequenceEqual(second.GetComponentSpan(threadIndex)));
            Assert.IsTrue(first.GetComponentSpan(threadIndex).SequenceEqual(first.GetComponentSpan(threadIndex)));
        }

        private static LineViewModel CreateLine(LogMetaInformation meta, string source = Source)
        {
            var parser = new RegexBasedLineParser(meta);
            return new LineViewModel(0, source, parser, new Selection(),
                new TransformationPerformer(Array.Empty<string>()));
        }
    }
}
