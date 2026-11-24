#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Windows.Threading;
using LogGrokX.Data;
using LogGrokX.MarkedLines;
using LogGrokX.Platform;
using LogGrokX.Search;
using LogGrokX.Theming;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Tests;

[TestClass]
[DoNotParallelize]
public class DocumentLoadingLifecycleTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ClosingDocument_ReleasesLoadingResources(bool waitUntilLoaded)
    {
        RunOnDispatcher(() =>
        {
            var fileName = Path.Combine(Path.GetTempPath(), $"loggrokx-close-{Guid.NewGuid():N}.log");
            try
            {
                var lineCount = WriteLog(fileName, waitUntilLoaded ? 4 : 96);
                var settings = new ApplicationSettings
                {
                    Profiles =
                    [
                        new ProfileSettings
                        {
                            Name = "Lifecycle test",
                            InheritLegacySettings = false,
                            LogFormats =
                            [
                                new LogFormat
                                {
                                    Regex = @"^(?<Time>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}) (?<Level>\S+) (?<Thread>\S+) (?<Component>\S+) (?<Message>.*)",
                                    IndexedFields = ["Level", "Thread", "Component"],
                                    TimeField = "Time",
                                    TimeFormat = "yyyy-MM-dd HH:mm:ss.fff"
                                }
                            ]
                        }
                    ],
                    SelectedProfile = "Lifecycle test"
                };
                using var viewModel = TestServices.CreateMain(settings);

                var references = OpenAndCloseDocument(viewModel, fileName, lineCount, waitUntilLoaded);

                using (var stream = new FileStream(fileName, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    Assert.IsTrue(stream.CanRead);

                PumpUntil(() =>
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    return references.Values.All(reference => !reference.IsAlive);
                }, TimeSpan.FromSeconds(3));

                foreach (var (name, reference) in references)
                    Assert.IsFalse(reference.IsAlive, $"{name} is retained after closing the document.");
                Assert.AreEqual(0, viewModel.Documents.Count);
                GC.KeepAlive(viewModel);
            }
            finally
            {
                File.Delete(fileName);
            }
        });
    }

    [TestMethod]
    public void OpeningEmptyFile_MarksDocumentAsEmpty()
    {
        RunOnDispatcher(() =>
        {
            var fileName = Path.Combine(Path.GetTempPath(), $"loggrokx-empty-{Guid.NewGuid():N}.log");
            try
            {
                File.WriteAllBytes(fileName, Array.Empty<byte>());
                var settings = new ApplicationSettings
                {
                    Profiles =
                    [
                        new ProfileSettings
                        {
                            Name = "Empty file test",
                            InheritLegacySettings = false,
                            LogFormats =
                            [
                                new LogFormat
                                {
                                    Regex = @"^(?<Message>.*)",
                                    IndexedFields = [],
                                    TimeField = string.Empty,
                                    TimeFormat = string.Empty
                                }
                            ]
                        }
                    ],
                    SelectedProfile = "Empty file test"
                };
                using var viewModel = TestServices.CreateMain(settings);

                viewModel.AddDocument(fileName);
                var document = viewModel.CurrentDocument;
                Assert.IsNotNull(document);
                Assert.IsTrue(PumpUntil(() => !document.LogViewModel.IsLoading, TimeSpan.FromSeconds(10)));

                Assert.AreEqual(0, document.LineCount);
                Assert.IsTrue(document.IsFileEmpty);
            }
            finally
            {
                File.Delete(fileName);
            }
        });
    }

    [TestMethod]
    public void RestoringDocument_AppliesStateAndWaitsForLoadingBeforeNavigation()
    {
        RunOnDispatcher(() =>
        {
            var fileName = Path.Combine(Path.GetTempPath(), $"loggrokx-restore-{Guid.NewGuid():N}.log");
            File.WriteAllLines(fileName, Enumerable.Range(0, 8).Select(index => $"line {index}"));
            try
            {
                using var viewModel = TestServices.CreateMain(new ApplicationSettings());
                var state = new UpdateRestartDocument(fileName)
                {
                    Searches =
                    [
                        new UpdateRestartSearch("line 1", true, false),
                        new UpdateRestartSearch("line [24]", false, true)
                    ],
                    CurrentSearchIndex = 1,
                    MarkedLines = [1, 6],
                    CurrentLine = 6,
                    ScrollPosition = 2
                };

                viewModel.RestoreDocuments([state]);
                var document = viewModel.CurrentDocument!;
                var currentIndex = -2;
                var scrollIndex = -2;
                document.LogViewModel.NavigateToLineRequest.RestorePosition += (current, scroll) =>
                    (currentIndex, scrollIndex) = (current, scroll);

                Assert.IsTrue(PumpUntil(() => !document.LogViewModel.IsLoading && currentIndex != -2,
                    TimeSpan.FromSeconds(10)));
                Assert.AreEqual(2, document.SearchViewModel.Documents.Count);
                Assert.AreEqual("line [24]", document.SearchViewModel.TextToSearch);
                Assert.IsFalse(document.SearchViewModel.CurrentDocument!.SearchPattern.IsCaseSensitive);
                Assert.IsTrue(document.SearchViewModel.CurrentDocument.SearchPattern.UseRegex);
                CollectionAssert.AreEqual(new[] { 1, 6 }, document.MarkedLines.Snapshot().ToArray());
                Assert.AreEqual(6, currentIndex);
                Assert.AreEqual(2, scrollIndex);
            }
            finally
            {
                File.Delete(fileName);
            }
        });
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Dictionary<string, WeakReference> OpenAndCloseDocument(MainWindowViewModel viewModel,
        string fileName, int totalLineCount, bool waitUntilLoaded)
    {
        viewModel.AddDocument(fileName);
        var document = viewModel.CurrentDocument!;
        Assert.IsNotNull(document);
        Assert.IsTrue(PumpUntil(() => waitUntilLoaded
            ? !document.LogViewModel.IsLoading
            : document.LineCount >= 10000, TimeSpan.FromSeconds(10)));
        if (!waitUntilLoaded)
            Assert.IsTrue(document.LineCount < totalLineCount, "The document must be closed before loading finishes.");

        var references = new Dictionary<string, WeakReference>
        {
            [nameof(DocumentViewModel)] = new(document),
            [nameof(LogViewModel)] = new(document.LogViewModel),
            ["Indexer"] = new(document.Indexer),
            [nameof(TimeIndex)] = new(document.TimeIndex)
        };

        viewModel.CurrentDocument = null;
        viewModel.Documents.Remove(document);
        viewModel.OnDocumentCloseCommand.Execute(document);
        return references;
    }

    private static int WriteLog(string fileName, int sizeMiB)
    {
        var line = "2026-01-01 12:34:56.789 INFO 123 Component " + new string('x', 64) + "\r\n";
        var buffer = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(line, 8192)));
        using var stream = File.Create(fileName);
        var lineCount = 0;
        while (stream.Length < (long)sizeMiB * 1024 * 1024)
        {
            stream.Write(buffer);
            lineCount += 8192;
        }
        return lineCount;
    }

    private static bool PumpUntil(Func<bool> condition, TimeSpan timeout)
    {
        var frame = new DispatcherFrame();
        var stopwatch = Stopwatch.StartNew();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += (_, _) =>
        {
            if (condition() || stopwatch.Elapsed >= timeout)
                frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
        return condition();
    }

    private static void RunOnDispatcher(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            try
            {
                action();
            }
            catch (Exception exception)
            {
                error = exception;
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(30)), "Document loading or closing did not finish.");
        if (error != null)
            throw error;
    }
}
