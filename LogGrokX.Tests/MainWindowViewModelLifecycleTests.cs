#nullable enable

using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using LogGrokX.AvalonDockExtensions;
using LogGrokX.MarkedLines;
using LogGrokX.Platform;
using LogGrokX.Search;
using LogGrokX.Theming;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.Configuration;

namespace LogGrokX.Tests;

[TestClass]
public class MainWindowViewModelLifecycleTests
{
    [TestMethod]
    public void AddDocument_ExistingPathActivatesOpenDocument()
    {
        RunOnSta(() =>
        {
            var fileName = Path.Combine(Path.GetTempPath(), $"loggrokx-{Guid.NewGuid():N}.log");
            File.WriteAllText(fileName, "2026-01-01 00:00:00 INFO 1 Test Message");
            try
            {
                using var viewModel = CreateViewModel();
                viewModel.AddDocument(fileName);
                var first = viewModel.CurrentDocument;
                Assert.IsNotNull(first);

                var documentStyle = new Style();
                var styleSelector = new DocumentLayoutItemStyleSelector { DocumentStyle = documentStyle };
                var documentContent = new ContentControl { Content = first };
                Assert.AreSame(documentStyle, styleSelector.SelectStyle(documentContent, documentContent));
                var markedLinesContent = new ContentControl { Content = viewModel.MarkedLinesViewModel };
                Assert.IsNull(styleSelector.SelectStyle(markedLinesContent, markedLinesContent));
                var mergedContent = new ContentControl { Content = viewModel.MergedViewModel };
                Assert.IsNull(styleSelector.SelectStyle(mergedContent, mergedContent));

                var alternatePath = Path.Combine(Path.GetDirectoryName(fileName)!, ".", Path.GetFileName(fileName));
                viewModel.AddDocument(alternatePath);

                Assert.AreEqual(1, viewModel.Documents.Count);
                Assert.AreSame(first, viewModel.CurrentDocument);
            }
            finally
            {
                File.Delete(fileName);
            }
        });
    }

    [TestMethod]
    public void RestartUpdate_PassesAllOpenDocumentPaths()
    {
        RunOnSta(() =>
        {
            var firstFile = Path.Combine(Path.GetTempPath(), $"loggrokx-{Guid.NewGuid():N}.log");
            var secondFile = Path.Combine(Path.GetTempPath(), $"loggrokx-{Guid.NewGuid():N}.log");
            File.WriteAllText(firstFile, "2026-01-01 00:00:00 INFO 1 First message");
            File.WriteAllText(secondFile, "2026-01-01 00:00:00 INFO 2 Second message");
            try
            {
                var updates = new TestServices.UpdatesStub();
                updates.SetStatus(new UpdateStatus(UpdateState.Ready, "v2.0", 100));
                using var viewModel = CreateViewModel(updates: updates);
                viewModel.AddDocument(firstFile);
                viewModel.AddDocument(secondFile);

                viewModel.UpdateCommand.Execute(null);

                CollectionAssert.AreEqual(new[] { firstFile, secondFile }, updates.RestartFiles);
            }
            finally
            {
                File.Delete(firstFile);
                File.Delete(secondFile);
            }
        });
    }

    [TestMethod]
    public void Dispose_DocumentsCollectionDoesNotKeepMarkedLinesViewModelAlive()
    {
        RunOnSta(() =>
        {
            var documents = new ObservableCollection<DocumentViewModel>();
            var reference = CreateAndDisposeMarkedLinesViewModel(documents);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            Assert.IsFalse(reference.IsAlive);
            GC.KeepAlive(documents);
        });
    }

    [TestMethod]
    public void RemovingDocument_DocumentDoesNotKeepMarkedLinesViewModelAlive()
    {
        RunOnSta(() =>
        {
            var fileName = Path.Combine(Path.GetTempPath(), $"loggrokx-{Guid.NewGuid():N}.log");
            File.WriteAllText(fileName, "2026-01-01 00:00:00 INFO 1 Test Message");
            try
            {
                var (reference, document) = CreateAndRemoveDocument(fileName);

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                Assert.IsFalse(reference.IsAlive);
                GC.KeepAlive(document);
            }
            finally
            {
                File.Delete(fileName);
            }
        });
    }

