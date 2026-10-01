using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using LogGrokX.Data;

namespace LogGrokX
{
    public enum StructuredTextKind
    {
        Json,
        Xml,
    }

    public static class TextOperations
    {
        private const string Ellipsis = "...";
        private const string XmlDeclarationStart = "<?xml";
        private const int MaxFailedXmlParseAttempts = 8;
        private const int MaxXmlUnclosedTagsLookup = 64;

        public static StringRange Normalize(StringRange stringRange, ViewSettings settings)
        {
            return stringRange.Length < settings.BigLineSize
                ? stringRange
                : StringRange.FromString(Normalize(stringRange.ToString(), settings));
        }

        public static string Normalize(string source, ViewSettings settings)
        {
            return (source.Length < settings.BigLineSize ? source : FormatLongString(source.TrimEnd('\0'), settings)).TrimEnd();
        }
        private static string FormatLongString(string source, ViewSettings settings)
        {
            var lines = 
                source.Split(Environment.NewLine);

            if (lines.Length == 1 && lines[0].Length <= settings.BigLineSize)
                return source;

            var sb = new StringBuilder();

            for (var idx = 0; idx < lines.Length; idx++)
            {
                var line = lines[idx];
                if (line.Length <= settings.BigLineSize)
                {
                    sb.Append(line);
                }
                else
                {
                    if(settings.BigLine == ViewSettings.ViewBigLine.Break)
                    {
                        sb.Append(BreakLongString(line, settings));
                    }
                    else
                    {
                        sb.Append(line, 0, settings.BigLineSize);
                        sb.Append(Ellipsis);
                    }
                }
                
                if (idx != lines.Length - 1)
                    sb.Append(Environment.NewLine);
            }

            return sb.ToString();
        }

        private static string BreakLongString(string source, ViewSettings settings)
        {
            var sb = new StringBuilder();
            for (var i = 0; i <= source.Length / settings.BigLineSize; ++i)
            {
                if (i != 0 )
                    sb.Append("\n");
                if(i * settings.BigLineSize + settings.BigLineSize < source.Length)
                    sb.Append(source.AsSpan(i * settings.BigLineSize, settings.BigLineSize));
                else
                    sb.Append(source.AsSpan(i * settings.BigLineSize));
            }
            return sb.ToString();
        }

        public static int CountLines(string source)
        {
            return CountLines(source.AsSpan());
        }

        public static (string resultString, int linesTrimmed) TrimLines(string source, uint maxLines)
        {
            var sourceSpan = source.AsSpan();
            var toTrimOffset = SkipLines(sourceSpan, maxLines);
            return (new string(sourceSpan[..toTrimOffset]), CountLines(sourceSpan[toTrimOffset..]));
        }

        public static IEnumerable<(int start, int length)> GetJsonRanges(string source)
        {
            return GetBracedGroups(source.AsSpan()).Where(interval => 
                IsValidJson(source.Substring(interval.start, interval.length)));
        }
        
        public static IEnumerable<(int start, int length)> GetBracedGroups(ReadOnlySpan<char> sourceText)
        {
            List<(int, int)>? result = null;
            var offset = 0;
            var text = sourceText;

            IEnumerable<(int start, int length)> MakeResult(List<(int, int)>? resultValue)
            {
                return resultValue ?? Enumerable.Empty<(int start, int length)>();
            }

            while (text.Length > 0)
            {
                var openBraceIndex = text.IndexOf('{') + offset;
                if (openBraceIndex == -1) return MakeResult(result);
                var nextBraceIndex = -1;
                var counter = 0;

                offset = openBraceIndex + 1;
                text = sourceText[offset..];
                
                while (counter >= 0)
                {
                    var localNextBraceIndex = text.IndexOfAny("{}");
                    if (localNextBraceIndex == -1) return MakeResult(result);
                    nextBraceIndex = localNextBraceIndex + offset;
                    if (sourceText[nextBraceIndex] == '{')
                        counter++;
                    else
                        counter--;

                    offset = nextBraceIndex + 1;
                    text = sourceText[offset..];
                }

                if (nextBraceIndex > 0)
                {
                    result ??= new List<(int, int)>(8);
                    result.Add((openBraceIndex, offset - openBraceIndex));
                }

                text = sourceText[offset..];
            }

            return MakeResult(result);
        }

