using System;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Data.Tests;

[TestClass]
public class ValueMappingJsonTests
{
    [TestMethod]
    public void SerializeAndDeserializeRoundTrip()
    {
        var lists = new[]
        {
            new ValueMappingList
            {
                Name = "Types",
                Enabled = false,
                IgnoreCase = true,
                Fields = new[] { "Message", "Type" },
                Items = new[]
                {
                    new ValueMappingItem { Value = "0x00001", Mapping = "type 1" },
                    new ValueMappingItem { Value = "0x00002", Mapping = "тип \"2\"" }
                }
            },
            new ValueMappingList { Name = "Empty" }
        };

        var json = ValueMappingJson.Serialize(lists);
        var restored = ValueMappingJson.Deserialize(json);

        Assert.AreEqual(2, restored.Count);
        Assert.AreEqual("Types", restored[0].Name);
        Assert.IsFalse(restored[0].Enabled);
        Assert.IsTrue(restored[0].IgnoreCase);
        CollectionAssert.AreEqual(new[] { "Message", "Type" }, restored[0].Fields);
        Assert.AreEqual(2, restored[0].Items.Length);
        Assert.AreEqual("0x00001", restored[0].Items[0].Value);
        Assert.AreEqual("type 1", restored[0].Items[0].Mapping);
        Assert.AreEqual("тип \"2\"", restored[0].Items[1].Mapping);
        Assert.AreEqual("Empty", restored[1].Name);
        Assert.IsTrue(restored[1].Enabled);
        Assert.AreEqual(0, restored[1].Items.Length);
        StringAssert.Contains(json, "тип");
    }

    [TestMethod]
    public void SerializeSingleListWritesObject()
    {
        var json = ValueMappingJson.Serialize(new ValueMappingList
        {
            Name = "Types",
            Items = new[] { new ValueMappingItem { Value = "0x00001", Mapping = "type 1" } }
        });

        using var document = JsonDocument.Parse(json);
        Assert.AreEqual(JsonValueKind.Object, document.RootElement.ValueKind);
        Assert.AreEqual("Types", document.RootElement.GetProperty("Name").GetString());
        Assert.AreEqual("0x00001", document.RootElement.GetProperty("Items")[0].GetProperty("Value").GetString());
    }

    [TestMethod]
    public void DeserializeSingleObject()
    {
        var lists = ValueMappingJson.Deserialize(
            "{ \"name\": \"Types\", \"fields\": [\"Type\"], \"items\": [ { \"value\": \"0x00001\", \"mapping\": \"type 1\" } ] }");

        Assert.AreEqual(1, lists.Count);
        Assert.AreEqual("Types", lists[0].Name);
        Assert.IsTrue(lists[0].Enabled);
        Assert.IsFalse(lists[0].IgnoreCase);
        CollectionAssert.AreEqual(new[] { "Type" }, lists[0].Fields);
        Assert.AreEqual("type 1", lists[0].Items[0].Mapping);
    }

    [TestMethod]
    public void DeserializePlainDictionaryUsesDefaultName()
    {
        var lists = ValueMappingJson.Deserialize("{ \"0x00001\": \"type 1\", \"0x00002\": \"type 2\" }", "types");

        Assert.AreEqual(1, lists.Count);
        Assert.AreEqual("types", lists[0].Name);
        Assert.AreEqual(2, lists[0].Items.Length);
        Assert.AreEqual("0x00002", lists[0].Items[1].Value);
        Assert.AreEqual("type 2", lists[0].Items[1].Mapping);
    }

    [TestMethod]
    public void DeserializeItemsAsObjectAndNumbers()
    {
        var lists = ValueMappingJson.Deserialize(
            "{ \"Name\": \"Levels\", \"Fields\": \"Level, Severity\", \"Items\": { \"1\": \"info\", \"2\": 2 } }");

        CollectionAssert.AreEqual(new[] { "Level", "Severity" }, lists[0].Fields);
        Assert.AreEqual("1", lists[0].Items[0].Value);
        Assert.AreEqual("info", lists[0].Items[0].Mapping);
        Assert.AreEqual("2", lists[0].Items[1].Mapping);
    }

    [TestMethod]
    public void DeserializeAllowsCommentsAndTrailingCommas()
    {
        var lists = ValueMappingJson.Deserialize(
            "[ // codes\n { \"Name\": \"A\", \"Items\": [ { \"Value\": \"1\", \"Mapping\": \"one\" }, ], }, ]");

        Assert.AreEqual("one", lists[0].Items[0].Mapping);
    }

    [TestMethod]
    public void DeserializedListIsUsableByMapper()
    {
        var lists = ValueMappingJson.Deserialize("{ \"0x00001\": \"type 1\" }");

        Assert.AreEqual("0x00001(type 1)", new ValueMapper(lists).ForField("Message").Map("0x00001"));
    }

    [TestMethod]
    [DataRow("42")]
    [DataRow("[1, 2]")]
    [DataRow("{ \"Items\": 5 }")]
    [DataRow("{ \"Items\": [ \"x\" ] }")]
    public void DeserializeRejectsUnexpectedShapes(string json)
    {
        Assert.ThrowsExactly<FormatException>(() => ValueMappingJson.Deserialize(json));
    }

    [TestMethod]
    public void DeserializeRejectsInvalidJson()
    {
        Assert.Throws<JsonException>(() => ValueMappingJson.Deserialize("{ not json"));
    }
}
