#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Threading;
using LogGrokX.MarkedLines;
using LogGrokX.MergedView;
using LogGrokX.Search;
using LogGrokX.Theming;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Tests;

[TestClass]
public class MergedRebuildTests
{
    [TestMethod]
    public void EnablingMergeYieldsToDispatcherBeforePublishingRows()
    {
        RunOnSta(() =>
        {
            using var fixture = new Fixture();
            var merged = fixture.Merged;
            fixture.Main.IsMergedView = true;
            Assert.IsTrue(merged.IsLoading);
            Assert.AreEqual(0, merged.Lines.Count);
            var handledInput = false;
            var handledBeforePublish = false;
            Dispatcher.CurrentDispatcher.BeginInvoke(() => handledInput = true, DispatcherPriority.Send);
            merged.Rebuilt += () => handledBeforePublish = handledInput;
            WaitFor(() => !merged.IsLoading && merged.TotalLineCount == 12);
            Assert.IsTrue(handledBeforePublish);
            Assert.AreEqual(12, merged.Lines.Count);
        });
    }

    [TestMethod]
    public void FiltersReuseMergedOrderAndKeepFullTimelineAndPreviousSnapshot()
    {
        RunOnSta(() =>
        {
            using var fixture = new Fixture();
            fixture.Enable();
            var merged = fixture.Merged;
            var original = merged.GetSearchSnapshot();
            var originalLines = original.Lines.ToArray();
            var maximum = merged.TimeRangeFilter.Maximum;
            var sourceField = Array.IndexOf(merged.MetaInformation.FieldNames, "Source");
            merged.SelectedItems = new[] { merged.Lines[0] };
            fixture.Change(() => merged.ExcludeCommand.Execute(sourceField));
            Assert.AreEqual(6, merged.Lines.Count);
            Assert.AreSame(original.Data, merged.GetSearchSnapshot().Data);
            Assert.AreSame(original.Data.TimeIndex, merged.MergedTimeIndex);
            Assert.AreSame(original.Data.TimelineSegments, merged.TimelineSegments);
            Assert.AreEqual(maximum, merged.TimeRangeFilter.Maximum);
            Assert.AreEqual(12, merged.TotalLineCount);
            CollectionAssert.AreEqual(originalLines, original.Lines);

            var lower = original.Data.Lines[6].Ticks;
            fixture.Change(() => merged.TimeRangeFilter.LowerValue = lower);
            Assert.AreEqual(3, merged.Lines.Count);
            Assert.AreSame(original.Data, merged.GetSearchSnapshot().Data);
            Assert.AreEqual((double)lower, merged.TimeRangeFilter.LowerValue);
            Assert.AreEqual(maximum, merged.TimeRangeFilter.Maximum);
            var snapshot = merged.GetSearchSnapshot();
            CollectionAssert.AreEqual(new[] { 9, 10, 11 }, snapshot.FullIndexMap!);
            merged.FirstVisibleIndex = 0;
            Assert.AreEqual(9, merged.ScrollPosition);
            var target = -1;
            merged.NavigateToLineRequest.Navigate += (index, _) => target = index;
            merged.NavigateTo(10);
            Assert.AreEqual(1, target);

            fixture.Change(() => merged.ClearFiltersCommand.Execute(null));
            Assert.AreSame(original.Data.Lines, merged.GetSearchSnapshot().Lines);
            Assert.IsNull(merged.GetSearchSnapshot().FullIndexMap);
            Assert.AreEqual(12, merged.Lines.Count);
        });
    }

    [TestMethod]
    public void TimeRangeKeepsUntimedRowsInMixedMerge()
    {
        RunOnSta(() =>
        {
            using var fixture = new Fixture(plainLast: true);
            fixture.Enable();
            var merged = fixture.Merged;
            var original = merged.GetSearchSnapshot();
            var lower = original.Lines[4].Ticks;
            fixture.Change(() => merged.TimeRangeFilter.LowerValue = lower);
            Assert.AreEqual(8, merged.Lines.Count);
            Assert.AreEqual(4, merged.GetSearchSnapshot().Lines.Count(line => line.Ticks < 0));
            Assert.AreSame(original.Data, merged.GetSearchSnapshot().Data);
        });
    }

