using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Unicode;

namespace LogGrokX.Data;

public static partial class Base64Detector
{
    public const int MinWholeTextLength = 8;
    public const int MinFragmentLength = 16;
    public const int MaxSourceLength = 1024 * 1024;
    public const int MaxHexDumpBytes = 4096;
    private const int MaxGluedTokenAttempts = 16;
    private const int MinBinaryKeyLength = 64;

    private const string PemBeginMarker = "-----BEGIN ";

    [GeneratedRegex(@"(?<![A-Za-z0-9+/_-])[A-Za-z0-9+/_-]+={0,2}(?![A-Za-z0-9+/=_-])", RegexOptions.CultureInvariant)]
    private static partial Regex CandidateRegex();

    [GeneratedRegex(@"-----BEGIN (?<label>[A-Z0-9][A-Z0-9 ]*)-----(?<body>(?:[A-Za-z0-9+/=\s]|\\[rn])+?)-----END \k<label>-----",
        RegexOptions.CultureInvariant)]
    private static partial Regex PemRegex();

    [GeneratedRegex(@"\s|\\[rn]", RegexOptions.CultureInvariant)]
    private static partial Regex PemBodySeparatorRegex();

    private static readonly JsonSerializerOptions RelaxedJson = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static bool ContainsBase64(string? source) => Detect(source) != Base64Content.None;

    public static Base64Content Detect(string? source, IReadOnlyList<StructuredSpan>? structuredSpans = null)
    {
        TryDecode(source, Base64Content.None, out _, out var found, structuredSpans);
        return found;
    }

    public static bool TryDecode(string? source, out string decoded) =>
        TryDecode(source, Base64Content.All, out decoded, out _);

    public static bool TryDecode(string? source, Base64Content decode, out string decoded, out Base64Content found,
        IReadOnlyList<StructuredSpan>? structuredSpans = null)
    {
        decoded = string.Empty;
        found = Base64Content.None;
        if (string.IsNullOrEmpty(source) || source.Length > MaxSourceLength)
            return false;

        var replacements = new List<(int index, int length, string text)>();
        if (structuredSpans is not { Count: > 0 })
        {
            CollectPlain(source, decode, replacements, ref found);
        }
        else
        {
            var position = 0;
            foreach (var span in structuredSpans.OrderBy(s => s.Start))
            {
                if (span.Start < position || span.Length <= 0 || span.Start + span.Length > source.Length)
                    continue;

                CollectPlain(source, position, span.Start - position, decode, replacements, ref found);
                if (span.IsXml)
                    CollectXml(source, span.Start, span.Length, decode, replacements, ref found);
                else
                    CollectJson(source, span.Start, span.Length, decode, replacements, ref found);
                position = span.Start + span.Length;
            }

            CollectPlain(source, position, source.Length - position, decode, replacements, ref found);
        }

        if (replacements.Count == 0)
            return false;

        decoded = Apply(source, replacements);
        return true;
    }

    private static string Apply(string source, List<(int index, int length, string text)> replacements)
    {
        replacements.Sort(static (x, y) => x.index.CompareTo(y.index));
        var builder = new StringBuilder(source.Length);
        var offset = 0;
        foreach (var (index, replacedLength, text) in replacements)
        {
            builder.Append(source, offset, index - offset);
            builder.Append(text);
            offset = index + replacedLength;
        }

        builder.Append(source, offset, source.Length - offset);
        return builder.ToString();
    }

    private static void CollectPlain(string source, int start, int length, Base64Content decode,
        List<(int index, int length, string text)> replacements, ref Base64Content found)
    {
        if (length <= 0)
            return;
        if (start == 0 && length == source.Length)
        {
            CollectPlain(source, decode, replacements, ref found);
            return;
        }

        var local = new List<(int index, int length, string text)>();
        CollectPlain(source.Substring(start, length), decode, local, ref found);
        foreach (var (index, replacedLength, text) in local)
            replacements.Add((index + start, replacedLength, text));
    }

