#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LogGrokX.Data.Symbols;
using LogGrokX.Platform;
using LogGrokX.Symbols;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Tests;

[TestClass]
[DoNotParallelize]
public sealed class CallStackSymbolsTests
{
    private const string Message = "Call stack:\n 0# test_.dll!86da0\n 1# other.dll!10\n";

    private SymbolOptions _previousOptions = SymbolOptions.Default;
    private string _root = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        _previousOptions = SymbolOptions.Current;
        _root = Path.Combine(Path.GetTempPath(), "LogGrokX.CallStackSymbolsTests." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        File.WriteAllBytes(Path.Combine(_root, "test_.pdb"), [1]);
        SymbolOptions.Current = new SymbolOptions(true, _root, Path.Combine(_root, "src"), Path.Combine(_root, "cache"), false);
    }

    [TestCleanup]
    public void Cleanup()
    {
        SymbolOptions.Current = _previousOptions;
        Directory.Delete(_root, recursive: true);
    }

    [TestMethod]
    public async Task ResolvesFramesThroughSymbolReader()
    {
        var reader = new FakeReader();
        using var service = CreateService(reader);
        var frames = CallStackParser.Parse(Message);

        Assert.AreEqual(FrameSymbolStatus.Pending, service.Get(frames[0]).Status);
        var symbols = await service.ResolveAllAsync(frames);

        Assert.AreEqual(FrameSymbolStatus.Resolved, symbols[0].Status);
        Assert.AreEqual(Path.Combine(_root, "test_.pdb"), symbols[0].PdbPath);
        Assert.AreEqual(@"test_.dll!Run+0x10 [C:\src\main.cpp @ 42]", symbols[0].FormatFrame(frames[0]));
        Assert.AreEqual(FrameSymbolStatus.NoSymbols, symbols[1].Status);
        Assert.AreEqual(FrameSymbolStatus.Resolved, service.Get(frames[0]).Status);
        CollectionAssert.AreEqual(new[] { 0x86da0UL }, reader.Offsets.ToArray());
    }

    [TestMethod]
    public async Task RefreshDropsCachedSymbols()
    {
        var reader = new FakeReader();
        using var service = CreateService(reader);
        var frame = CallStackParser.Parse(Message)[0];
        await service.ResolveAsync(frame);
        var version = service.Version;
        var reset = 0;
        service.Reset += () => reset++;

        service.Refresh();
        await service.ResolveAsync(frame);

        Assert.AreNotEqual(version, service.Version);
        Assert.AreEqual(1, reset);
        Assert.AreEqual(2, reader.Offsets.Count);
        Assert.AreEqual(1, reader.Resets);
    }

    [TestMethod]
    public async Task FindsSourceInLocalRoots()
    {
        var source = Path.Combine(_root, "src", "main.cpp");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllText(source, "int main() {}");
        using var service = CreateService(new FakeReader());
        var symbol = await service.ResolveAsync(CallStackParser.Parse(Message)[0]);

        var result = await service.FindSourceAsync(symbol);

        Assert.AreEqual(source, result.Path);
    }

    [TestMethod]
    public void ShowCallStackPresentsParsedFrames()
    {
        var presenter = new FakePresenter();
        using var service = new CallStackSymbolService(new FakeReader(), presenter, new Desktop(), new ImmediateDispatcher(),
            new NotFoundHandler());

        service.ShowCallStack("no frames");
        service.ShowCallStack(Message);

        Assert.AreEqual(1, presenter.Shown.Count);
        Assert.AreEqual(2, presenter.Shown[0].Frames.Count);
        Assert.IsTrue(presenter.Shown[0].Completion.Wait(TimeSpan.FromSeconds(10)));
        Assert.AreEqual("Run+0x10", presenter.Shown[0].Frames[0].Function);
    }

