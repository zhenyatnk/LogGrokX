using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LogGrokX.Data;

public static class ValueMappingJson
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly JsonDocumentOptions ReadOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    public static string Serialize(IEnumerable<ValueMappingList> lists) =>
        new JsonArray(lists.Select(ToNode).ToArray<JsonNode?>()).ToJsonString(WriteOptions);

    public static string Serialize(ValueMappingList list) => ToNode(list).ToJsonString(WriteOptions);

    public static IReadOnlyList<ValueMappingList> Deserialize(string json, string defaultName = "")
    {
        using var document = JsonDocument.Parse(json, ReadOptions);
        var root = document.RootElement;
        switch (root.ValueKind)
        {
            case JsonValueKind.Array:
                return root.EnumerateArray().Select(element => ReadList(element, defaultName)).ToList();
            case JsonValueKind.Object:
                return new[] { ReadList(root, defaultName) };
            default:
                throw new FormatException("A value mapping file must contain a JSON object or an array of objects.");
        }
    }

    private static JsonObject ToNode(ValueMappingList list) => new()
    {
        ["Name"] = list.Name,
        ["Enabled"] = list.Enabled,
        ["IgnoreCase"] = list.IgnoreCase,
        ["Fields"] = new JsonArray(list.Fields.Select(field => (JsonNode?)JsonValue.Create(field)).ToArray()),
        ["Items"] = new JsonArray(list.Items.Select(item => (JsonNode?)new JsonObject
        {
            ["Value"] = item.Value,
            ["Mapping"] = item.Mapping
        }).ToArray())
    };

    private static ValueMappingList ReadList(JsonElement element, string defaultName)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new FormatException("Each value mapping list must be a JSON object.");

        if (!TryGetProperty(element, "Items", out var items))
        {
            return new ValueMappingList
            {
                Name = defaultName,
                Items = ReadItemMap(element)
            };
        }

        return new ValueMappingList
        {
            Name = TryGetProperty(element, "Name", out var name) ? ReadString(name) : defaultName,
            Enabled = !TryGetProperty(element, "Enabled", out var enabled) || ReadBool(enabled, true),
            IgnoreCase = TryGetProperty(element, "IgnoreCase", out var ignoreCase) && ReadBool(ignoreCase, false),
            Fields = TryGetProperty(element, "Fields", out var fields) ? ReadFields(fields) : Array.Empty<string>(),
            Items = items.ValueKind switch
            {
                JsonValueKind.Array => items.EnumerateArray().Select(ReadItem).ToArray(),
                JsonValueKind.Object => ReadItemMap(items),
                JsonValueKind.Null => Array.Empty<ValueMappingItem>(),
                _ => throw new FormatException("\"Items\" must be an array of {\"Value\", \"Mapping\"} objects or an object.")
            }
        };
    }

    private static ValueMappingItem ReadItem(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new FormatException("Each mapping item must be an object with \"Value\" and \"Mapping\".");

        return new ValueMappingItem
        {
            Value = TryGetProperty(element, "Value", out var value) ? ReadString(value) : string.Empty,
            Mapping = TryGetProperty(element, "Mapping", out var mapping) ? ReadString(mapping) : string.Empty
        };
    }

    private static ValueMappingItem[] ReadItemMap(JsonElement element) =>
        element.EnumerateObject()
            .Select(property => new ValueMappingItem { Value = property.Name, Mapping = ReadString(property.Value) })
            .ToArray();

    private static string[] ReadFields(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Array => element.EnumerateArray().Select(ReadString).Where(f => f.Length > 0).ToArray(),
        JsonValueKind.String => (element.GetString() ?? string.Empty)
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        JsonValueKind.Null => Array.Empty<string>(),
        _ => throw new FormatException("\"Fields\" must be an array of field names.")
    };

    private static string ReadString(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString() ?? string.Empty,
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => element.GetRawText(),
        JsonValueKind.Null => string.Empty,
        _ => throw new FormatException($"Expected a string value, got {element.ValueKind}.")
    };

    private static bool ReadBool(JsonElement element, bool defaultValue) => element.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String when bool.TryParse(element.GetString(), out var value) => value,
        _ => defaultValue
    };

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
