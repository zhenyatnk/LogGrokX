using System;
using System.Collections.Generic;
using System.Linq;

namespace LogGrokX;

internal sealed class XmlSourceMap : StructuredSourceMap
{
    private enum TokenKind
    {
        Markup,
        Text,
        Comment,
        CData,
        ProcessingInstruction,
        Declaration
    }

    private readonly record struct Token(TokenKind Kind, int Start, int Length);

    private XmlSourceMap(string source, string formatted, List<int> sourcePositions,
        List<int> formattedPositions) : base(source, formatted, sourcePositions, formattedPositions) { }

    public static bool TryCreate(string source, string formatted, out XmlSourceMap? map)
    {
        map = null;
        var sourceTokens = Tokenize(source);
        var formattedTokens = Tokenize(formatted);
        var sourcePositions = new List<int>();
        var formattedPositions = new List<int>();
        var sourceIndex = 0;
        var formattedIndex = 0;

        while (sourceIndex < sourceTokens.Count || formattedIndex < formattedTokens.Count)
        {
            if (formattedIndex < formattedTokens.Count &&
                IsWhitespaceText(formatted, formattedTokens[formattedIndex]) &&
                (sourceIndex >= sourceTokens.Count || sourceTokens[sourceIndex].Kind != TokenKind.Text))
            {
                formattedIndex++;
                continue;
            }

            if (sourceIndex < sourceTokens.Count && IsDocumentWhitespace(source, sourceTokens, sourceIndex) &&
                (formattedIndex >= formattedTokens.Count || formattedTokens[formattedIndex].Kind != TokenKind.Text))
            {
                sourceIndex++;
                continue;
            }

            if (sourceIndex < sourceTokens.Count && formattedIndex < formattedTokens.Count &&
                (IsDocumentWhitespace(source, sourceTokens, sourceIndex) ||
                 IsDocumentWhitespace(formatted, formattedTokens, formattedIndex)))
            {
                sourceIndex++;
                formattedIndex++;
                continue;
            }

            if (sourceIndex >= sourceTokens.Count || formattedIndex >= formattedTokens.Count)
                return false;

            var sourceToken = sourceTokens[sourceIndex];
            var formattedToken = formattedTokens[formattedIndex];
            if (sourceToken.Kind != formattedToken.Kind)
                return false;

            var matched = sourceToken.Kind switch
            {
                TokenKind.Markup or TokenKind.Declaration => CompareMarkup(source, formatted, sourceToken,
                    formattedToken, sourcePositions, formattedPositions),
                TokenKind.ProcessingInstruction => CompareProcessingInstruction(source, formatted, sourceToken,
                    formattedToken, sourcePositions, formattedPositions),
                _ => CompareExactText(source, formatted, sourceToken, formattedToken,
                    sourcePositions, formattedPositions)
            };
            if (!matched)
                return false;

            sourceIndex++;
            formattedIndex++;
        }

        if (sourcePositions.Count == 0)
            return false;
        map = new XmlSourceMap(source, formatted, sourcePositions, formattedPositions);
        return true;
    }

    private static List<Token> Tokenize(string text)
    {
        var result = new List<Token>();
        var position = 0;
        while (position < text.Length)
        {
            if (text[position] != '<')
            {
                var end = text.IndexOf('<', position);
                if (end < 0) end = text.Length;
                result.Add(new Token(TokenKind.Text, position, end - position));
                position = end;
                continue;
            }

            if (text.AsSpan(position).StartsWith("<!--"))
            {
                var end = text.IndexOf("-->", position + 4, StringComparison.Ordinal);
                end = end < 0 ? text.Length : end + 3;
                result.Add(new Token(TokenKind.Comment, position, end - position));
                position = end;
                continue;
            }

            if (text.AsSpan(position).StartsWith("<![CDATA["))
            {
                var end = text.IndexOf("]]>", position + 9, StringComparison.Ordinal);
                end = end < 0 ? text.Length : end + 3;
                result.Add(new Token(TokenKind.CData, position, end - position));
                position = end;
                continue;
            }

            if (text.AsSpan(position).StartsWith("<?"))
            {
                var end = text.IndexOf("?>", position + 2, StringComparison.Ordinal);
                end = end < 0 ? text.Length : end + 2;
                var kind = text.AsSpan(position).StartsWith("<?xml") &&
                           position + 5 < text.Length && char.IsWhiteSpace(text[position + 5])
                    ? TokenKind.Declaration
                    : TokenKind.ProcessingInstruction;
                result.Add(new Token(kind, position, end - position));
                position = end;
                continue;
            }

            var quote = '\0';
            var tagEnd = position + 1;
            for (; tagEnd < text.Length; tagEnd++)
            {
                var ch = text[tagEnd];
                if (quote != '\0')
                {
                    if (ch == quote) quote = '\0';
                    continue;
                }

                if (ch is '\'' or '"') quote = ch;
                else if (ch == '>') break;
            }

            tagEnd = Math.Min(tagEnd + 1, text.Length);
            result.Add(new Token(TokenKind.Markup, position, tagEnd - position));
            position = tagEnd;
        }

        return result;
    }

