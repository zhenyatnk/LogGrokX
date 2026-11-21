using System;
using System.Threading;
using LogGrokX.Controls.TextRender;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Tests
{
    [TestClass]
    public class FoldingStateSharingTests
    {
        [TestMethod]
        public void DefaultSettingsCollapseJsonRegardlessOfSize()
        {
            var model = new TextModel(42, "{\"a\":{\"b\":1,\"c\":2}}");
            var state = new TextViewSharedFoldingState();

            var collapsed = state.GetDefaultSettings(model.CollapsibleRanges!, model.Count);

            Assert.AreEqual(model.CollapsibleRanges!.Count, collapsed.Count);
            foreach (var range in model.CollapsibleRanges)
                Assert.IsTrue(collapsed.Contains(range.start));
        }

        [TestMethod]
        public void ExpandingAndCollapsingOneTextViewUpdatesAnotherViewOfTheSameText()
        {
            RunOnSta(() =>
            {
                const string json = "{\"a\":{\"b\":1,\"c\":2}}";
                var state = new TextViewSharedFoldingState();
                var first = new TextView();
                var second = new TextView();
                TextView.SetSharedFoldingState(first, state);
                TextView.SetSharedFoldingState(second, state);
                first.TextModel = new TextModel(42, json);
                second.TextModel = new TextModel(42, json);

                Assert.IsTrue(first.FoldingManager!.ExpandRecursivelyCommand!.CanExecute(null));
                Assert.IsTrue(second.FoldingManager!.ExpandRecursivelyCommand!.CanExecute(null));

                first.FoldingManager.ExpandRecursivelyCommand.Execute(null);
                Assert.IsTrue(second.FoldingManager.CollapseRecursivelyCommand!.CanExecute(null));
                Assert.IsFalse(second.FoldingManager.ExpandRecursivelyCommand.CanExecute(null));

                first.FoldingManager.CollapseRecursivelyCommand!.Execute(null);

                Assert.IsTrue(first.FoldingManager.ExpandRecursivelyCommand.CanExecute(null));
                Assert.IsTrue(second.FoldingManager.ExpandRecursivelyCommand.CanExecute(null));
                Assert.IsFalse(second.FoldingManager.CollapseRecursivelyCommand.CanExecute(null));
            });
        }

        [TestMethod]
        public void ToggleInOneTextViewIsNotPropagatedToDifferentUniqueId()
        {
            RunOnSta(() =>
            {
                const string json = "{\"a\":{\"b\":1,\"c\":2}}";
                var state = new TextViewSharedFoldingState();
                var first = new TextView();
                var other = new TextView();
                TextView.SetSharedFoldingState(first, state);
                TextView.SetSharedFoldingState(other, state);
                first.TextModel = new TextModel(1, json);
                other.TextModel = new TextModel(2, json);
                first.FoldingManager!.ExpandRecursivelyCommand!.Execute(null);
                other.FoldingManager!.ExpandRecursivelyCommand!.Execute(null);

                first.FoldingManager.CollapseRecursivelyCommand!.Execute(null);

                Assert.IsFalse(first.FoldingManager.CollapseRecursivelyCommand.CanExecute(null));
                Assert.IsTrue(other.FoldingManager!.CollapseRecursivelyCommand!.CanExecute(null));
            });
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