        public static string FormatInlineJson(string source)
        {
            var jsonIntervals = GetJsonRanges(source).ToList();
            return jsonIntervals.Count == 0
                ? source
                : FormatInlineJson(source.AsSpan(), jsonIntervals.ToArray().AsSpan());
        }

        public static string FormatInlineJson(ReadOnlySpan<char> text,
            ReadOnlySpan<(int start, int length)> jsonIntervals)
        {
            return FormatInlineJsonCore(text, jsonIntervals, 0);
        }

        private static string FormatInlineJsonCore(ReadOnlySpan<char> text, ReadOnlySpan<(int start, int length)> jsonIntervals, 
            int startOffset, bool isFirstInterval = true)
        {
            if (jsonIntervals.Length == 0)
                return text.ToString();

            var firstStart = jsonIntervals[0].start - startOffset;
            var firstLength = jsonIntervals[0].length;
            
            StringBuilder stringBuilder = new();
            stringBuilder.Append(text[..firstStart]);
            
            if (!isFirstInterval)
                stringBuilder.Append(Environment.NewLine);

            stringBuilder.Append(FormatJsonText(text.Slice(firstStart, firstLength).ToString()));
            stringBuilder.Append(FormatInlineJsonCore(
                text[(firstStart + firstLength)..], 
                jsonIntervals[1..], firstStart + firstLength + startOffset, false));
            return stringBuilder.ToString();
        }

        public static List<(int start, int length, StructuredTextKind kind)> GetStructuredRanges(string source)
        {
            if (source.AsSpan().IndexOfAny('{', '<') < 0)
                return new List<(int start, int length, StructuredTextKind kind)>();

            var candidates = GetJsonRanges(source)
                .Select(r => (r.start, r.length, kind: StructuredTextKind.Json))
                .Concat(GetXmlRanges(source).Select(r => (r.start, r.length, kind: StructuredTextKind.Xml)))
                .OrderBy(r => r.start)
                .ThenByDescending(r => r.length);

            var result = new List<(int start, int length, StructuredTextKind kind)>();
            var end = 0;
            foreach (var candidate in candidates)
            {
                if (candidate.start < end)
                    continue;

                result.Add(candidate);
                end = candidate.start + candidate.length;
            }

            return result;
        }

        public static (string text, List<(int start, int length, StructuredTextKind kind)> ranges) FormatInlineStructured(
            string source, IReadOnlyList<(int start, int length, StructuredTextKind kind)> ranges)
        {
            var builder = new StringBuilder();
            var formattedRanges = new List<(int start, int length, StructuredTextKind kind)>(ranges.Count);
            var position = 0;
            for (var i = 0; i < ranges.Count; i++)
            {
                var (start, length, kind) = ranges[i];
                builder.Append(source, position, start - position);
                if (i != 0)
                    builder.Append(Environment.NewLine);

                var text = source.Substring(start, length);
                var formatted = kind == StructuredTextKind.Json ? FormatJsonText(text) : FormatXmlText(text);
                formattedRanges.Add((builder.Length, formatted.Length, kind));
                builder.Append(formatted);
                position = start + length;
            }

            builder.Append(source, position, source.Length - position);
            return (builder.ToString(), formattedRanges);
        }

