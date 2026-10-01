using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
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

    private const string PemBeginMarker = "-----BEGIN ";

    [GeneratedRegex(@"(?<![A-Za-z0-9+/_-])[A-Za-z0-9+/_-]+={0,2}(?![A-Za-z0-9+/=_-])", RegexOptions.CultureInvariant)]
    private static partial Regex CandidateRegex();

    [GeneratedRegex(@"-----BEGIN (?<label>[A-Z0-9][A-Z0-9 ]*)-----(?<body>(?:[A-Za-z0-9+/=\s]|\\[rn])+?)-----END \k<label>-----",
        RegexOptions.CultureInvariant)]
    private static partial Regex PemRegex();

    [GeneratedRegex(@"\s|\\[rn]", RegexOptions.CultureInvariant)]
    private static partial Regex PemBodySeparatorRegex();

    public static bool ContainsBase64(string? source) => Detect(source) != Base64Content.None;

    public static Base64Content Detect(string? source)
    {
        TryDecode(source, Base64Content.None, out _, out var found);
        return found;
    }

    public static bool TryDecode(string? source, out string decoded) =>
        TryDecode(source, Base64Content.All, out decoded, out _);

    public static bool TryDecode(string? source, Base64Content decode, out string decoded, out Base64Content found)
    {
        decoded = string.Empty;
        found = Base64Content.None;
        if (string.IsNullOrEmpty(source) || source.Length > MaxSourceLength)
            return false;

        var decodePem = decode.HasFlag(Base64Content.Pem);
        var decodeBase64 = decode.HasFlag(Base64Content.Base64);

        var (start, length) = GetWholeTextRange(source);
        if (length >= MinWholeTextLength && TryDecodeToken(source.AsSpan(start, length), out var whole))
        {
            found = Base64Content.Base64;
            if (!decodeBase64)
                return false;

            decoded = string.Concat(source.AsSpan(0, start), whole, source.AsSpan(start + length));
            return true;
        }

        var replacements = new List<(int index, int length, string text)>();
        var offset = 0;
        if (source.Contains(PemBeginMarker, StringComparison.Ordinal))
        {
            foreach (Match pem in PemRegex().Matches(source))
            {
                if (!TryDecodePem(pem, out var text))
                    continue;

                found |= AddFragmentReplacements(source, offset, pem.Index - offset,
                    decodeBase64 ? replacements : null);
                found |= Base64Content.Pem;
                if (decodePem)
                    replacements.Add((pem.Index, pem.Length, text));
                offset = pem.Index + pem.Length;
            }
        }

        found |= AddFragmentReplacements(source, offset, source.Length - offset,
            decodeBase64 ? replacements : null);
        if (replacements.Count == 0)
            return false;

        var builder = new StringBuilder(source.Length);
        offset = 0;
        foreach (var (index, replacedLength, text) in replacements)
        {
            builder.Append(source, offset, index - offset);
            builder.Append(text);
            offset = index + replacedLength;
        }

        builder.Append(source, offset, source.Length - offset);
        decoded = builder.ToString();
        return true;
    }

    private static Base64Content AddFragmentReplacements(string source, int start, int length,
        List<(int index, int length, string text)>? replacements)
    {
        var found = Base64Content.None;
        if (length < MinFragmentLength)
            return found;

        foreach (var match in CandidateRegex().EnumerateMatches(source.AsSpan(start, length)))
        {
            if (match.Length < MinFragmentLength)
                continue;

            var index = start + match.Index;
            if (TryDecodeToken(source.AsSpan(index, match.Length), out var text))
            {
                found = Base64Content.Base64;
                replacements?.Add((index, match.Length, text));
            }
            else if (TryDecodeGluedToken(source.AsSpan(index, match.Length), out var gluedOffset, out var gluedLength, out text))
            {
                found = Base64Content.Base64;
                replacements?.Add((index + gluedOffset, gluedLength, text));
            }

            if (found != Base64Content.None && replacements == null)
                return found;
        }

        return found;
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
        var bytes = ArrayPool<byte>.Shared.Rent(paddedLength / 4 * 3);
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

            if (!Convert.TryFromBase64Chars(chars.AsSpan(0, paddedLength), bytes, out var written) || written == 0)
                return false;

            if (!TryGetReadableText(bytes.AsSpan(0, written), out var text))
                return false;

            decoded = text;
            return true;
        }
        finally
        {
            ArrayPool<char>.Shared.Return(chars);
            ArrayPool<byte>.Shared.Return(bytes);
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