    [TestMethod]
    public void LinePartShowsResolvedFramesWhenSymbolsAreToggled()
    {
        using var service = CreateService(new FakeReader());
        using var monitor = new DisplaySettingsMonitor(new ImmediateDispatcher(), service);
        var state = new DecodedPartsState();
        var part = new LinePartViewModel(1, Message, monitor, decodedState: state.For(1, 0));
        var changed = new ManualResetEventSlim();
        part.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LinePartViewModel.TextModel))
                changed.Set();
        };

        Assert.IsTrue(part.IsCallStack);
        part.IsSymbolsResolved = true;
        changed.Reset();
        _ = part.TextModel;

        Assert.IsTrue(changed.Wait(TimeSpan.FromSeconds(10)));
        var text = part.TextModel.GetDisplayedText(null);
        StringAssert.Contains(text, @"0# test_.dll!Run+0x10 [C:\src\main.cpp @ 42]");
        StringAssert.Contains(text, "1# other.dll!10");
        Assert.IsTrue(state.Get(null, 1, 0).Symbols);

        var recreated = new LinePartViewModel(2, Message, monitor, decodedState: state.For(1, 0));
        Assert.IsTrue(recreated.IsSymbolsResolved);

        part.IsSymbolsResolved = false;
        text = part.TextModel.GetDisplayedText(null);
        StringAssert.Contains(text, "0# test_.dll!86da0");
        Assert.IsFalse(text.Contains("Run", StringComparison.Ordinal), text);
    }

    [TestMethod]
    public void DisabledSymbolsHideCallStackToggle()
    {
        SymbolOptions.Current = SymbolOptions.Current with { Enabled = false };
        using var service = CreateService(new FakeReader());
        using var monitor = new DisplaySettingsMonitor(new ImmediateDispatcher(), service);

        var part = new LinePartViewModel(1, Message, monitor);
        part.IsSymbolsResolved = true;

        Assert.IsFalse(part.IsCallStack);
        Assert.IsFalse(part.IsSymbolsResolved);
    }

    [TestMethod]
    public void TextWithoutFramesIsNotCallStack()
    {
        using var service = CreateService(new FakeReader());
        using var monitor = new DisplaySettingsMonitor(new ImmediateDispatcher(), service);

        Assert.IsFalse(new LinePartViewModel(1, "plain message", monitor).IsCallStack);
        Assert.IsFalse(new LinePartViewModel(2, Message, monitor, detectBase64: false).IsCallStack);
    }

    private static CallStackSymbolService CreateService(ISymbolReader reader) =>
        new(reader, new FakePresenter(), new Desktop(), new ImmediateDispatcher(), new NotFoundHandler());

    private sealed class FakeReader : ISymbolReader
    {
        public List<ulong> Offsets { get; } = new();

        public int Resets { get; private set; }

        public SymbolInfo? Resolve(string pdbPath, ulong offset)
        {
            lock (Offsets)
                Offsets.Add(offset);
            return new SymbolInfo("Run", 0x10, @"C:\src\main.cpp", 42);
        }

        public void Reset() => Resets++;
    }

    private sealed class FakePresenter : ICallStackPresenter
    {
        public List<CallStackViewModel> Shown { get; } = new();

        public void Show(CallStackViewModel viewModel) => Shown.Add(viewModel);
    }

    private sealed class Desktop : IDesktopService
    {
        public void OpenUrl(string url) { }
        public void OpenPath(string path) { }
        public void RevealFile(string path) { }
        public void SetClipboardText(string text) { }
    }

    private sealed class ImmediateDispatcher : IUiDispatcher
    {
        public bool CheckAccess() => true;

        public IDisposable Post(Action action)
        {
            action();
            return new Done();
        }

        public IUiTimer CreateTimer(TimeSpan interval, Action tick, UiTimerPriority priority = UiTimerPriority.Normal) =>
            throw new InvalidOperationException("Unexpected timer.");

        private sealed class Done : IDisposable
        {
            public void Dispose() { }
        }
    }

    private sealed class NotFoundHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