        public static IEnumerable<(int start, int length)> GetXmlRanges(string source)
        {
            if (source.IndexOf('<') < 0)
                return Enumerable.Empty<(int start, int length)>();

            var declarations = new Dictionary<int, int>();
            var elements = ScanXmlElements(source.AsSpan(), declarations);
            if (elements.Count == 0)
                return Enumerable.Empty<(int start, int length)>();

            elements.Sort(static (x, y) => x.open != y.open ? x.open.CompareTo(y.open) : y.end.CompareTo(x.end));

            List<(int start, int length)>? result = null;
            var acceptedEnd = 0;
            var failedAttempts = 0;
            foreach (var (open, _, end, hasChildren) in elements)
            {
                if (!hasChildren || open < acceptedEnd)
                    continue;

                var start = declarations.TryGetValue(open, out var declarationStart) ? declarationStart : open;
                if (TryParseXml(source.Substring(start, end - start)) == null)
                {
                    if (++failedAttempts >= MaxFailedXmlParseAttempts)
                        break;
                    continue;
                }

                result ??= new List<(int start, int length)>(2);
                result.Add((start, end - start));
                acceptedEnd = end;
            }

            return result ?? Enumerable.Empty<(int start, int length)>();
        }

        public static List<(int open, int close)> GetXmlElementRanges(ReadOnlySpan<char> xml)
        {
            var elements = ScanXmlElements(xml, null);
            var result = new List<(int open, int close)>(elements.Count);
            foreach (var (open, close, _, _) in elements)
                result.Add((open, close));
            return result;
        }

        public static string FormatInlineXml(string source)
        {
            var ranges = GetXmlRanges(source)
                .Select(r => (r.start, r.length, StructuredTextKind.Xml))
                .ToList();
            return ranges.Count == 0 ? source : FormatInlineStructured(source, ranges).text;
        }

        private static List<(int open, int close, int end, bool hasChildren)> ScanXmlElements(
            ReadOnlySpan<char> source, Dictionary<int, int>? declarations)
        {
            var result = new List<(int open, int close, int end, bool hasChildren)>();
            var openTags = new List<(int open, int nameStart, int nameLength, bool hasChildren)>();
            var position = 0;
            while (position < source.Length)
            {
                var next = source[position..].IndexOf('<');
                if (next < 0)
                    break;

                position += next;
                var rest = source[position..];
                if (rest.StartsWith("<!--") || rest.StartsWith("<![CDATA[") || rest.StartsWith("<?"))
                {
                    var terminator = rest[1] == '?' ? "?>" : rest[2] == '-' ? "-->" : "]]>";
                    var skipped = SkipPast(source, position, terminator);
                    if (skipped < 0)
                        break;

                    if (declarations != null && rest.StartsWith(XmlDeclarationStart, StringComparison.Ordinal))
                    {
                        var rootStart = skipped;
                        while (rootStart < source.Length && char.IsWhiteSpace(source[rootStart]))
                            rootStart++;
                        declarations[rootStart] = position;
                    }

                    position = skipped;
                    continue;
                }

                var isEndTag = rest.StartsWith("</");
                if (!isEndTag && !IsXmlStartTag(source, position))
                {
                    position++;
                    continue;
                }

                var tagEnd = FindXmlTagEnd(source, position);
                if (tagEnd < 0)
                {
                    position++;
                    continue;
                }

                var nameStart = position + (isEndTag ? 2 : 1);
                var nameLength = GetXmlNameLength(source, nameStart);
                if (isEndTag)
                {
                    var index = FindOpenTag(source, openTags, source.Slice(nameStart, nameLength));
                    if (index >= 0)
                    {
                        var (open, _, _, hasChildren) = openTags[index];
                        openTags.RemoveRange(index, openTags.Count - index);
                        result.Add((open, position, tagEnd + 1, hasChildren));
                    }
                }
                else
                {
                    if (openTags.Count != 0)
                        openTags[^1] = openTags[^1] with { hasChildren = true };

                    if (source[tagEnd - 1] != '/')
                        openTags.Add((position, nameStart, nameLength, false));
                }

                position = tagEnd + 1;
            }

            return result;
        }

        private static int FindOpenTag(ReadOnlySpan<char> source,
            List<(int open, int nameStart, int nameLength, bool hasChildren)> openTags, ReadOnlySpan<char> name)
        {
            var lowest = Math.Max(0, openTags.Count - MaxXmlUnclosedTagsLookup);
            for (var i = openTags.Count - 1; i >= lowest; i--)
            {
                var (_, nameStart, nameLength, _) = openTags[i];
                if (source.Slice(nameStart, nameLength).SequenceEqual(name))
                    return i;
            }

            return -1;
        }