    [TestMethod]
    [DoNotParallelize]
    public void TimelineCommands_UpdateBothViewsAndPersistAllThreeStates()
    {
        RunOnSta(() =>
        {
            var settingsPath = Path.Combine(Path.GetTempPath(), $"loggrokx-timeline-{Guid.NewGuid():N}.yaml");
            var fileName = Path.Combine(Path.GetTempPath(), $"loggrokx-{Guid.NewGuid():N}.log");
            File.WriteAllText(fileName, "2026-01-01 00:00:00 INFO 1 Test Message");
            try
            {
                File.WriteAllText(settingsPath, "Settings:\n  ViewSettings:\n    TimelineAtTop: true\n");
                var settings = new ApplicationSettings(settingsPath);
                settings.ViewSettings.TimelineAtTop = true;
                using var viewModel = CreateViewModel(settings);
                viewModel.AddDocument(fileName);
                var log = viewModel.CurrentDocument!.LogViewModel;
                var merged = viewModel.MergedViewModel;
                Assert.IsTrue(log.IsTimelineAtTop);
                Assert.IsTrue(merged.IsTimelineAtTop);
                Assert.IsTrue(viewModel.IsTimelineVisible);

                foreach (var (command, visible, dock) in new[]
                {
                    (viewModel.ShowTimelineAtBottomCommand, true, Dock.Bottom),
                    (viewModel.HideTimelineCommand, false, Dock.Bottom),
                    (viewModel.ShowTimelineAtTopCommand, true, Dock.Top),
                    (viewModel.ShowTimelineAtBottomCommand, true, Dock.Bottom)
                })
                {
                    command.Execute(null);
                    Assert.AreEqual(visible, viewModel.IsTimelineVisible);
                    Assert.AreEqual(visible, log.IsTimelineVisible);
                    Assert.AreEqual(visible, merged.IsTimelineVisible);
                    Assert.AreEqual(dock == Dock.Top, log.IsTimelineAtTop);
                    Assert.AreEqual(dock == Dock.Top, merged.IsTimelineAtTop);

                    var saved = new ViewSettings();
                    new ConfigurationBuilder().AddYamlFile(settingsPath, false, false).Build()
                        .GetSection("Settings:ViewSettings").Bind(saved);
                    Assert.AreEqual(visible, saved.TimelineVisible);
                    Assert.AreEqual(dock == Dock.Top, saved.TimelineAtTop);
                }
            }
            finally
            {
                File.Delete(fileName);
                File.Delete(settingsPath);
            }
        });
    }

    [TestMethod]
    public void ActivateSearchCommand_FocusesSearchOfCurrentDocument()
    {
        RunOnSta(() =>
        {
            var fileName = Path.Combine(Path.GetTempPath(), $"loggrokx-{Guid.NewGuid():N}.log");
            File.WriteAllText(fileName, "2026-01-01 00:00:00 INFO 1 Test Message");
            try
            {
                using var viewModel = CreateViewModel();
                viewModel.ActivateSearchCommand.Execute(null);

                viewModel.AddDocument(fileName);
                var document = viewModel.CurrentDocument!;
                var focusRequests = 0;
                document.SearchViewModel.SetFocusRequest.SetFocus += () => focusRequests++;

                viewModel.ActivateSearchCommand.Execute(null);

                Assert.AreEqual(1, focusRequests);
            }
            finally
            {
                File.Delete(fileName);
            }
        });
    }