    private static void CollectPlain(string source, Base64Content decode,
        List<(int index, int length, string text)> replacements, ref Base64Content found)
    {
        var decodePem = decode.HasFlag(Base64Content.Pem);
        var decodeBase64 = decode.HasFlag(Base64Content.Base64);

        var (start, length) = GetWholeTextRange(source);
        if (length >= MinWholeTextLength && TryDecodeToken(source.AsSpan(start, length), out var whole))
        {
            found |= Base64Content.Base64;
            if (decodeBase64)
                replacements.Add((start, length, whole));
            return;
        }

        if (TryDescribeBinaryKey(source.AsSpan(start, length), out var key))
        {
            found |= Base64Content.Pem;
            if (decodePem)
                replacements.Add((start, length, key));
            return;
        }

        var offset = 0;
        if (source.Contains(PemBeginMarker, StringComparison.Ordinal))
        {
            foreach (Match pem in PemRegex().Matches(source))
            {
                if (!TryDecodePem(pem, out var text))
                    continue;

                AddFragmentReplacements(source, offset, pem.Index - offset, decode, replacements, ref found);
                found |= Base64Content.Pem;
                if (decodePem)
                    replacements.Add((pem.Index, pem.Length, text));
                offset = pem.Index + pem.Length;
            }
        }

        AddFragmentReplacements(source, offset, source.Length - offset, decode, replacements, ref found);
    }

    private static string? DecodeValue(string value, Base64Content decode, ref Base64Content found)
    {
        var replacements = new List<(int index, int length, string text)>();
        CollectPlain(value, decode, replacements, ref found);
        return replacements.Count == 0 ? null : Apply(value, replacements);
    }

    private static void CollectJson(string source, int start, int length, Base64Content decode,
        List<(int index, int length, string text)> replacements, ref Base64Content found)
    {
        var end = start + length;
        var position = start;
        while (position < end)
        {
            var quote = source.IndexOf('"', position, end - position);
            if (quote < 0)
                return;

            var close = quote + 1;
            while (close < end && source[close] != '"')
                close += source[close] == '\\' ? 2 : 1;
            if (close >= end)
                return;

            position = close + 1;
            var next = position;
            while (next < end && char.IsWhiteSpace(source[next]))
                next++;
            if (next < end && source[next] == ':')
                continue;

            var literal = source.Substring(quote, close - quote + 1);
            string? value;
            try
            {
                value = JsonSerializer.Deserialize<string>(literal);
            }
            catch (JsonException)
            {
                continue;
            }

            if (string.IsNullOrEmpty(value) || DecodeValue(value, decode, ref found) is not { } decodedValue)
                continue;

            replacements.Add((quote, literal.Length, EncodeJsonValue(decodedValue)));
        }
    }

    private static string EncodeJsonValue(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length > 1 && trimmed[0] is '{' or '[')
        {
            try
            {
                using var _ = JsonDocument.Parse(trimmed);
                return trimmed;
            }
            catch (JsonException)
            {
            }
        }

        if (value.Contains('\n'))
        {
            var lines = value.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
            return JsonSerializer.Serialize(lines, RelaxedJson);
        }