        private static int GetXmlNameLength(ReadOnlySpan<char> source, int nameStart)
        {
            var length = 0;
            while (nameStart + length < source.Length)
            {
                var ch = source[nameStart + length];
                if (char.IsWhiteSpace(ch) || ch is '/' or '>')
                    break;
                length++;
            }

            return length;
        }

        private static bool IsXmlStartTag(ReadOnlySpan<char> source, int position)
        {
            return position + 1 < source.Length
                   && source[position] == '<'
                   && (char.IsLetter(source[position + 1]) || source[position + 1] == '_');
        }

        private static int SkipPast(ReadOnlySpan<char> source, int position, string terminator)
        {
            var index = source[position..].IndexOf(terminator);
            return index < 0 ? -1 : position + index + terminator.Length;
        }

        private static int FindXmlTagEnd(ReadOnlySpan<char> source, int position)
        {
            var quote = '\0';
            for (var i = position + 1; i < source.Length; i++)
            {
                var ch = source[i];
                if (ch == '<')
                    return -1;

                if (quote != '\0')
                {
                    if (ch == quote)
                        quote = '\0';
                }
                else if (ch is '"' or '\'')
                {
                    quote = ch;
                }
                else if (ch == '>')
                {
                    return i;
                }
            }

            return -1;
        }

        private static XDocument? TryParseXml(string xml)
        {
            try
            {
                using var reader = XmlReader.Create(new StringReader(xml),
                    new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
                var document = XDocument.Load(reader);
                return document.Root is { HasElements: true } ? document : null;
            }
            catch (XmlException)
            {
                return null;
            }
        }

        private static string FormatXmlText(string xml)
        {
            var document = TryParseXml(xml) ?? throw new InvalidOperationException();
            var parts = new List<string>();
            if (document.Declaration != null)
                parts.Add(document.Declaration.ToString());
            parts.AddRange(document.Nodes().Select(node => node.ToString()));
            return string.Join(Environment.NewLine, parts);
        }

        private static bool IsValidJson(string jsonString)
        {
            // prevent some exceptions
            // correct json starts with
            // { <whitespace> }
            // or
            // { <whitespace> "
            var openBraceIndex = jsonString.IndexOf('{');
            if (openBraceIndex < 0) return false;
            var startSpan = jsonString.AsSpan(openBraceIndex+1);
            var nextValidJsonChar = startSpan.IndexOfAny("\"}");
            if (nextValidJsonChar < 0) return false;
                
            foreach (var ch in startSpan[..nextValidJsonChar])
            {
                if (!char.IsWhiteSpace(ch))
                    return false;
            }
            
            try
            {
                using var doc = JsonDocument.Parse(jsonString, new JsonDocumentOptions { AllowTrailingCommas = true });
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }
        private static string FormatJsonText(string jsonString)
        {
            using var doc = JsonDocument.Parse(jsonString, new JsonDocumentOptions { AllowTrailingCommas = true });
            var memoryStream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(memoryStream, 
                       new JsonWriterOptions { Indented = true, 
                                               Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
            {
                doc.WriteTo(writer);
            }
            return new UTF8Encoding()
                .GetString(memoryStream.ToArray());
        }
        
        private static int SkipLines(ReadOnlySpan<char> source, uint linesToSkip)
        {
            if (linesToSkip == 0) return 0;
            var offset = 0;
            var span = source;
            while (true)
            {
                var indexOfLf = span.IndexOf('\n');
                if (indexOfLf == -1)
                    return offset + span.Length;
                
                offset += indexOfLf + 1;
                if (linesToSkip == 1) return offset;
                span = source[offset..];
                linesToSkip -= 1;
            }
        }

        private static int CountLines(ReadOnlySpan<char> source)
        {
            if (source.IsEmpty) return 0;
            var indexof = source.IndexOf('\n');
            return indexof > 0 ? 1 + CountLines(source[(indexof + 1)..]) : 1;
        }
    }
}