    [TestMethod]
    [DoNotParallelize]
    public void ActivateSearchCommand_FocusesMergedSearchWhenMergedViewIsActive()
    {
        RunOnSta(() =>
        {
            var settingsPath = Path.Combine(Path.GetTempPath(), $"loggrokx-merged-{Guid.NewGuid():N}.yaml");
            File.WriteAllText(settingsPath, "Settings:\n  ViewSettings:\n    MergedFilesView: true\n");
            try
            {
                var settings = new ApplicationSettings(settingsPath);
                using var viewModel = CreateViewModel(settings);
                viewModel.IsMergedView = true;
                viewModel.IsMergedViewActive = true;
                var focusRequests = 0;
                viewModel.MergedViewModel.Search.SetFocusRequest.SetFocus += () => focusRequests++;

                viewModel.ActivateSearchCommand.Execute(null);

                Assert.AreEqual(1, focusRequests);
            }
            finally
            {
                File.Delete(settingsPath);
            }
        });
    }

    [TestMethod]
    public void ClearSearchCommand_ClearsCurrentDocumentWithoutFocusRequest()
    {
        RunOnSta(() =>
        {
            var fileName = Path.Combine(Path.GetTempPath(), $"loggrokx-{Guid.NewGuid():N}.log");
            File.WriteAllText(fileName, "2026-01-01 00:00:00 INFO 1 Synthetic message");
            try
            {
                using var viewModel = CreateViewModel();
                viewModel.ClearSearchCommand.Execute(null);
                viewModel.AddDocument(fileName);
                var search = viewModel.CurrentDocument!.SearchViewModel;
                var focusRequests = 0;
                search.SetFocusRequest.SetFocus += () => focusRequests++;
                search.TextToSearch = "alpha";

                viewModel.ClearSearchCommand.Execute(null);

                Assert.AreEqual(string.Empty, search.TextToSearch);
                Assert.IsNull(search.CurrentDocument);
                Assert.IsTrue(search.IsFilterDisabled);
                Assert.AreEqual(0, focusRequests);
                viewModel.Dispose();
            }
            finally
            {
                File.Delete(fileName);
            }
        });
    }

    [TestMethod]
    [DoNotParallelize]
    public void ClearSearchCommand_ClearsMergedSearchWithoutFocusRequest()
    {
        RunOnSta(() =>
        {
            var settingsPath = Path.Combine(Path.GetTempPath(), $"loggrokx-merged-{Guid.NewGuid():N}.yaml");
            File.WriteAllText(settingsPath, "Settings:\n  ViewSettings:\n    MergedFilesView: true\n");
            try
            {
                var settings = new ApplicationSettings(settingsPath);
                using var viewModel = CreateViewModel(settings);
                viewModel.IsMergedView = true;
                viewModel.IsMergedViewActive = true;
                var search = viewModel.MergedViewModel.Search;
                var focusRequests = 0;
                search.SetFocusRequest.SetFocus += () => focusRequests++;
                search.AppendToSearchCommand.Execute("alpha");
                Assert.IsNotNull(search.CurrentDocument);

                viewModel.ClearSearchCommand.Execute(null);

                Assert.AreEqual(string.Empty, search.TextToSearch);
                Assert.IsNull(search.CurrentDocument);
                Assert.IsTrue(search.IsFilterDisabled);
                Assert.AreEqual(0, focusRequests);
            }
            finally
            {
                File.Delete(settingsPath);
            }
        });
    }

    private static MainWindowViewModel CreateViewModel(ApplicationSettings? settings = null,
        TestServices.UpdatesStub? updates = null)
    {
        settings ??= ApplicationSettings.Instance();
        return TestServices.CreateMain(settings, updates);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateAndDisposeMarkedLinesViewModel(
        ObservableCollection<DocumentViewModel> documents)
    {
        var viewModel = new MarkedLinesViewModel(documents);
        viewModel.Dispose();
        return new WeakReference(viewModel);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference reference, DocumentViewModel document) CreateAndRemoveDocument(string fileName)
    {
        using var viewModel = CreateViewModel();
        viewModel.AddDocument(fileName);
        var document = viewModel.CurrentDocument!;
        var reference = new WeakReference(viewModel.MarkedLinesViewModel);
        viewModel.Documents.Remove(document);
        return (reference, document);
    }

    private static void RunOnSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                error = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null)
            throw error;
    }
}
