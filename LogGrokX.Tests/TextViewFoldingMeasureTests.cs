using System;
using System.Linq;
using System.Threading;
using System.Windows;
using LogGrokX.Controls.TextRender;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Tests
{
    [TestClass]
    public class TextViewFoldingMeasureTests
    {
        private static readonly Size Infinite = new(double.PositiveInfinity, double.PositiveInfinity);

        private static string CreateJson(int count) =>
            "{" + string.Join(",", Enumerable.Range(0, count)
                .Select(i => $"\"k{i}\":{{\"a\":{i},\"b\":[1,2,3],\"c\":{{\"d\":\"v{i}\"}}}}")) + "}";

        [TestMethod]
        public void FoldingChangesProduceSameSizeAsFreshTextView()
        {
            RunOnSta(() =>
            {
                var json = CreateJson(200);
                var state = new TextViewSharedFoldingState();
                var textView = CreateTextView(state, json);

                textView.Measure(Infinite);
                var defaultSize = textView.DesiredSize;

                textView.FoldingManager!.ExpandRecursivelyCommand!.Execute(null);
                textView.Measure(Infinite);
                var expandedSize = textView.DesiredSize;

                var fresh = CreateTextView(state, json);
                fresh.Measure(Infinite);
                Assert.AreEqual(expandedSize, fresh.DesiredSize);
                Assert.IsTrue(expandedSize.Height > defaultSize.Height);

                textView.FoldingManager.CollapseRecursivelyCommand!.Execute(null);
                textView.Measure(Infinite);
                var collapsedSize = textView.DesiredSize;
                Assert.IsTrue(collapsedSize.Height <= defaultSize.Height);

                textView.FoldingManager.ResetToDefaultCommand!.Execute(null);
                textView.Measure(Infinite);
                Assert.AreEqual(defaultSize, textView.DesiredSize);
            });
        }

        [TestMethod]
        public void CollapsedJsonSubstitutionIsInlinedAndCached()
        {
            var model = new TextModel(1, CreateJson(3));
            var root = model.CollapsibleRanges!.OrderByDescending(r => r.length).First();

            var first = model.GetCollapsedTextSubstitution(root.start);
            var second = model.GetCollapsedTextSubstitution(root.start);

            Assert.IsFalse(first.ToString().Contains('\n'));
            StringAssert.Contains(first.ToString(), "\"d\": \"v2\"");
            Assert.AreSame(first.SourceString, second.SourceString);
        }

        private static TextView CreateTextView(TextViewSharedFoldingState state, string json)
        {
            var textView = new TextView();
            TextView.SetSharedFoldingState(textView, state);
            textView.TextModel = new TextModel(7, json);
            return textView;
        }

        private static void RunOnSta(Action action)
        {
            Exception error = null;
            var thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    error = e;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (error != null)
                throw error;
        }
    }
}
