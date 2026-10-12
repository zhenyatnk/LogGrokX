using System;
using System.IO;
using System.Linq;
using LogGrokX.Controls;
using LogGrokX.Data;
using LogGrokX.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Tests
{
    [TestClass]
    public class ValueMappingTests
    {
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
        public void PlainTextIsMapped()
        {
            Assert.AreEqual("type 0x00001(type 1) received",
                ValueMappingText.Apply("type 0x00001 received", Mapper(("0x00001", "type 1"))));
        }

        [TestMethod]
        public void NullOrEmptyMapperLeavesTextUnchanged()
        {
            Assert.AreEqual("0x00001", ValueMappingText.Apply("0x00001", null));
            Assert.AreEqual("0x00001", ValueMappingText.Apply("0x00001", FieldValueMapper.None));
        }

        [TestMethod]
        public void JsonStringValueIsMappedAndStaysJson()
        {
            const string source = "result {\"type\": \"0x00001\", \"0x00001\": 1} done 0x00001";

            var mapped = ValueMappingText.Apply(source, Mapper(("0x00001", "type \"1\"")));

            Assert.AreEqual("result {\"type\": \"0x00001(type \\\"1\\\")\", \"0x00001\": 1} done 0x00001(type \"1\")", mapped);
            var ranges = TextOperations.GetStructuredRanges(mapped);
            Assert.AreEqual(1, ranges.Count);
            Assert.AreEqual(StructuredTextKind.Json, ranges[0].kind);
        }

        [TestMethod]
        public void JsonNumberIsMappedAsComment()
        {
            const string source = "{\"code\": 5, \"name\": \"x\"}";

            var mapped = ValueMappingText.Apply(source, Mapper(("5", "denied")));

            Assert.AreEqual("{\"code\": 5 /*denied*/, \"name\": \"x\"}", mapped);
            var ranges = TextOperations.GetStructuredRanges(mapped);
            Assert.AreEqual(1, ranges.Count);
            Assert.AreEqual(StructuredTextKind.Json, ranges[0].kind);
        }

        [TestMethod]
        public void JsonNumberMappingKeepsJsonFormatted()
        {
            const string source = "{\"a\":{\"b\":5}}";

            var model = new TextModel(1, ValueMappingText.Apply(source, Mapper(("5", "denied"))));

            Assert.IsNotNull(model.CollapsibleRanges);
            var displayed = model.GetDisplayedText(new System.Collections.Generic.HashSet<int>());
            StringAssert.Contains(displayed, "\"b\": 5 /*denied*/");
        }

        [TestMethod]
        public void JsonNumberCommentOmitsCommentTerminator()
        {
            const string source = "{\"code\": 5}";

            var mapped = ValueMappingText.Apply(source, Mapper(("5", "a*/b")));

            StringAssert.Contains(mapped, "5 /*a* /b*/");
            Assert.AreEqual(1, TextOperations.GetStructuredRanges(mapped).Count);
        }

        [TestMethod]
        public void XmlTextAndAttributesAreMappedButNotTagNames()
        {
            const string source = "<root type=\"1\"><item>1</item><type>2</type></root>";

            var mapped = ValueMappingText.Apply(source, Mapper(("1", "a<b"), ("type", "t"), ("item", "i")));

            Assert.AreEqual("<root type=\"1(a&lt;b)\"><item>1(a&lt;b)</item><type>2</type></root>", mapped);
            var ranges = TextOperations.GetStructuredRanges(mapped);
            Assert.AreEqual(1, ranges.Count);
            Assert.AreEqual(StructuredTextKind.Xml, ranges[0].kind);
        }

        [TestMethod]
        public void LinePartShowsMappedTextAndKeepsOriginal()
        {
            var part = new LinePartViewModel(1, "status 0x00001", valueMapper: Mapper(("0x00001", "type 1")));

            Assert.AreEqual("status 0x00001", part.OriginalText);
            Assert.AreEqual("status 0x00001(type 1)", part.TextModel.GetDisplayedText(null));
        }

        [TestMethod]
        public void LinePartWithoutMapperIsUnchanged()
        {
            var part = new LinePartViewModel(1, "status 0x00001");

            Assert.AreEqual("status 0x00001", part.TextModel.GetDisplayedText(null));
        }

        [TestMethod]
        public void LineMapsOnlyConfiguredFieldsAndCopiesMappedText()
        {
            var meta = new LogMetaInformation(new LogFormat
            {
                Regex = @"^(?<Level>[^\s]+)\s+(?<Message>.*)$"
            });
            var mapper = new ValueMapper(new[]
            {
                new ValueMappingList
                {
                    Name = "levels",
                    Fields = new[] { "Level" },
                    Items = new[] { new ValueMappingItem { Value = "1", Mapping = "info" } }
                }
            });
            var line = new LineViewModel(0, "1 code 1", new RegexBasedLineParser(meta), new Selection(),
                new TransformationPerformer(Array.Empty<string>()), mapper.ForFields(meta.FieldNames));

            Assert.AreEqual("1(info)", line[0].TextModel.GetDisplayedText(null));
            Assert.AreEqual("code 1", line[1].TextModel.GetDisplayedText(null));
            Assert.AreEqual("1", line.GetFieldText(0));
            Assert.AreEqual("1(info) code 1", line.GetDisplayText(null));
        }

        [TestMethod]
        public void ListViewModelConvertsToDataSkippingEmptyRows()
        {
            var list = new ValueMappingListViewModel
            {
                Name = " Types ",
                IgnoreCase = true,
                Fields = "Message, Type;; message"
            };
            list.Items.Add(new ValueMappingItemViewModel { Value = " 0x00001 ", Mapping = "type 1" });
            list.Items.Add(new ValueMappingItemViewModel());

            var data = list.ToData();

            Assert.AreEqual("Types", data.Name);
            Assert.IsTrue(data.IgnoreCase);
            CollectionAssert.AreEqual(new[] { "Message", "Type" }, data.Fields);
            Assert.AreEqual(1, data.Items.Length);
            Assert.AreEqual("0x00001", data.Items[0].Value);
            Assert.AreEqual(0, list.GetValidationErrors().Count());
        }

        [TestMethod]
        public void ListViewModelReportsDuplicatesAndMissingValues()
        {
            var list = new ValueMappingListViewModel { Name = "Types" };
            list.Items.Add(new ValueMappingItemViewModel { Value = "1", Mapping = "a" });
            list.Items.Add(new ValueMappingItemViewModel { Value = "1 ", Mapping = "b" });
            list.Items.Add(new ValueMappingItemViewModel { Mapping = "c" });

            var errors = list.GetValidationErrors().ToList();

            Assert.AreEqual(2, errors.Count);
            StringAssert.Contains(errors[0], "without a value");
            StringAssert.Contains(errors[1], "duplicate values: 1");
        }

        [TestMethod]
        public void ListViewModelPreviewIgnoresFieldRestriction()
        {
            var list = new ValueMappingListViewModel(new ValueMappingList
            {
                Name = "Types",
                Enabled = false,
                Fields = new[] { "Type" },
                Items = new[] { new ValueMappingItem { Value = "0x00001", Mapping = "type 1" } }
            });

            Assert.AreEqual("0x00001", list.SampleText);
            Assert.AreEqual("0x00001(type 1)", list.SamplePreview);
        }

        [TestMethod]
        public void ListViewModelRaisesItemsChangedWhenItemIsEdited()
        {
            var list = new ValueMappingListViewModel();
            var item = new ValueMappingItemViewModel();
            list.Items.Add(item);
            var raised = 0;
            list.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ValueMappingListViewModel.Items))
                    raised++;
            };

            item.Mapping = "x";

            Assert.AreEqual(1, raised);
        }

        [TestMethod]
        public void ValueMappingsAreAddedToFileAndBindBack()
        {
            var fileName = Path.Combine(Path.GetTempPath(), $"LogGrokX.ValueMappings.{Guid.NewGuid():N}.yaml");
            try
            {
                File.WriteAllText(fileName,
                    "Settings:\n" +
                    "  ViewSettings:\n" +
                    "    BigLine: prune\n" +
                    "  LogFormats:\n" +
                    "    - Regex: ^(?<Message>.*)\n" +
                    "      IndexedFields:\n" +
                    "        - Level\n");

                var lists = new[]
                {
                    new ValueMappingList
                    {
                        Name = "Types: main",
                        IgnoreCase = true,
                        Fields = new[] { "Message" },
                        Items = new[]
                        {
                            new ValueMappingItem { Value = "0x00001", Mapping = "type 1" },
                            new ValueMappingItem { Value = "true", Mapping = "# yes" },
                            new ValueMappingItem { Value = "-1", Mapping = "it's: bad" }
                        }
                    }
                };

                var file = new YamlSettingsFile(fileName);
                file.EnsureKey("Settings", "ValueMappings");
                file.ReplaceSequence("Settings", "ValueMappings",
                    indent => SettingsYamlRenderer.RenderValueMappings(lists, indent));
                file.Save();

                var configuration = new ConfigurationBuilder()
                    .AddYamlFile(fileName, optional: false, reloadOnChange: false)
                    .Build();
                var restored = configuration.GetSection("Settings:ValueMappings").Get<ValueMappingList[]>();

                Assert.IsNotNull(restored);
                Assert.AreEqual(1, restored.Length);
                Assert.AreEqual("Types: main", restored[0].Name);
                Assert.IsTrue(restored[0].Enabled);
                Assert.IsTrue(restored[0].IgnoreCase);
                CollectionAssert.AreEqual(new[] { "Message" }, restored[0].Fields);
                Assert.AreEqual(3, restored[0].Items.Length);
                Assert.AreEqual("0x00001", restored[0].Items[0].Value);
                Assert.AreEqual("type 1", restored[0].Items[0].Mapping);
                Assert.AreEqual("true", restored[0].Items[1].Value);
                Assert.AreEqual("# yes", restored[0].Items[1].Mapping);
                Assert.AreEqual("-1", restored[0].Items[2].Value);
                Assert.AreEqual("it's: bad", restored[0].Items[2].Mapping);
                Assert.AreEqual("prune", configuration["Settings:ViewSettings:BigLine"]);
                Assert.AreEqual("Level", configuration["Settings:LogFormats:0:IndexedFields:0"]);

                var second = new YamlSettingsFile(fileName);
                second.EnsureKey("Settings", "ValueMappings");
                second.ReplaceSequence("Settings", "ValueMappings",
                    indent => SettingsYamlRenderer.RenderValueMappings(lists, indent));
                Assert.IsFalse(second.HasChanges);
            }
            finally
            {
                File.Delete(fileName);
            }
        }
    }
}
