using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using LogGrokX.Controls;
using LogGrokX.Data;
using LogGrokX.Data.Virtualization;
using LogGrokX.MergedView;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Tests;

[TestClass]
[DoNotParallelize]
public class DecodedPartsStateTests
{
    private static readonly string EncodedLine = "INFO " + Encode("Hello, decoded World!");

    private BinaryDetectionOptions _previousDetection = BinaryDetectionOptions.All;

    [TestInitialize]
    public void Initialize()
    {
        _previousDetection = BinaryDetectionOptions.Current;
        BinaryDetectionOptions.Current = BinaryDetectionOptions.All;
    }

    [TestCleanup]
    public void Cleanup() => BinaryDetectionOptions.Current = _previousDetection;

    private static string Encode(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

    private static readonly RegexBasedLineParser Parser = new(new LogMetaInformation(new LogFormat
    {
        Regex = @"^(?<Level>[^\s]+)\s+(?<Message>.*)$"
    }));

    private static readonly TransformationPerformer NoTransformations = new(Array.Empty<string>());

    private static LineViewModel CreateLine(int index, string source, DecodedPartsState state) =>
        new(index, source, Parser, new Selection(), NoTransformations, decodedParts: state);

    [TestMethod]
    public void RecreatedLineKeepsBinToggle()
    {
        var state = new DecodedPartsState();
        var first = CreateLine(3, EncodedLine, state);
        Assert.IsTrue(first.IsDecodable);
        first.IsDecoded = true;

        var recreated = CreateLine(3, EncodedLine, state);

        Assert.IsTrue(recreated.IsDecoded);
        Assert.IsTrue(recreated.IsBase64Decoded);
        StringAssert.Contains(recreated[1].TextModel.GetDisplayedText(null), "Hello, decoded World!");
    }

    [TestMethod]
    public void BinToggleIsNotSharedWithoutState()
    {
        var first = CreateLine(3, EncodedLine, null);
        first.IsDecoded = true;

        Assert.IsFalse(CreateLine(3, EncodedLine, null).IsDecoded);
    }

    [TestMethod]
    public void OtherLinesAreNotDecoded()
    {
        var state = new DecodedPartsState();
        CreateLine(3, EncodedLine, state).IsDecoded = true;

        Assert.IsFalse(CreateLine(4, EncodedLine, state).IsDecoded);
    }

    [TestMethod]
    public void LineCreatedBeforeToggleFollowsSharedState()
    {
        var state = new DecodedPartsState();
        var shown = CreateLine(3, EncodedLine, state);
        var replacement = CreateLine(3, EncodedLine, state);
        Assert.IsFalse(replacement.IsDecoded);

        shown.IsDecoded = true;

        Assert.IsTrue(replacement.IsDecoded);
        StringAssert.Contains(replacement[1].TextModel.GetDisplayedText(null), "Hello, decoded World!");
    }

    [TestMethod]
    public void UntoggleOnRecreatedLineClearsState()
    {
        var state = new DecodedPartsState();
        var first = CreateLine(3, EncodedLine, state);
        first.IsDecoded = true;
        Assert.AreEqual(1, state.Count);

        var recreated = CreateLine(3, EncodedLine, state);
        recreated.IsDecoded = false;

        Assert.AreEqual(0, state.Count);
        Assert.IsFalse(first.IsDecoded);
        StringAssert.Contains(first[1].TextModel.GetDisplayedText(null), EncodedLine["INFO ".Length..]);
    }

    [TestMethod]
    public void SharedStateChangeRaisesRowNotifications()
    {
        var state = new DecodedPartsState();
        var shown = CreateLine(3, EncodedLine, state);
        var other = CreateLine(3, EncodedLine, state);
        Assert.IsFalse(shown.IsDecoded);
        var changed = new List<string>();
        ((INotifyPropertyChanged)shown).PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? string.Empty);

        other.IsDecoded = true;

        Assert.IsTrue(shown.IsDecoded);
        CollectionAssert.Contains(changed, nameof(BaseLogLineViewModel.IsDecoded));
    }

    [TestMethod]
    public void PerCellDecodeKindIsKept()
    {
        var hex = Convert.ToHexString(Encoding.UTF8.GetBytes("Hello, hex World!"));
        var source = $"INFO {hex} {Encode("Hello, base64 World!")}";
        var state = new DecodedPartsState();
        var first = CreateLine(5, source, state);
        Assert.IsTrue(first[1].IsHex);
        Assert.IsTrue(first[1].IsBase64);
        first[1].IsHexDecoded = true;

        var recreated = CreateLine(5, source, state);

        Assert.IsTrue(recreated[1].IsHexDecoded);
        Assert.IsFalse(recreated[1].IsBase64Decoded);
        StringAssert.StartsWith(recreated[1].TextModel.GetDisplayedText(null), "Hello, hex World!");
    }

    [TestMethod]
    public void DisabledDetectionMasksRestoredState()
    {
        var state = new DecodedPartsState();
        CreateLine(3, EncodedLine, state).IsDecoded = true;

        BinaryDetectionOptions.Current = BinaryDetectionOptions.None;
        var recreated = CreateLine(3, EncodedLine, state);

        Assert.IsFalse(recreated.IsDecodable);
        Assert.IsFalse(recreated.IsDecoded);
        StringAssert.Contains(recreated[1].TextModel.GetDisplayedText(null), EncodedLine["INFO ".Length..]);
    }

    [TestMethod]
    public void StateIsKeyedBySource()
    {
        var state = new DecodedPartsState();
        var first = new object();
        var second = new object();
        state.For(first, 1, 2).Set(new DecodedPartState(Base64Content.Pem, false));

        Assert.AreEqual(Base64Content.Pem, state.For(first, 1, 2).Get().Base64);
        Assert.IsTrue(state.For(second, 1, 2).Get().IsEmpty);
        Assert.IsTrue(state.For(1, 2).Get().IsEmpty);
    }

    [TestMethod]
    public void SettingSameStateDoesNotChangeVersion()
    {
        var state = new DecodedPartsState();
        var key = state.For(1, 1);
        key.Set(new DecodedPartState(Base64Content.Base64, true));
        var version = state.Version;

        key.Set(new DecodedPartState(Base64Content.Base64, true));
        state.For(2, 1).Set(default);

        Assert.AreEqual(version, state.Version);
    }

    [TestMethod]
    public void BinToggleSurvivesVirtualListPageEviction()
    {
        var state = new DecodedPartsState();
        var lines = Enumerable.Range(0, 128 * 30).Select(i => (i, EncodedLine)).ToList();
        var list = new VirtualList<(int index, string str), ItemViewModel>(
            new ListItemProvider<(int index, string str)>(lines),
            line => CreateLine(line.index, line.str, state));

        var first = (LineViewModel)list[0];
        first.IsDecoded = true;
        for (var i = 0; i < list.Count; i += 128)
            _ = list[i];

        var again = (LineViewModel)list[0];
        Assert.AreNotSame(first, again);
        Assert.IsTrue(again.IsDecoded);
        Assert.IsFalse(((LineViewModel)list[1]).IsDecoded);
    }
}
