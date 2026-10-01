using System;
using System.Linq;
using System.Threading;
using System.Windows.Controls;
using LogGrokX.Controls;
using LogGrokX.Controls.GridView;
using LogGrokX.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Tests
{
    [TestClass]
    public class GridViewFactoryTests
    {
        private const string RegexPattern =
            @"^(?<Time>\d{4}-\d{2}-\d{2}\s[^\s]+)\s+(?<Level>[^\s]+)\s+(?<Thread>[^\s]+)\s+(?<Component>[^\s]+)\s+(?<Message>.*)$";

        private const string Source =
            "2024-01-15 08:32:11.482 INFO ThreadA Component1 hello world";

        [TestMethod]
        public void CreateViewAssignsContentTextProvider()
        {
            RunOnSta(() =>
            {
                var meta = new LogMetaInformation(new LogFormat { Regex = RegexPattern });
                var factory = new GridViewFactory(meta, false, null);

                var view = (System.Windows.Controls.GridView)factory.CreateView(null);

                Assert.AreEqual(meta.FieldNames.Length + 2, view.Columns.Count);

                var columns = view.Columns.Cast<LogGridViewColumn>().ToArray();

                Assert.IsNull(columns[0].ContentTextProvider);
                Assert.IsNotNull(columns[1].ContentTextProvider);

                for (var i = 0; i < meta.FieldNames.Length; i++)
                    Assert.IsNotNull(columns[i + 2].ContentTextProvider);

                Assert.IsFalse(columns[1].FitToContent);
                for (var i = 0; i < meta.FieldNames.Length; i++)
                    Assert.AreEqual(meta.FieldNames[i].Equals("Message", StringComparison.OrdinalIgnoreCase),
                        columns[i + 2].FitToContent);

                var line = CreateLine(meta);

                Assert.AreEqual("0", columns[1].ContentTextProvider!(line));
                Assert.AreEqual("ThreadA",
                    columns[2 + Array.IndexOf(meta.FieldNames, "Thread")].ContentTextProvider!(line));
                Assert.AreEqual("hello world",
                    columns[2 + Array.IndexOf(meta.FieldNames, "Message")].ContentTextProvider!(line));
            });
        }

        private static LineViewModel CreateLine(LogMetaInformation meta, string source = Source)
        {
            var parser = new RegexBasedLineParser(meta);
            return new LineViewModel(0, source, parser, new Selection(),
                new TransformationPerformer(Array.Empty<string>()));
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
