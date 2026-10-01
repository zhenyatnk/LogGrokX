using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using LogGrokX.Data;

namespace LogGrokX;

public static partial class HexText
{
    private const int MinBytes = 9;
    private const int MinSourceLength = MinBytes * 2;
    // Random bytes read as UTF-16 often look like CJK text, so only alphabets below U+0800
    // (Latin, Cyrillic, Greek, ...) are accepted for UTF-16.
    private const char MaxUtf16Char = '\u07FF';

    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);
    private static readonly Encoding StrictUtf16 = new UnicodeEncoding(false, false, true);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    [GeneratedRegex(@"(?<![0-9A-Za-z])(?:0[xX])?[0-9A-Fa-f]{2}(?:(?:[ \-:]|, ?)?(?:0[xX])?[0-9A-Fa-f]{2}){8,}(?![0-9A-Za-z])",
        RegexOptions.CultureInvariant)]
    private static partial Regex HexRunRegex();

    public static bool ContainsDecodableHex(string source) => TryDecode(source, out _);

    public static bool TryDecode(string source, out string decoded)
    {
        decoded = source;
        if (source.Length < MinSourceLength || !HasDecodableRun(source))
            return false;

        var structuredRanges = TextOperations.GetStructuredRanges(source);
        var builder = new StringBuilder(source.Length);
        var found = false;
        var position = 0;
        foreach (var (start, length, kind) in structuredRanges)
        {
            found |= AppendDecodedPlain(builder, source, position, start - position);
            found |= AppendDecodedStructured(builder, source.Substring(start, length), kind);
            position = start + length;
        }

        found |= AppendDecodedPlain(builder, source, position, source.Length - position);
        if (!found)
            return false;

        decoded = builder.ToString();
        return true;
    }

    private static bool HasDecodableRun(string source)
    {
        foreach (Match match in HexRunRegex().Matches(source))
        {
            if (TryDecodeRun(match.ValueSpan, out _))
                return true;
        }

        return false;
    }

    private static bool AppendDecodedPlain(StringBuilder builder, string source, int start, int length)
    {
        if (length <= 0)
            return false;

        var text = source.Substring(start, length);
        var found = false;
        builder.Append(HexRunRegex().Replace(text, match =>
        {
            if (!TryDecodeRun(match.ValueSpan, out var decoded))
                return match.Value;

            found = true;
            return decoded;
        }));
        return found;
    }

    private static bool AppendDecodedStructured(StringBuilder builder, string fragment, StructuredTextKind kind)
    {
        var jsonStrings = kind == StructuredTextKind.Json ? GetJsonStringContents(fragment) : null;
        var found = false;
        var decoded = HexRunRegex().Replace(fragment, match =>
        {
            if (jsonStrings != null && !IsInside(jsonStrings, match.Index, match.Length))
                return match.Value;

            if (!TryDecodeRun(match.ValueSpan, out var text))
                return match.Value;

            found = true;
            return kind == StructuredTextKind.Json ? EscapeJson(text) : SecurityElement.Escape(text);
        });

        if (found && IsSameStructure(decoded, kind))
        {
            builder.Append(decoded);
            return true;
        }

        builder.Append(fragment);
        return false;
    }

    private static bool IsSameStructure(string fragment, StructuredTextKind kind)
    {
        var ranges = TextOperations.GetStructuredRanges(fragment);
        return ranges.Count == 1 && ranges[0] == (0, fragment.Length, kind);
    }

    private static string EscapeJson(string text)
    {
        var encoded = JsonSerializer.Serialize(text, JsonOptions);
        return encoded.Substring(1, encoded.Length - 2);
    }

    private static List<(int start, int end)> GetJsonStringContents(string json)
    {
        var result = new List<(int start, int end)>();
        var start = -1;
        for (var i = 0; i < json.Length; i++)
        {
            var ch = json[i];
            if (start >= 0 && ch == '\\')
            {
                i++;
                continue;
            }

            if (ch != '"')
                continue;

            if (start < 0)
            {
                start = i + 1;
            }
            else
            {
                result.Add((start, i));
                start = -1;
            }
        }

        return result;
    }

    private static bool IsInside(List<(int start, int end)> ranges, int start, int length)
    {
        foreach (var range in ranges)
        {
            if (start >= range.start && start + length <= range.end)
                return true;
        }

        return false;
    }

    private static bool TryDecodeRun(ReadOnlySpan<char> run, out string text)
    {
        text = string.Empty;
        if (IsHexNumber(run))
            return false;

        var bytes = ParseBytes(run);
        if (bytes.Length < MinBytes)
            return false;

        if (TryGetPrintableText(bytes, StrictUtf8, char.MaxValue, out text) ||
            (bytes.Length % 2 == 0 && TryGetPrintableText(bytes, StrictUtf16, MaxUtf16Char, out text)))
            return true;

        return Base64Detector.TryDescribeBinaryKeyFromBytes(bytes, out text);
    }

    // A contiguous token of digits 0-9 only (optionally 0x-prefixed) looks like a number, not like encoded text.
    private static bool IsHexNumber(ReadOnlySpan<char> run)
    {
        var digits = run.Length > 2 && run[0] == '0' && run[1] is 'x' or 'X' ? run[2..] : run;
        foreach (var ch in digits)
        {
            if (!char.IsAsciiDigit(ch))
                return false;
        }

        return true;
    }

    private static byte[] ParseBytes(ReadOnlySpan<char> run)
    {
        var bytes = new byte[run.Length / 2];
        var count = 0;
        var i = 0;
        while (i + 1 < run.Length)
        {
            if (run[i] == '0' && run[i + 1] is 'x' or 'X')
            {
                i += 2;
                continue;
            }

            if (Uri.IsHexDigit(run[i]) && Uri.IsHexDigit(run[i + 1]))
            {
                bytes[count++] = (byte)((HexValue(run[i]) << 4) | HexValue(run[i + 1]));
                i += 2;
            }
            else
            {
                i++;
            }
        }

        return bytes.AsSpan(0, count).ToArray();
    }

    private static int HexValue(char ch) => ch switch
    {
        >= '0' and <= '9' => ch - '0',
        >= 'a' and <= 'f' => ch - 'a' + 10,
        _ => ch - 'A' + 10
    };

    private static bool TryGetPrintableText(byte[] bytes, Encoding encoding, char maxChar, out string text)
    {
        text = string.Empty;
        string decoded;
        try
        {
            decoded = encoding.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        decoded = decoded.TrimEnd('\0');
        if (decoded.Length == 0)
            return false;

        var hasLetter = false;
        foreach (var ch in decoded)
        {
            if (char.IsControl(ch) && ch is not ('\r' or '\n' or '\t'))
                return false;
            if (ch > maxChar || char.GetUnicodeCategory(ch) is UnicodeCategory.OtherNotAssigned
                    or UnicodeCategory.PrivateUse)
                return false;
            if (char.IsLetter(ch))
                hasLetter = true;
        }

        if (!hasLetter)
            return false;

        text = decoded;
        return true;
    }
}
