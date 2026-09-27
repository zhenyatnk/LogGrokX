using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Tests
{
    [TestClass]
    public class TextModelTests
    {
        private const string Json = "{\"a\":1,\"b\":[1,2]}";

        [TestMethod]
        public void CollapsedTailOfManyLinesIsCopiedFully()
        {
            var lines = Enumerable.Range(0, 50).Select(i => $"line {i}").ToArray();
            var model = new TextModel(1, string.Join("\n", lines));
            Assert.IsNotNull(model.CollapsibleRanges);

            var collapsed = model.CollapsibleRanges!.Select(r => r.start).ToHashSet();
            var displayed = model.GetDisplayedText(collapsed);

            Assert.IsFalse(displayed.Contains(">>>"), displayed);
            StringAssert.Contains(displayed, "line 49");
            StringAssert.Contains(displayed, "line 25");
        }

        [TestMethod]
        public void DisplayedTextOfLongSingleLineIsNotTruncated()
        {
            var source = new string('x', 200_000);
            var model = new TextModel(1, source);

            Assert.AreEqual(source, model.GetDisplayedText(null));
        }

        [TestMethod]
        public void DisplayedTextOfLongMultiLineIsNotTruncated()
        {
            var longLine = new string('y', 200_000);
            var source = "first\n" + longLine + "\nlast";
            var model = new TextModel(1, source);

            var displayed = model.GetDisplayedText(null);

            StringAssert.Contains(displayed, longLine);
            Assert.IsFalse(displayed.Contains("..."));
            StringAssert.StartsWith(displayed, "first");
            StringAssert.EndsWith(displayed, "last");
        }

        [TestMethod]
        public void ExpandedJsonIsIndented()
        {
            var model = new TextModel(1, Json);

            var displayed = model.GetDisplayedText(new HashSet<int>());

            StringAssert.Contains(displayed, "\"a\": 1");
            StringAssert.Contains(displayed, "\"b\": [");
        }

        [TestMethod]
        public void CollapsedOuterJsonIsInlined()
        {
            var model = new TextModel(1, Json);
            Assert.IsNotNull(model.CollapsibleRanges);

            var root = model.CollapsibleRanges!.OrderByDescending(r => r.length).First();

            var displayed = model.GetDisplayedText(new HashSet<int> { root.start });

            StringAssert.Contains(displayed, "\"a\": 1");
            StringAssert.Contains(displayed, "\"b\": [1,2]");
            Assert.IsFalse(displayed.Contains('\n'), $"Expected inline json but got: {displayed}");
        }

        [TestMethod]
        public void CollapsedInnerJsonIsInlined()
        {
            var model = new TextModel(1, Json);
            Assert.IsNotNull(model.CollapsibleRanges);

            var inner = model.CollapsibleRanges!.OrderBy(r => r.length).First();

            var displayed = model.GetDisplayedText(new HashSet<int> { inner.start });

            StringAssert.Contains(displayed, "\"b\": [1,2]");
            StringAssert.Contains(displayed, "\"a\": 1");
        }

        [TestMethod]
        public void WholeLineAndMessageComponentHaveSameCollapsibleStarts()
        {
            const string prefix = "2024-01-01 10:00:00 INF app ";
            const string json = "{\"a\":{\"b\":1,\"c\":2}}";
            var whole = new TextModel(1, prefix + json);
            var component = new TextModel(2, json);

            CollectionAssert.AreEqual(
                whole.CollapsibleRanges!.Select(r => r.start).ToList(),
                component.CollapsibleRanges!.Select(r => r.start).ToList());
        }

        [TestMethod]
        public void NullFoldingStateKeepsExpandedJson()
        {
            var model = new TextModel(1, Json);

            var displayed = model.GetDisplayedText(null);

            StringAssert.Contains(displayed, "\"a\": 1");
        }
    }
}
