using System;
using System.Collections.Generic;
using System.IO;

namespace LogGrokX.Data.Symbols;

public enum SourceTokenKind
{
    Text,
    Keyword,
    Type,
    Preprocessor,
    Comment,
    String,
    Number
}

public readonly record struct SourceToken(int Start, int Length, SourceTokenKind Kind);

// A line-based C/C++ lexer for the source viewer: block comments and raw strings continue across lines.
public static class CppSyntax
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".c", ".cc", ".cpp", ".cxx", ".c++", ".h", ".hh", ".hpp", ".hxx", ".h++", ".inl", ".ipp", ".tpp", ".ixx", ".cppm"
    };

    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "alignas", "alignof", "asm", "break", "case", "catch", "class", "concept", "const", "const_cast", "consteval",
        "constexpr", "constinit", "continue", "co_await", "co_return", "co_yield", "decltype", "default", "delete", "do",
        "dynamic_cast", "else", "enum", "explicit", "export", "extern", "false", "final", "for", "friend", "goto", "if",
        "import", "inline", "module", "mutable", "namespace", "new", "noexcept", "nullptr", "operator", "override",
        "private", "protected", "public", "register", "reinterpret_cast", "requires", "return", "sizeof", "static",
        "static_assert", "static_cast", "struct", "switch", "template", "this", "thread_local", "throw", "true", "try",
        "typedef", "typeid", "typename", "union", "using", "virtual", "volatile", "while", "__cdecl", "__stdcall",
        "__fastcall", "__declspec", "__forceinline", "__inline", "__try", "__except", "__finally", "__leave"
    };

    private static readonly HashSet<string> Types = new(StringComparer.Ordinal)
    {
        "auto", "bool", "char", "char8_t", "char16_t", "char32_t", "double", "float", "int", "long", "short", "signed",
        "unsigned", "void", "wchar_t", "size_t", "ptrdiff_t", "intptr_t", "uintptr_t", "int8_t", "int16_t", "int32_t",
        "int64_t", "uint8_t", "uint16_t", "uint32_t", "uint64_t", "__int8", "__int16", "__int32", "__int64",
        "BOOL", "BYTE", "WORD", "DWORD", "DWORD64", "QWORD", "UINT", "ULONG", "LONG", "LONGLONG", "ULONGLONG", "HANDLE",
        "HRESULT", "LPVOID", "LPCVOID", "PVOID", "LPSTR", "LPCSTR", "LPWSTR", "LPCWSTR", "WCHAR", "TCHAR", "SIZE_T"
    };

    public static bool IsCppFile(string? path) =>
        !string.IsNullOrEmpty(path) && Extensions.Contains(Path.GetExtension(path));

    public static IReadOnlyList<SourceToken>[] Tokenize(IReadOnlyList<string> lines)
    {
        var result = new IReadOnlyList<SourceToken>[lines.Count];
        var state = new State();
        for (var i = 0; i < lines.Count; i++)
            result[i] = TokenizeLine(lines[i], ref state);
        return result;
    }

    private struct State
    {
        public bool InBlockComment;
        public string? RawDelimiter;
    }

    private static List<SourceToken> TokenizeLine(string line, ref State state)
    {
        var tokens = new List<SourceToken>();
        var position = 0;
        if (state.InBlockComment)
        {
            position = EndOfBlockComment(line, 0, ref state);
            Add(tokens, 0, position, SourceTokenKind.Comment);
        }
        else if (state.RawDelimiter != null)
        {
            position = EndOfRawString(line, 0, ref state);
            Add(tokens, 0, position, SourceTokenKind.String);
        }

        var directive = false;
        while (position < line.Length)
        {
            var c = line[position];
            var start = position;
            if (c == '/' && Next(line, position) == '/')
            {
                Add(tokens, start, line.Length - start, SourceTokenKind.Comment);
                break;
            }

            if (c == '/' && Next(line, position) == '*')
            {
                state.InBlockComment = true;
                position = EndOfBlockComment(line, position + 2, ref state);
                Add(tokens, start, position - start, SourceTokenKind.Comment);
                continue;
            }

            if (c == '#' && line.AsSpan(0, position).IsWhiteSpace())
            {
                position++;
                while (position < line.Length && char.IsWhiteSpace(line[position]))
                    position++;
                while (position < line.Length && IsIdentifierPart(line[position]))
                    position++;
                directive = line.AsSpan(start, position - start).EndsWith("include", StringComparison.Ordinal);
                Add(tokens, start, position - start, SourceTokenKind.Preprocessor);
                continue;
            }

            if (directive && c == '<')
            {
                var close = line.IndexOf('>', position);
                position = close < 0 ? line.Length : close + 1;
                Add(tokens, start, position - start, SourceTokenKind.String);
                continue;
            }

            if (char.IsDigit(c) || c == '.' && position + 1 < line.Length && char.IsDigit(line[position + 1]))
            {
                position = EndOfNumber(line, position);
                Add(tokens, start, position - start, SourceTokenKind.Number);
                continue;
            }

            if (IsIdentifierStart(c))
            {
                while (position < line.Length && IsIdentifierPart(line[position]))
                    position++;
                var word = line.Substring(start, position - start);
                if (position < line.Length && line[position] is '"' or '\'' && IsLiteralPrefix(word))
                {
                    position = word.EndsWith('R') && line[position] == '"'
                        ? StartRawString(line, position, ref state)
                        : EndOfQuoted(line, position);
                    Add(tokens, start, position - start, SourceTokenKind.String);
                    continue;
                }

                Add(tokens, start, word.Length,
                    Keywords.Contains(word) ? SourceTokenKind.Keyword
                    : Types.Contains(word) ? SourceTokenKind.Type
                    : SourceTokenKind.Text);
                continue;
            }

            if (c is '"' or '\'')
            {
                position = EndOfQuoted(line, position);
                Add(tokens, start, position - start, SourceTokenKind.String);
                continue;
            }

            position++;
            Add(tokens, start, 1, SourceTokenKind.Text);
        }

        return tokens;
    }

    private static char Next(string line, int position) => position + 1 < line.Length ? line[position + 1] : '\0';

    private static bool IsIdentifierStart(char c) => char.IsLetter(c) || c == '_';

    private static bool IsIdentifierPart(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static bool IsLiteralPrefix(string word) =>
        word is "L" or "u" or "U" or "u8" or "R" or "LR" or "uR" or "UR" or "u8R";

    private static int EndOfBlockComment(string line, int position, ref State state)
    {
        var end = line.IndexOf("*/", position, StringComparison.Ordinal);
        if (end < 0)
            return line.Length;
        state.InBlockComment = false;
        return end + 2;
    }

    private static int StartRawString(string line, int quote, ref State state)
    {
        var open = line.IndexOf('(', quote + 1);
        if (open < 0)
            return EndOfQuoted(line, quote);
        state.RawDelimiter = ")" + line.Substring(quote + 1, open - quote - 1) + "\"";
        return EndOfRawString(line, open + 1, ref state);
    }

    private static int EndOfRawString(string line, int position, ref State state)
    {
        var end = line.IndexOf(state.RawDelimiter!, position, StringComparison.Ordinal);
        if (end < 0)
            return line.Length;
        var length = state.RawDelimiter!.Length;
        state.RawDelimiter = null;
        return end + length;
    }

    private static int EndOfQuoted(string line, int position)
    {
        var quote = line[position++];
        while (position < line.Length)
        {
            var c = line[position++];
            if (c == '\\')
                position++;
            else if (c == quote)
                break;
        }

        return Math.Min(position, line.Length);
    }

    private static int EndOfNumber(string line, int position)
    {
        while (position < line.Length)
        {
            var c = line[position];
            if (c is '+' or '-' && line[position - 1] is 'e' or 'E' or 'p' or 'P' &&
                !(line[position - 1] is 'e' or 'E' && IsHexLiteral(line, position)))
                position++;
            else if (char.IsLetterOrDigit(c) || c is '.' or '_' or '\'')
                position++;
            else
                break;
        }

        return position;
    }

    private static bool IsHexLiteral(string line, int end)
    {
        var start = end;
        while (start > 0 && (char.IsLetterOrDigit(line[start - 1]) || line[start - 1] is '.' or '\''))
            start--;
        return end - start > 1 && line[start] == '0' && line[start + 1] is 'x' or 'X' &&
               line.AsSpan(start, end - start).IndexOfAny('p', 'P') < 0;
    }

    private static void Add(List<SourceToken> tokens, int start, int length, SourceTokenKind kind)
    {
        if (length <= 0)
            return;
        if (tokens.Count > 0 && tokens[^1] is { } last && last.Kind == kind && last.Start + last.Length == start)
            tokens[^1] = last with { Length = last.Length + length };
        else
            tokens.Add(new SourceToken(start, length, kind));
    }
}
