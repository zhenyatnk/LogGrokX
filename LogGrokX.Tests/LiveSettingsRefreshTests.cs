using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using LogGrokX.Controls;
using LogGrokX.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Tests;

[TestClass]
[DoNotParallelize]
public class LiveSettingsRefreshTests
{
    private static string Encode(string text) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(text));

    private static FieldValueMapper Mapper(params (string value, string mapping)[] items) =>
        new ValueMapper(new[]
        {
            new ValueMappingList
            {
                Name = "test",
                Items = items.Select(i => new ValueMappingItem { Value = i.value, Mapping = i.mapping }).ToArray()
            }
        }).ForField(null);

    [TestMethod]
    public void PartFollowsDetectionSettingChanges()
    {
        var previous = BinaryDetectionOptions.Current;
        try
        {
            BinaryDetectionOptions.Current = BinaryDetectionOptions.All;
            var part = new LinePartViewModel(1, Encode("Hello, World!"));
            var changed = new List<string>();
            ((INotifyPropertyChanged)part).PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? string.Empty);

            BinaryDetectionOptions.Current = BinaryDetectionOptions.None;

            Assert.IsFalse(part.IsBase64);
            Assert.IsFalse(part.IsPem);
            Assert.IsFalse(part.IsHex);
            CollectionAssert.Contains(changed, nameof(LinePartViewModel.IsBase64));
            CollectionAssert.Contains(changed, nameof(LinePartViewModel.TextModel));
        }
        finally
        {
            BinaryDetectionOptions.Current = previous;
        }
    }

    [TestMethod]
    public void ExplicitDetectionSnapshotIsNotAffectedBySettingChanges()
    {
        var previous = BinaryDetectionOptions.Current;
        try
        {
            BinaryDetectionOptions.Current = BinaryDetectionOptions.None;
            var part = new LinePartViewModel(1, Encode("Hello, World!"), detection: BinaryDetectionOptions.All);

            BinaryDetectionOptions.Current = BinaryDetectionOptions.All;

            Assert.IsTrue(part.IsBase64);
        }
        finally
        {
            BinaryDetectionOptions.Current = previous;
        }
    }

    [TestMethod]
    public void DisabledDetectionIsNotAffectedBySettingChanges()
    {
        var previous = BinaryDetectionOptions.Current;
        try
        {
            BinaryDetectionOptions.Current = BinaryDetectionOptions.All;
            var part = new LinePartViewModel(1, Encode("Hello, World!"), detectBase64: false);

            BinaryDetectionOptions.Current = BinaryDetectionOptions.None;

            Assert.IsFalse(part.IsBase64);
        }
        finally
        {
            BinaryDetectionOptions.Current = previous;
        }
    }

    [TestMethod]
    public void DecodedStateIsClearedWhenDetectionDisablesKinds()
    {
        var previous = BinaryDetectionOptions.Current;
        try
        {
            BinaryDetectionOptions.Current = BinaryDetectionOptions.All;
            var part = new LinePartViewModel(1, $"hex 48656C6C6F20776F726C64 b64 {Encode("Hello, World!")}");
            part.IsDecoded = true;
            Assert.IsTrue(part.IsDecoded);

            BinaryDetectionOptions.Current = BinaryDetectionOptions.None;

            Assert.IsFalse(part.IsDecoded);
            Assert.IsFalse(part.IsHexDecoded);
            Assert.IsFalse(part.IsBase64Decoded);
            Assert.IsFalse(part.IsPemDecoded);
            var displayed = part.TextModel.GetDisplayedText(null);
            StringAssert.Contains(displayed, Encode("Hello, World!"));
        }
        finally
        {
            BinaryDetectionOptions.Current = previous;
        }
    }

    [TestMethod]
    public void HexKindCanBeDisabledIndependently()
    {
        var previous = BinaryDetectionOptions.Current;
        try
        {
            BinaryDetectionOptions.Current = BinaryDetectionOptions.All;
            var part = new LinePartViewModel(1, "48656C6C6F20776F726C64");
            Assert.IsTrue(part.IsHex);

            BinaryDetectionOptions.Current = new BinaryDetectionOptions(true, true, false);

            Assert.IsFalse(part.IsHex);
            Assert.IsFalse(part.IsBase64);
        }
        finally
        {
            BinaryDetectionOptions.Current = previous;
        }
    }

    [TestMethod]
    public void PartFollowsValueMappingChanges()
    {
        var previous = ValueMapper.Current;
        try
        {
            ValueMapper.Current = new ValueMapper(new[]
            {
                new ValueMappingList
                {
                    Name = "codes",
                    Items = new[] { new ValueMappingItem { Value = "0x00001", Mapping = "type 1" } }
                }
            });
            var part = new LinePartViewModel(1, "status 0x00001",
                valueMapperResolver: () => ValueMapper.Current.ForField(null));

            Assert.AreEqual("status 0x00001(type 1)", part.TextModel.GetDisplayedText(null));

            ValueMapper.Current = ValueMapper.Empty;

            Assert.AreEqual("status 0x00001", part.TextModel.GetDisplayedText(null));
            Assert.AreEqual("status 0x00001", part.OriginalText);
        }
        finally
        {
            ValueMapper.Current = previous;
        }
    }

    [TestMethod]
    public void ExplicitValueMapperIsNotAffectedByMappingChanges()
    {
        var previous = ValueMapper.Current;
        try
        {
            ValueMapper.Current = ValueMapper.Empty;
            var part = new LinePartViewModel(1, "status 0x00001", valueMapper: Mapper(("0x00001", "type 1")));

            ValueMapper.Current = new ValueMapper(new[]
            {
                new ValueMappingList
                {
                    Name = "other",
                    Items = new[] { new ValueMappingItem { Value = "0x00001", Mapping = "other" } }
                }
            });

            Assert.AreEqual("status 0x00001(type 1)", part.TextModel.GetDisplayedText(null));
        }
        finally
        {
            ValueMapper.Current = previous;
        }
    }

    [TestMethod]
    public void LineDisplayTextFollowsValueMappingChanges()
    {
        var previous = ValueMapper.Current;
        try
        {
            var meta = new LogMetaInformation(new LogFormat
            {
                Regex = @"^(?<Level>[^\s]+)\s+(?<Message>.*)$"
            });
            var line = new LineViewModel(0, "1 code 0x00001", new RegexBasedLineParser(meta), new Selection(),
                new TransformationPerformer(Array.Empty<string>()),
                valueMappersResolver: () => ValueMapper.Current.ForFields(meta.FieldNames));

            Assert.AreEqual("1 code 0x00001", line.GetDisplayText(null));

            ValueMapper.Current = new ValueMapper(new[]
            {
                new ValueMappingList
                {
                    Name = "codes",
                    Fields = new[] { "Message" },
                    Items = new[] { new ValueMappingItem { Value = "0x00001", Mapping = "type 1" } }
                }
            });

            Assert.AreEqual("1 code 0x00001(type 1)", line.GetDisplayText(null));
            Assert.AreEqual("1", line.GetFieldText(0));
            Assert.AreEqual("code 0x00001", line.GetFieldText(1));
        }
        finally
        {
            ValueMapper.Current = previous;
        }
    }

    [TestMethod]
    public void RowLevelDecodableFollowsDetectionSettingChanges()
    {
        var previous = BinaryDetectionOptions.Current;
        try
        {
            BinaryDetectionOptions.Current = BinaryDetectionOptions.All;
            var meta = new LogMetaInformation(new LogFormat
            {
                Regex = @"^(?<Message>.*)$"
            });
            var line = new LineViewModel(0, Encode("Hello, World!"), new RegexBasedLineParser(meta),
                new Selection(), new TransformationPerformer(Array.Empty<string>()));
            var changed = new List<string>();
            ((INotifyPropertyChanged)line).PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? string.Empty);

            Assert.IsTrue(line.IsDecodable);
            Assert.IsTrue(line.IsBase64);

            BinaryDetectionOptions.Current = BinaryDetectionOptions.None;

            Assert.IsFalse(line.IsDecodable);
            Assert.IsFalse(line.IsBase64);
            CollectionAssert.Contains(changed, nameof(BaseLogLineViewModel.IsDecodable));
            CollectionAssert.Contains(changed, nameof(BaseLogLineViewModel.IsBase64));
        }
        finally
        {
            BinaryDetectionOptions.Current = previous;
        }
    }

    [TestMethod]
    public void PartStillNotifiesDetectionChangesAfterGarbageCollection()
    {
        var previous = BinaryDetectionOptions.Current;
        try
        {
            BinaryDetectionOptions.Current = BinaryDetectionOptions.All;
            var part = new LinePartViewModel(1, Encode("Hello, World!"));
            var changed = new List<string>();
            ((INotifyPropertyChanged)part).PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? string.Empty);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            BinaryDetectionOptions.Current = BinaryDetectionOptions.None;

            CollectionAssert.Contains(changed, nameof(LinePartViewModel.IsBase64));
            Assert.IsFalse(part.IsBase64);
            GC.KeepAlive(part);
        }
        finally
        {
            BinaryDetectionOptions.Current = previous;
        }
    }

    [TestMethod]
    public void PartStillNotifiesMappingChangesAfterGarbageCollection()
    {
        var previous = ValueMapper.Current;
        try
        {
            ValueMapper.Current = ValueMapper.Empty;
            var part = new LinePartViewModel(1, "status 0x00001",
                valueMapperResolver: () => ValueMapper.Current.ForField(null));
            var changed = new List<string>();
            ((INotifyPropertyChanged)part).PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? string.Empty);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            ValueMapper.Current = new ValueMapper(new[]
            {
                new ValueMappingList
                {
                    Name = "codes",
                    Items = new[] { new ValueMappingItem { Value = "0x00001", Mapping = "type 1" } }
                }
            });

            CollectionAssert.Contains(changed, nameof(LinePartViewModel.TextModel));
            Assert.AreEqual("status 0x00001(type 1)", part.TextModel.GetDisplayedText(null));
            GC.KeepAlive(part);
        }
        finally
        {
            ValueMapper.Current = previous;
        }
    }
}
