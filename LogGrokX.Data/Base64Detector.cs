using System;
using System.Buffers;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Unicode;

namespace LogGrokX.Data;

public static partial class Base64Detector
{
    public const int MinWholeTextLength = 8;
    public const int MinFragmentLength = 16;
    public const int MaxSourceLength = 1024 * 1024;

    [GeneratedRegex(@"(?<![A-Za-z0-9+/_-])[A-Za-z0-9+/_-]+={0,2}(?![A-Za-z0-9+/=_-])", RegexOptions.CultureInvariant)]
    private static partial Regex CandidateRegex();

    public static bool ContainsBase64(string? source) => TryDecode(source, out _);

    public static bool TryDecode(string? source, out string decoded)
    {
        decoded = string.Empty;
        if (string.IsNullOrEmpty(source) || source.Length > MaxSourceLength)
            return false;

        var (start, length) = GetWholeTextRange(source);
        if (length >= MinWholeTextLength && TryDecodeToken(source.AsSpan(start, length), out var whole))
        {
            decoded = string.Concat(source.AsSpan(0, start), whole, source.AsSpan(start + length));
            return true;
        }

        StringBuilder? builder = null;
        var offset = 0;
        foreach (var match in CandidateRegex().EnumerateMatches(source))
        {
            if (match.Length < MinFragmentLength)
                continue;

            var token = source.AsSpan(match.Index, match.Length);
            if (!TryDecodeToken(token, out var text))
                continue;

            builder ??= new StringBuilder(source.Length);
            builder.Append(source, offset, match.Index - offset);
            builder.Append(text);
            offset = match.Index + match.Length;
        }

        if (builder == null)
            return false;

        builder.Append(source, offset, source.Length - offset);
        decoded = builder.ToString();
        return true;
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

            var data = bytes.AsSpan(0, written);
            if (!Utf8.IsValid(data))
                return false;

            var text = Encoding.UTF8.GetString(data);
            if (!IsReadableText(text))
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