    [TestMethod]
    public void SourceSelectionCreatesNewDataWithoutChangingPreviousSnapshot()
    {
        RunOnSta(() =>
        {
            using var fixture = new Fixture();
            fixture.Enable();
            var merged = fixture.Merged;
            var previous = merged.GetSearchSnapshot();
            fixture.Change(() => merged.AvailableDocuments[0].IsSelected = false);
            Assert.AreEqual(8, merged.Lines.Count);
            Assert.AreNotSame(previous.Data, merged.GetSearchSnapshot().Data);
            Assert.AreEqual(12, previous.Lines.Length);
            Assert.AreEqual(3, previous.Sources.Length);
            Assert.IsTrue(merged.Lines.Cast<MergedLineViewModel>().All(line => line.Source.Document != fixture.Main.Documents[0]));
            fixture.Change(() => merged.AvailableDocuments[0].IsSelected = true);
            Assert.AreEqual(12, merged.Lines.Count);
        });
    }

    [TestMethod]
    public void RapidProfileAndSourceChangesPublishOnlyLatestRequest()
    {
        RunOnSta(() =>
        {
            using var fixture = new Fixture();
            var merged = fixture.Merged;
            fixture.Main.IsMergedView = true;
            merged.AvailableDocuments[0].IsSelected = false;
            merged.AvailableDocuments[1].IsSelected = false;
            fixture.Main.SelectedProfile = fixture.Main.Profiles.Single(profile => profile.Name == "Plain text");
            var published = new List<(int Total, string[] Fields, DocumentViewModel[] Documents)>();
            merged.Rebuilt += () =>
            {
                published.Add((merged.TotalLineCount, merged.MetaInformation.FieldNames,
                    merged.GetSearchSnapshot().Sources.Select(source => source.Document).ToArray()));
            };
            WaitFor(() => !merged.IsLoading && fixture.Main.Documents.All(document => !document.LogViewModel.IsLoading));
            Assert.IsGreaterThan(0, published.Count);
            foreach (var result in published)
            {
                Assert.IsLessThanOrEqualTo(4, result.Total);
                CollectionAssert.AreEqual(new[] { "Text" }, result.Fields);
                CollectionAssert.AreEqual(new[] { fixture.Main.Documents[2] }, result.Documents);
            }
            Assert.AreEqual(4, merged.Lines.Count);
            Assert.AreEqual(1, merged.GetSearchSnapshot().Sources.Length);
        });
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void PendingMergeDoesNotPublishAfterDisableOrDispose(bool dispose)
    {
        RunOnSta(() =>
        {
            using var fixture = new Fixture();
            var merged = fixture.Merged;
            var published = 0;
            merged.Rebuilt += () => published++;
            fixture.Main.IsMergedView = true;
            if (dispose)
                fixture.Main.Dispose();
            else
                fixture.Main.IsMergedView = false;
            WaitFor(() => !merged.IsLoading);
            Assert.AreEqual(0, published);
            Assert.AreEqual(0, merged.TotalLineCount);
        });
    }

    [TestMethod]
    public void SearchesShareSnapshotAndFollowFilteredIndexesAndQueryChanges()
    {
        RunOnSta(() =>
        {
            using var fixture = new Fixture();
            fixture.Enable();
            var merged = fixture.Merged;
            using var first = new MergedSearchDocumentViewModel(merged, new SearchPattern("match", false, false));
            using var second = new MergedSearchDocumentViewModel(merged, new SearchPattern("match", false, false));
            WaitFor(() => !first.IsSearching && !second.IsSearching);
            Assert.AreSame(merged.GetSearchSnapshot(), first.Snapshot);
            Assert.AreSame(first.Snapshot, second.Snapshot);
            Assert.AreEqual(12, first.Lines.Count);
            var previous = first.Snapshot;
            merged.SelectedItems = new[] { merged.Lines[0] };
            fixture.Change(() => merged.ExcludeCommand.Execute(Array.IndexOf(merged.MetaInformation.FieldNames, "Source")));
            WaitFor(() => !first.IsSearching && !second.IsSearching);
            Assert.AreSame(first.Snapshot, second.Snapshot);
            Assert.AreSame(previous.Data, first.Snapshot.Data);
            Assert.AreEqual(12, previous.Lines.Length);
            Assert.AreEqual(6, first.Lines.Count);
            CollectionAssert.AreEqual(new[] { 3, 4, 5, 9, 10, 11 },
                first.Lines.Cast<MergedLineViewModel>().Select(line => line.MergedIndex).ToArray());
            first.SearchPattern = new SearchPattern("match 3", false, false);
            WaitFor(() => !first.IsSearching);
            Assert.AreEqual(3, first.Lines.Count);
            fixture.Change(() => merged.ClearFiltersCommand.Execute(null));
            WaitFor(() => !first.IsSearching && !second.IsSearching);
            Assert.AreEqual(3, first.Lines.Count);
            Assert.AreEqual(12, second.Lines.Count);
        });
    }

    [TestMethod]
    public void SearchThreadGroupingRemainsScopedToSourceAfterFiltering()
    {
        RunOnSta(() =>
        {
            using var fixture = new Fixture();
            fixture.Enable();
            var merged = fixture.Merged;
            fixture.Main.IsGroupByThread = true;
            merged.CurrentItemIndex = 0;
            using var search = new MergedSearchDocumentViewModel(merged, new SearchPattern("match", false, false));
            WaitFor(() => !search.IsSearching);
            Assert.AreEqual(2, search.Lines.Count);
            Assert.IsTrue(search.Lines.Cast<MergedLineViewModel>().All(line => line.Source.Document == fixture.Main.Documents[0]));
            fixture.Change(() => merged.TimeRangeFilter.LowerValue = merged.GetSearchSnapshot().Data.Lines[6].Ticks);
            WaitFor(() => !search.IsSearching);
            Assert.AreEqual(1, search.Lines.Count);
            Assert.IsTrue(search.Lines.Cast<MergedLineViewModel>().All(line => line.Source.Document == fixture.Main.Documents[0]));
        });
    }

    [TestMethod]
    public void LoadingSourcesEventuallyPublishCompleteMerge()
    {
        RunOnSta(() =>
        {
            using var fixture = new Fixture(linesPerSource: 2000, waitForLoading: false);
            fixture.Main.IsMergedView = true;
            WaitFor(() => !fixture.Merged.IsLoading && fixture.Merged.TotalLineCount == 6000);
            Assert.AreEqual(6000, fixture.Merged.Lines.Count);
            Assert.AreEqual(6000, fixture.Merged.MergedTimeIndex.Count);
        });
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "loggrokx-merge-rebuild-" + Guid.NewGuid().ToString("N"));
        public MainWindowViewModel Main { get; }
        public MergedViewModel Merged => Main.MergedViewModel;

        public Fixture(bool plainLast = false, int linesPerSource = 4, bool waitForLoading = true)
        {
            Directory.CreateDirectory(_directory);
            var settingsPath = Path.Combine(_directory, "appsettings.yaml");
            File.Copy(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
                "LogGrokX", "appsettings.yaml")), settingsPath);
            var settings = ApplicationSettings.LoadFromFile(settingsPath);
            settings.SelectedProfile = "Venn";
            Main = new MainWindowViewModel(settings, new SearchAutocompleteCache(), new SavedSearchPatternStore(),
                new UiThemeService(), new TimelinePlacementService(settings), new TextZoomService(settings),
                new ThreadGroupingService(settings), new MergedFilesViewService(), new UpdateCheckService(settings),
                documents => new MarkedLinesViewModel(documents));
            for (var source = 0; source < 3; source++)
            {
                var path = Path.Combine(_directory, $"sample-{source}.log");
                var lines = Enumerable.Range(0, linesPerSource).Select(line => plainLast && source == 2 ? $"plain match {line}" :
                    new DateTime(2026, 10, 22).AddSeconds(line * 3 + source).ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)
                    + $" - [INFO] - [1:{source + 1}:{line % 2 + 1}:M:U] - [process.exe] - [{(line % 2 == 0 ? "IPC" : "FS")}] - match {line}");
                File.WriteAllLines(path, lines);
                Main.AddDocument(path);
            }
            if (waitForLoading)
                WaitFor(() => Main.Documents.All(document => !document.LogViewModel.IsLoading));
        }

        public void Enable()
        {
            Main.IsMergedView = true;
            WaitFor(() => !Merged.IsLoading && Merged.TotalLineCount == 12);
        }

        public void Change(Action action)
        {
            var revision = Merged.Revision;
            action();
            WaitFor(() => !Merged.IsLoading && Merged.Revision > revision);
        }

        public void Dispose()
        {
            Main.Dispose();
            Directory.Delete(_directory, true);
        }
    }

    private static void WaitFor(Func<bool> ready)
    {
        var frame = new DispatcherFrame();
        var started = DateTime.UtcNow;
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) => { if (ready() || DateTime.UtcNow - started > TimeSpan.FromSeconds(10)) frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
        Assert.IsTrue(ready(), "Merged operation did not finish");
    }

    private static void RunOnSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            try { action(); }
            catch (Exception exception) { error = exception; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }
}
