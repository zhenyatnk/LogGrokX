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
    public void CycleTimeline_UpdatesBothViewsAndPersistsAllThreeStates()
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
                Assert.AreEqual(Dock.Top, log.TimelineDock);
                Assert.AreEqual(Dock.Top, merged.TimelineDock);
                Assert.IsTrue(viewModel.IsTimelineVisible);

                foreach (var (visible, dock) in new[]
                {
                    (true, Dock.Bottom), (false, Dock.Bottom), (true, Dock.Top), (true, Dock.Bottom)
                })
                {
                    viewModel.CycleTimelineCommand.Execute(null);
                    Assert.AreEqual(visible, viewModel.IsTimelineVisible);
                    Assert.AreEqual(visible, log.IsTimelineVisible);
                    Assert.AreEqual(visible, merged.IsTimelineVisible);
                    Assert.AreEqual(dock, log.TimelineDock);
                    Assert.AreEqual(dock, merged.TimelineDock);

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

    private static MainWindowViewModel CreateViewModel(ApplicationSettings? settings = null)
    {
        settings ??= ApplicationSettings.Instance();
        return new MainWindowViewModel(
            settings,
            new SearchAutocompleteCache(),
            new SavedSearchPatternStore(),
            new UiThemeService(),
            new TimelinePlacementService(settings),
            new TextZoomService(settings),
            new ThreadGroupingService(settings),
            new MergedFilesViewService(settings),
            new UpdateCheckService(settings),
            documents => new MarkedLinesViewModel(documents));
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
