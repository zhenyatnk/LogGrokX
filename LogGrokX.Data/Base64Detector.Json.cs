using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace LogGrokX.Data;

public static partial class Base64Detector
{
    private const string TypeField = "Type";
    private const string ContentField = "Content";
    private const string SubjectAlternativeNamesField = "Subject alternative names";
    private const string SubjectPublicKeyInfoTitle = "Public key (SubjectPublicKeyInfo)";

    private static readonly string[] KeyDescriptionTitles =
    {
        "X.509 certificate",
        "RSA public key (CryptoAPI PUBLICKEYBLOB)",
        SubjectPublicKeyInfoTitle
    };

    [GeneratedRegex(@"\G\s*-----BEGIN (?<label>[A-Z0-9][A-Z0-9 ]*)-----\n(?<content>.*?)\n-----END \k<label>-----",
        RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex DecodedPemRegex();

    [GeneratedRegex(@"^(?<name>[A-Za-z][A-Za-z0-9 .()\-]*):(?: (?<value>.*))?$", RegexOptions.CultureInvariant)]
    private static partial Regex DescriptionFieldRegex();

    public static bool TryFormatKeyDescriptionAsJson(string text, out string json)
    {
        json = string.Empty;
        if (string.IsNullOrEmpty(text))
            return false;

        var normalized = text.Replace("\r\n", "\n").Trim();
        var node = normalized.StartsWith(PemBeginMarker, StringComparison.Ordinal)
            ? ParseDecodedPemBlocks(normalized)
            : ParseTitledKeyDescription(normalized);
        if (node == null)
            return false;

        json = node.ToJsonString(RelaxedJson);
        return true;
    }

    private static JsonNode? ParseDecodedPemBlocks(string text)
    {
        var blocks = new JsonArray();
        var position = 0;
        var match = DecodedPemRegex().Match(text);
        while (match.Success)
        {
            var label = match.Groups["label"].Value;
            var content = match.Groups["content"].Value;
            blocks.Add(ParseDescriptionFields(content, label) ?? new JsonObject
            {
                [TypeField] = label,
                [ContentField] = ToJsonContent(content)
            });
            position = match.Index + match.Length;
            match = match.NextMatch();
        }

        if (blocks.Count == 0 || position != text.Length)
            return null;

        if (blocks.Count > 1)
            return blocks;

        var single = blocks[0];
        blocks.RemoveAt(0);
        return single;
    }

    private static JsonObject? ParseTitledKeyDescription(string text)
    {
        foreach (var title in KeyDescriptionTitles)
        {
            if (text.Length <= title.Length + 1 || !text.StartsWith(title, StringComparison.Ordinal))
                continue;

            var separator = text[title.Length];
            if (separator == '\n')
                return ParseDescriptionFields(text[(title.Length + 1)..], title);

            if (separator == ':' && title == SubjectPublicKeyInfoTitle && !text.Contains('\n'))
            {
                return new JsonObject
                {
                    [TypeField] = title,
                    ["Public key"] = text[(title.Length + 1)..].Trim()
                };
            }
        }

        return null;
    }

    private static JsonObject? ParseDescriptionFields(string content, string type)
    {
        if (content.Length == 0)
            return null;

        var result = new JsonObject { [TypeField] = type };
        JsonArray? continuation = null;
        foreach (var line in content.Split('\n'))
        {
            var match = DescriptionFieldRegex().Match(line);
            if (match.Success)
            {
                var name = match.Groups["name"].Value.TrimEnd();
                if (result.ContainsKey(name))
                    return null;

                if (match.Groups["value"].Success)
                {
                    var value = match.Groups["value"].Value;
                    result[name] = name == SubjectAlternativeNamesField
                        ? new JsonArray(value.Split(", ").Select(v => (JsonNode?)JsonValue.Create(v)).ToArray())
                        : JsonValue.Create(value);
                    continuation = null;
                }
                else
                {
                    continuation = new JsonArray();
                    result[name] = continuation;
                }

                continue;
            }

            if (continuation == null || line.Length == 0)
                return null;

            continuation.Add(JsonValue.Create(line));
        }

        return result.Count > 1 ? result : null;
    }

    private static JsonNode? ToJsonContent(string content)
    {
        var trimmed = content.Trim();
        if (trimmed.Length > 1 && trimmed[0] is '{' or '[')
        {
            try
            {
                return JsonNode.Parse(trimmed);
            }
            catch (JsonException)
            {
            }
        }

        if (!content.Contains('\n'))
            return JsonValue.Create(content);

        return new JsonArray(content.TrimEnd('\n').Split('\n').Select(l => (JsonNode?)JsonValue.Create(l)).ToArray());
    }
}
