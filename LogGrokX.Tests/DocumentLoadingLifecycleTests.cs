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
                using var viewModel = new MainWindowViewModel(
                    settings, new SearchAutocompleteCache(), new SavedSearchPatternStore(), new UiThemeService(),
                    new TimelinePlacementService(settings), new TextZoomService(settings),
                    new ThreadGroupingService(settings), new MergedFilesViewService(),
                    new UpdateCheckService(settings), documents => new MarkedLinesViewModel(documents));

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