    private static bool CompareMarkup(string source, string formatted, Token sourceToken, Token formattedToken,
        List<int> sourcePositions, List<int> formattedPositions)
    {
        var sourceIndex = sourceToken.Start;
        var formattedIndex = formattedToken.Start;
        var sourceEnd = sourceToken.Start + sourceToken.Length;
        var formattedEnd = formattedToken.Start + formattedToken.Length;
        var sourceQuote = '\0';
        var formattedQuote = '\0';

        while (sourceIndex < sourceEnd || formattedIndex < formattedEnd)
        {
            if (sourceQuote == '\0' && formattedQuote == '\0')
            {
                SkipMarkupWhitespace(source, ref sourceIndex, sourceEnd);
                SkipMarkupWhitespace(formatted, ref formattedIndex, formattedEnd);
            }

            if (sourceIndex >= sourceEnd || formattedIndex >= formattedEnd)
                break;

            var sourceChar = source[sourceIndex];
            var formattedChar = formatted[formattedIndex];
            if (sourceQuote == '\0' && formattedQuote == '\0' && IsQuote(sourceChar) && IsQuote(formattedChar))
            {
                sourcePositions.Add(sourceIndex);
                formattedPositions.Add(formattedIndex);
                sourceQuote = sourceChar;
                formattedQuote = formattedChar;
                sourceIndex++;
                formattedIndex++;
                continue;
            }

            if (sourceQuote != '\0' && formattedQuote != '\0' &&
                sourceChar == sourceQuote && formattedChar == formattedQuote)
            {
                sourcePositions.Add(sourceIndex);
                formattedPositions.Add(formattedIndex);
                sourceQuote = '\0';
                formattedQuote = '\0';
                sourceIndex++;
                formattedIndex++;
                continue;
            }

            if (sourceChar != formattedChar)
                return false;

            sourcePositions.Add(sourceIndex);
            formattedPositions.Add(formattedIndex);
            sourceIndex++;
            formattedIndex++;
        }

        return sourceIndex == sourceEnd && formattedIndex == formattedEnd &&
               sourceQuote == '\0' && formattedQuote == '\0';
    }

    private static bool CompareProcessingInstruction(string source, string formatted, Token sourceToken,
        Token formattedToken, List<int> sourcePositions, List<int> formattedPositions)
    {
        var sourceIndex = sourceToken.Start + 2;
        var formattedIndex = formattedToken.Start + 2;
        var sourceEnd = sourceToken.Start + sourceToken.Length;
        var formattedEnd = formattedToken.Start + formattedToken.Length;

        while (sourceIndex < sourceEnd && formattedIndex < formattedEnd &&
               !char.IsWhiteSpace(source[sourceIndex]) && !char.IsWhiteSpace(formatted[formattedIndex]) &&
               source[sourceIndex] != '?' && formatted[formattedIndex] != '?')
        {
            if (source[sourceIndex] != formatted[formattedIndex])
                return false;
            sourcePositions.Add(sourceIndex);
            formattedPositions.Add(formattedIndex);
            sourceIndex++;
            formattedIndex++;
        }

        SkipMarkupWhitespace(source, ref sourceIndex, sourceEnd);
        SkipMarkupWhitespace(formatted, ref formattedIndex, formattedEnd);
        var remainingSource = new Token(TokenKind.Text, sourceIndex, sourceEnd - sourceIndex);
        var remainingFormatted = new Token(TokenKind.Text, formattedIndex, formattedEnd - formattedIndex);
        return CompareExactText(source, formatted, remainingSource, remainingFormatted,
            sourcePositions, formattedPositions);
    }

    private static bool CompareExactText(string source, string formatted, Token sourceToken, Token formattedToken,
        List<int> sourcePositions, List<int> formattedPositions)
    {
        var sourceIndex = sourceToken.Start;
        var formattedIndex = formattedToken.Start;
        var sourceEnd = sourceToken.Start + sourceToken.Length;
        var formattedEnd = formattedToken.Start + formattedToken.Length;

        while (sourceIndex < sourceEnd && formattedIndex < formattedEnd)
        {
            var sourceNewline = NewlineLength(source, sourceIndex);
            var formattedNewline = NewlineLength(formatted, formattedIndex);
            if (sourceNewline > 0 || formattedNewline > 0)
            {
                if (sourceNewline == 0 || formattedNewline == 0)
                    return false;
                sourcePositions.Add(sourceIndex);
                formattedPositions.Add(formattedIndex);
                sourceIndex += sourceNewline;
                formattedIndex += formattedNewline;
                continue;
            }

            if (source[sourceIndex] != formatted[formattedIndex])
                return false;
            sourcePositions.Add(sourceIndex);
            formattedPositions.Add(formattedIndex);
            sourceIndex++;
            formattedIndex++;
        }

        return sourceIndex == sourceEnd && formattedIndex == formattedEnd;
    }

    private static int NewlineLength(string text, int position) =>
        text[position] == '\n' ? 1 :
        text[position] == '\r' && position + 1 < text.Length && text[position + 1] == '\n' ? 2 :
        text[position] == '\r' ? 1 : 0;

    private static bool IsWhitespaceText(string text, Token token) =>
        token.Kind == TokenKind.Text && text.AsSpan(token.Start, token.Length).IsWhiteSpace();

    private static bool IsDocumentWhitespace(string text, IReadOnlyList<Token> tokens, int index)
    {
        if (!IsWhitespaceText(text, tokens[index]))
            return false;
        var hasMarkupBefore = tokens.Take(index).Any(t => t.Kind == TokenKind.Markup);
        var hasMarkupAfter = tokens.Skip(index + 1).Any(t => t.Kind == TokenKind.Markup);
        return !hasMarkupBefore || !hasMarkupAfter;
    }

    private static void SkipMarkupWhitespace(string text, ref int position, int end)
    {
        while (position < end && char.IsWhiteSpace(text[position]))
            position++;
    }

    private static bool IsQuote(char ch) => ch is '\'' or '"';
}
