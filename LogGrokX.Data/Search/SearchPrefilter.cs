using System;
using System.Text;
using System.Text.RegularExpressions;

namespace LogGrokX.Data.Search;

/// <summary>
/// Cheap byte-level pre-check for a search pattern.
/// <para>
/// Searching used to decode every single line to UTF-16 and run the regex on it,
/// even though typically far less than 1% of lines match. When a required literal
/// can be extracted from the pattern, the literal is searched directly in the raw
/// bytes and non-matching lines are skipped without decoding.
/// </para>
/// <para>
/// The filter is conservative: it never rejects a line the regex would match.
/// </para>
/// </summary>
public sealed class SearchPrefilter
{
    private const int MinLiteralLength = 2;

    private readonly byte[] _literal;

    private SearchPrefilter(byte[] literal) => _literal = literal;

    public static SearchPrefilter? TryCreate(Regex regex, Encoding encoding)
    {
        if (!IsByteSearchSafe(encoding))
            return null;

        if ((regex.Options & RegexOptions.IgnoreCase) != 0)
            return null;

        if (!TryGetRequiredLiteral(regex.ToString(), out var literal))
            return null;

        byte[] bytes;
        try
        {
            bytes = encoding.GetBytes(literal);
        }
        catch (EncoderFallbackException)
        {
            return null;
        }

        return bytes.Length == 0 ? null : new SearchPrefilter(bytes);
    }

    public bool MayContainMatch(ReadOnlySpan<byte> data) => data.IndexOf(_literal) >= 0;

    private static bool IsByteSearchSafe(Encoding encoding)
    {
        if (encoding.IsSingleByte)
            return true;

        return encoding.CodePage switch
        {
            65001 => true, // utf-8
            1200 => true, // utf-16 LE
            1201 => true, // utf-16 BE
            12000 => true, // utf-32 LE
            12001 => true, // utf-32 BE
            _ => false
        };
    }

    /// <summary>
    /// Extracts the longest literal substring that any match of the pattern must contain.
    /// Returns false when no such literal can be proven (alternations, character
    /// classes only, inline options and so on).
    /// </summary>
    internal static bool TryGetRequiredLiteral(string pattern, out string literal)
    {
        literal = string.Empty;
        if (string.IsNullOrEmpty(pattern))
            return false;

        var current = new StringBuilder();
        var best = string.Empty;

        void Flush(StringBuilder builder, ref string bestSoFar)
        {
            if (builder.Length > bestSoFar.Length)
                bestSoFar = builder.ToString();
            builder.Clear();
        }

        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            switch (c)
            {
                case '|':
                    // A literal required by one branch is not required by the match.
                    return false;
                case '(':
                    if (i + 1 < pattern.Length && pattern[i + 1] == '?')
                        return false; // inline options / lookarounds: stay on the safe side
                    Flush(current, ref best);
                    break;
                case ')':
                case '[':
                case ']':
                case '.':
                case '^':
                case '$':
                case '+':
                    Flush(current, ref best);
                    break;
                case '*':
                case '?':
                case '{':
                    // Quantifier: the preceding character may be absent.
                    if (current.Length > 0)
                        current.Length--;
                    Flush(current, ref best);
                    if (c == '{')
                    {
                        var close = pattern.IndexOf('}', i);
                        if (close < 0)
                            return false;
                        i = close;
                    }

                    break;
                case '\\':
                    if (i + 1 >= pattern.Length)
                        return false;
                    var next = pattern[++i];
                    switch (next)
                    {
                        case 'n':
                            current.Append('\n');
                            break;
                        case 'r':
                            current.Append('\r');
                            break;
                        case 't':
                            current.Append('\t');
                            break;
                        default:
                            if (char.IsLetterOrDigit(next))
                            {
                                // \d, \w, \s, \b, \uXXXX and friends
                                Flush(current, ref best);
                                return FinishWithBest(current, best, out literal);
                            }

                            current.Append(next);
                            break;
                    }

                    break;
                default:
                    current.Append(c);
                    break;
            }
        }

        return FinishWithBest(current, best, out literal);

        static bool FinishWithBest(StringBuilder current, string best, out string literal)
        {
            if (current.Length > best.Length)
                best = current.ToString();

            literal = best;
            return literal.Length >= MinLiteralLength;
        }
    }
}