        return JsonSerializer.Serialize(value, RelaxedJson);
    }

    private static void CollectXml(string source, int start, int length, Base64Content decode,
        List<(int index, int length, string text)> replacements, ref Base64Content found)
    {
        var end = start + length;
        var position = start;
        while (position < end)
        {
            var open = source.IndexOf('<', position, end - position);
            var textEnd = open < 0 ? end : open;
            if (textEnd > position)
                CollectXmlValue(source, position, textEnd - position, null, decode, replacements, ref found);
            if (open < 0)
                return;

            if (string.CompareOrdinal(source, open, "<![CDATA[", 0, 9) == 0)
            {
                var cdataEnd = source.IndexOf("]]>", open + 9, end - open - 9, StringComparison.Ordinal);
                if (cdataEnd < 0)
                    return;

                var content = source.Substring(open + 9, cdataEnd - open - 9);
                if (DecodeValue(content, decode, ref found) is { } decodedContent && !decodedContent.Contains("]]>"))
                    replacements.Add((open + 9, content.Length, decodedContent));
                position = cdataEnd + 3;
                continue;
            }

            if (string.CompareOrdinal(source, open, "<!--", 0, 4) == 0)
            {
                var commentEnd = source.IndexOf("-->", open + 4, end - open - 4, StringComparison.Ordinal);
                if (commentEnd < 0)
                    return;
                position = commentEnd + 3;
                continue;
            }

            var tagPosition = open + 1;
            while (tagPosition < end && source[tagPosition] != '>')
            {
                var c = source[tagPosition];
                if (c is '"' or '\'')
                {
                    var valueEnd = source.IndexOf(c, tagPosition + 1, end - tagPosition - 1);
                    if (valueEnd < 0)
                        return;

                    CollectXmlValue(source, tagPosition + 1, valueEnd - tagPosition - 1, c, decode,
                        replacements, ref found);
                    tagPosition = valueEnd + 1;
                    continue;
                }

                tagPosition++;
            }

            position = tagPosition + 1;
        }
    }

    private static void CollectXmlValue(string source, int start, int length, char? quote, Base64Content decode,
        List<(int index, int length, string text)> replacements, ref Base64Content found)
    {
        var raw = source.Substring(start, length);
        if (string.IsNullOrWhiteSpace(raw))
            return;

        var value = WebUtility.HtmlDecode(raw);
        if (DecodeValue(value, decode, ref found) is not { } decodedValue)
            return;

        var escaped = decodedValue.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        if (quote == '"')
            escaped = escaped.Replace("\"", "&quot;");
        else if (quote == '\'')
            escaped = escaped.Replace("'", "&apos;");
        replacements.Add((start, length, escaped));
    }

    private static void AddFragmentReplacements(string source, int start, int length, Base64Content decode,
        List<(int index, int length, string text)> replacements, ref Base64Content found)
    {
        if (length < MinFragmentLength)
            return;

        var decodePem = decode.HasFlag(Base64Content.Pem);
        var decodeBase64 = decode.HasFlag(Base64Content.Base64);
        foreach (var match in CandidateRegex().EnumerateMatches(source.AsSpan(start, length)))
        {
            if (match.Length < MinFragmentLength)
                continue;

            var index = start + match.Index;
            var token = source.AsSpan(index, match.Length);
            if (TryDecodeToken(token, out var text))
            {
                found |= Base64Content.Base64;
                if (decodeBase64)
                    replacements.Add((index, match.Length, text));
            }
            else if (TryDescribeBinaryKey(token, out text))
            {
                found |= Base64Content.Pem;
                if (decodePem)
                    replacements.Add((index, match.Length, text));
            }
            else if (TryDecodeGluedToken(token, out var gluedOffset, out var gluedLength, out text))
            {
                found |= Base64Content.Base64;
                if (decodeBase64)
                    replacements.Add((index + gluedOffset, gluedLength, text));
            }

            if (decode == Base64Content.None && found == Base64Content.All)
                return;
        }
    }

    private static bool TryDescribeBinaryKey(ReadOnlySpan<char> token, out string description)
    {
        description = string.Empty;
        if (token.Length < MinBinaryKeyLength || !(token.StartsWith("MI") || token.StartsWith("Bg")))
            return false;
        if (!TryDecodeBase64Bytes(token, out var data))
            return false;

        if (data[0] == 0x30)
        {
            if (TryDescribeCertificate(data, out var certificate))
            {
                description = "X.509 certificate\n" + certificate.TrimEnd();
                return true;
            }

            if (TryDescribeSubjectPublicKeyInfo(data, out var publicKey))
            {
                description = publicKey;
                return true;
            }

            return false;
        }

        return TryDescribeCryptoApiPublicKey(data, out description);
    }

    private static bool TryDescribeSubjectPublicKeyInfo(byte[] data, out string description)
    {
        description = string.Empty;
        try
        {
            var publicKey = PublicKey.CreateFromSubjectPublicKeyInfo(data, out var read);
            if (read != data.Length)
                return false;

            description = "Public key (SubjectPublicKeyInfo): " + DescribePublicKey(publicKey);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static bool TryDescribeCryptoApiPublicKey(byte[] data, out string description)
    {
        const int headerLength = 20;
        description = string.Empty;
        if (data.Length < headerLength || data[0] != 0x06 || data[1] != 0x02 ||
            data[8] != (byte)'R' || data[9] != (byte)'S' || data[10] != (byte)'A' || data[11] != (byte)'1')
            return false;

        var algorithm = BitConverter.ToUInt32(data, 4);
        var bitLength = BitConverter.ToInt32(data, 12);
        var exponent = BitConverter.ToUInt32(data, 16);
        var modulusLength = bitLength / 8;
        if (bitLength <= 0 || bitLength % 8 != 0 || data.Length < headerLength + modulusLength)
            return false;

        var modulus = data.AsSpan(headerLength, modulusLength).ToArray();
        Array.Reverse(modulus);

        var builder = new StringBuilder();
        builder.Append("RSA public key (CryptoAPI PUBLICKEYBLOB)\n");
        builder.Append("Algorithm: ").Append(algorithm switch
        {
            0xA400 => "CALG_RSA_KEYX",
            0x2400 => "CALG_RSA_SIGN",
            _ => "0x" + algorithm.ToString("X4", CultureInfo.InvariantCulture)
        }).Append('\n');
        builder.Append("Key size: ").Append(bitLength).Append(" bits\n");
        builder.Append("Public exponent: ").Append(exponent).Append('\n');
        builder.Append("Modulus SHA-1: ").Append(Convert.ToHexString(SHA1.HashData(modulus))).Append('\n');
        builder.Append("Modulus:");
        for (var i = 0; i < modulus.Length; i += 32)
            builder.Append('\n').Append(Convert.ToHexString(modulus, i, Math.Min(32, modulus.Length - i)));

        description = builder.ToString();
        return true;
    }

    private static bool TryDecodeGluedToken(ReadOnlySpan<char> candidate, out int offset, out int length,
        out string decoded)
    {
        offset = 0;
        length = 0;
        decoded = string.Empty;
        var bestScore = int.MinValue;

        void Consider(ReadOnlySpan<char> token, int tokenOffset, ref int offset, ref int length, ref string decoded)
        {
            if (!TryDecodeToken(token, out var text))
                return;

            var score = GetTextScore(text);
            if (score <= bestScore)
                return;

            bestScore = score;
            offset = tokenOffset;
            length = token.Length;
            decoded = text;
        }

        var attempts = 0;
        for (var i = 0; i < candidate.Length - MinFragmentLength && attempts < MaxGluedTokenAttempts; i++)
        {
            if (!IsGlueSeparator(candidate[i]))
                continue;

            attempts++;
            Consider(candidate[(i + 1)..], i + 1, ref offset, ref length, ref decoded);
        }

        attempts = 0;
        var body = candidate.TrimEnd('=');
        for (var i = body.Length - 1; i >= MinFragmentLength && attempts < MaxGluedTokenAttempts; i--)
        {
            if (!IsGlueSeparator(body[i]))
                continue;

            attempts++;
            Consider(candidate[..i], 0, ref offset, ref length, ref decoded);
        }

        return bestScore != int.MinValue;
    }

    private static bool IsGlueSeparator(char c) => c is '/' or '-' or '_' or '+';

    private static int GetTextScore(string text)
    {
        var score = 0;
        foreach (var c in text)
        {
            score += char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) || "\"{}[]:,.-_/=@".Contains(c)
                ? 1
                : -2;
        }

        return score;
    }

    private static bool TryDecodePem(Match pem, out string decoded)
    {
        decoded = string.Empty;
        var label = pem.Groups["label"].Value;
        var body = PemBodySeparatorRegex().Replace(pem.Groups["body"].Value, string.Empty);
        if (body.Length == 0 || body.Length % 4 != 0)
            return false;

        var bytes = new byte[body.Length / 4 * 3];
        if (!Convert.TryFromBase64String(body, bytes, out var written) || written == 0)
            return false;

        var data = bytes.AsSpan(0, written);
        var content = TryGetReadableText(data, out var text) ? text
            : IsCertificateLabel(label) && TryDescribeCertificate(data, out var description) ? description
            : FormatHexDump(data);

        decoded = $"{PemBeginMarker}{label}-----\n{content.TrimEnd()}\n-----END {label}-----";
        return true;
    }

    private static bool IsCertificateLabel(string label) =>
        label is "CERTIFICATE" or "TRUSTED CERTIFICATE" or "X509 CERTIFICATE";

    private static bool TryDescribeCertificate(ReadOnlySpan<byte> data, out string description)
    {
        description = string.Empty;
        try
        {
            using var certificate = X509CertificateLoader.LoadCertificate(data);
            var builder = new StringBuilder();
            builder.Append("Subject: ").Append(certificate.Subject).Append('\n');
            builder.Append("Issuer: ").Append(certificate.Issuer).Append('\n');
            builder.Append("Serial number: ").Append(certificate.SerialNumber).Append('\n');
            builder.Append("Not before: ").Append(FormatDate(certificate.NotBefore)).Append('\n');
            builder.Append("Not after: ").Append(FormatDate(certificate.NotAfter)).Append('\n');
            builder.Append("Thumbprint (SHA-1): ").Append(certificate.Thumbprint).Append('\n');
            builder.Append("Signature algorithm: ")
                .Append(certificate.SignatureAlgorithm.FriendlyName ?? certificate.SignatureAlgorithm.Value)
                .Append('\n');
            builder.Append("Public key: ").Append(DescribePublicKey(certificate.PublicKey)).Append('\n');

            var alternativeNames = certificate.Extensions.OfType<X509SubjectAlternativeNameExtension>()
                .SelectMany(e => e.EnumerateDnsNames()
                    .Concat(e.EnumerateIPAddresses().Select(a => a.ToString())))
                .ToList();
            if (alternativeNames.Count != 0)
                builder.Append("Subject alternative names: ").AppendJoin(", ", alternativeNames).Append('\n');

            description = builder.ToString();
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static string FormatDate(DateTime date) =>
        date.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);

    private static string DescribePublicKey(PublicKey publicKey)
    {
        var algorithm = publicKey.Oid.FriendlyName ?? publicKey.Oid.Value ?? "unknown";
        int? keySize = null;
        try
        {
            using var rsa = publicKey.GetRSAPublicKey();
            using var ecdsa = rsa == null ? publicKey.GetECDsaPublicKey() : null;
            keySize = rsa?.KeySize ?? ecdsa?.KeySize;
        }
        catch (CryptographicException)
        {
        }

        return keySize is { } size ? $"{algorithm} {size} bits" : algorithm;
    }

    public static string FormatHexDump(ReadOnlySpan<byte> data)
    {
        const int bytesPerLine = 16;
        var shown = Math.Min(data.Length, MaxHexDumpBytes);
        var builder = new StringBuilder();
        for (var line = 0; line < shown; line += bytesPerLine)
        {
            var count = Math.Min(bytesPerLine, shown - line);
            builder.Append(line.ToString("x8", CultureInfo.InvariantCulture)).Append("  ");
            for (var i = 0; i < bytesPerLine; i++)
            {
                if (i == 8)
                    builder.Append(' ');
                builder.Append(i < count
                    ? data[line + i].ToString("x2", CultureInfo.InvariantCulture) + " "
                    : "   ");
            }

            builder.Append(" |");
            for (var i = 0; i < count; i++)
            {
                var b = data[line + i];
                builder.Append(b is >= 0x20 and < 0x7F ? (char)b : '.');
            }

            builder.Append("|\n");
        }

        if (data.Length > shown)
            builder.Append("... ").Append(data.Length - shown).Append(" more bytes\n");

        return builder.ToString();
    }

    private static (int start, int length) GetWholeTextRange(string source)
    {
        var start = 0;
        var end = source.Length;
        while (start < end && char.IsWhiteSpace(source[start]))
            start++;
        while (end > start && char.IsWhiteSpace(source[end - 1]))
            end--;

        if (end - start >= 2 && source[start] is '"' or '\'' && source[end - 1] == source[start])
        {
            start++;
            end--;
        }

        return (start, end - start);
    }

    public static bool TryDecodeToken(ReadOnlySpan<char> token, out string decoded)
    {
        decoded = string.Empty;
        return TryDecodeBase64Bytes(token, out var data) && TryGetReadableText(data, out decoded);
    }

    private static bool TryDecodeBase64Bytes(ReadOnlySpan<char> token, out byte[] data)
    {
        data = Array.Empty<byte>();

        var body = token.TrimEnd('=');
        var padding = token.Length - body.Length;
        if (body.IsEmpty || padding > 2 || body.Length % 4 == 1)
            return false;
        if (padding > 0 && (body.Length + padding) % 4 != 0)
            return false;

        var standard = body.IndexOfAny('+', '/') >= 0;
        var urlSafe = body.IndexOfAny('-', '_') >= 0;
        if (standard && urlSafe)
            return false;

        var paddedLength = (body.Length + 3) / 4 * 4;
        var chars = ArrayPool<char>.Shared.Rent(paddedLength);
        try
        {
            for (var i = 0; i < body.Length; i++)
            {
                chars[i] = body[i] switch
                {
                    '-' => '+',
                    '_' => '/',
                    var c => c
                };
            }

            for (var i = body.Length; i < paddedLength; i++)
                chars[i] = '=';

            var bytes = new byte[paddedLength / 4 * 3];
            if (!Convert.TryFromBase64Chars(chars.AsSpan(0, paddedLength), bytes, out var written) || written == 0)
                return false;

            data = written == bytes.Length ? bytes : bytes.AsSpan(0, written).ToArray();
            return true;
        }
        finally
        {
            ArrayPool<char>.Shared.Return(chars);
        }
    }

    private static bool TryGetReadableText(ReadOnlySpan<byte> data, out string text)
    {
        text = string.Empty;
        if (!Utf8.IsValid(data))
            return false;

        var decoded = Encoding.UTF8.GetString(data);
        if (!IsReadableText(decoded))
            return false;

        text = decoded;
        return true;
    }

    private static bool IsReadableText(string text)
    {
        var hasVisible = false;
        foreach (var c in text)
        {
            if (c is '\t' or '\r' or '\n')
                continue;
            if (char.IsControl(c) || c == '\uFFFD')
                return false;
            if (!char.IsWhiteSpace(c))
                hasVisible = true;
        }

        return hasVisible;
    }
}
