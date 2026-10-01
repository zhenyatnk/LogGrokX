using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LogGrokX;

public static partial class HexText
{
    private const int MinBytes = 4;
    private const int MinSourceLength = MinBytes * 2;
    // Random bytes read as UTF-16 often look like CJK text, so only alphabets below U+0800
    // (Latin, Cyrillic, Greek, ...) are accepted for UTF-16.
    private const char MaxUtf16Char = '\u07FF';

    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);
    private static readonly Encoding StrictUtf16 = new UnicodeEncoding(false, false, true);

    [GeneratedRegex(@"(?<![0-9A-Za-z])(?:0[xX])?[0-9A-Fa-f]{2}(?:(?:[ \-:]|, ?)?(?:0[xX])?[0-9A-Fa-f]{2}){3,}(?![0-9A-Za-z])",
        RegexOptions.CultureInvariant)]
    private static partial Regex HexRunRegex();

    public static bool ContainsDecodableHex(string source)
    {
        if (source.Length < MinSourceLength)
            return false;

        foreach (Match match in HexRunRegex().Matches(source))
        {
            if (TryDecodeRun(match.ValueSpan, out _))
                return true;
        }

        return false;
    }

    public static bool TryDecode(string source, out string decoded)
    {
        decoded = source;
        if (source.Length < MinSourceLength)
            return false;

        var found = false;
        var result = HexRunRegex().Replace(source, match =>
        {
            if (!TryDecodeRun(match.ValueSpan, out var text))
                return match.Value;

            found = true;
            return text;
        });

        if (!found)
            return false;

        decoded = result;
        return true;
    }

    private static bool TryDecodeRun(ReadOnlySpan<char> run, out string text)
    {
        text = string.Empty;
        var bytes = ParseBytes(run);
        if (bytes.Length < MinBytes)
            return false;

        return TryGetPrintableText(bytes, StrictUtf8, char.MaxValue, out text) ||
               (bytes.Length % 2 == 0 && TryGetPrintableText(bytes, StrictUtf16, MaxUtf16Char, out text));
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
