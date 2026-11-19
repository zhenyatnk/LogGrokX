using System;
using System.Linq;
using System.Text.RegularExpressions;
using LogGrokX.Data.Monikers;

namespace LogGrokX.Data
{
    public class RegexBasedLineParser : ILineParser
    {
        private const string ClassicLineRegex =
            @"^(?<Time>\d{4}-\d{2}-\d{2}\s[^\s]+)\s+(?<Level>[^\s]+)\s+(?<Thread>[^\s]+)\s+(?<Component>[^\s]+)\s+(?<Message>.*)";

        private readonly Regex _regex;
        private readonly Regex? _firstLineRegex;
        private readonly bool _isClassic;
        private readonly int _componentCount;
        private readonly int[] _fieldsToStore;
        private readonly int _timeGroupNumber;
        private readonly string _timeFormat;

        public RegexBasedLineParser(LogMetaInformation logMetaInformation, 
            bool onlyIndexed = false)
        {
            var lineRegex = logMetaInformation.LineRegex;
            _isClassic = lineRegex == ClassicLineRegex;
            _regex = new Regex(lineRegex,
                onlyIndexed ? RegexOptions.Compiled : RegexOptions.Compiled | RegexOptions.Singleline);
            if (!onlyIndexed)
                _firstLineRegex = new Regex(lineRegex, RegexOptions.Compiled);
            _componentCount = logMetaInformation.ComponentCount;
            _fieldsToStore = onlyIndexed
                ? logMetaInformation.IndexedFieldNumbers
                : Enumerable.Range(0, _componentCount).ToArray();
            _timeGroupNumber = logMetaInformation.HasTime ? logMetaInformation.TimeGroupNumber : -1;
            _timeFormat = logMetaInformation.TimeFormat;
        }

        public ParseResult Parse(string input)
        {
            var placeholder = new int[LineMetaInformation.GetSizeInts(_componentCount)];
            if (!TryParse(input, 0, input.Length,
                new LineMetaInformation(placeholder.AsSpan(), _componentCount).ParsedLineComponents,
                out _))
                throw new InvalidOperationException();
            return new ParseResult(_componentCount, placeholder);
        }

        public bool TryParse(string input, int beginning, int length,
            in ParsedLineComponents parsedLineComponents, out long timeTicks)
        {
            if (_isClassic && TryParseClassic(input.AsSpan(beginning, length), parsedLineComponents, out timeTicks))
                return true;

            timeTicks = -1;

            var match = MatchRecord(input, beginning, length, out var hasContinuation);
            if (match == null)
                return false;

            var index = 0;
            
            var lastComponentStart = 0;
            var lastComponentLength = 0;

            var caps = MatchSurgery.GetCaptures(match);
            var matchCounts = MatchSurgery.GetMatchCounts(match);
            foreach (var fieldToStore in _fieldsToStore)
            {
                var cap = caps[fieldToStore + 1];
                var matchCount = matchCounts[fieldToStore + 1];
                if (cap != null && matchCount > 0)
                {
                    var componentStartIndex = cap[0];
                    var componentLength =  cap[1];

                    lastComponentStart = componentStartIndex - beginning;
                    lastComponentLength = componentLength;
                }
                else
                {
                    lastComponentStart += lastComponentLength;
                    lastComponentLength = 0;
                }

                parsedLineComponents.ComponentStart(index) = lastComponentStart;
                parsedLineComponents.ComponentLength(index) = lastComponentLength;
                index++;
            }

            if (hasContinuation)
                ExtendLastComponent(parsedLineComponents, match.Index + match.Length - beginning, length);

            if (_timeGroupNumber > 0 && _timeGroupNumber < matchCounts.Length &&
                matchCounts[_timeGroupNumber] > 0)
            {
                var timeCapture = caps[_timeGroupNumber];
                if (timeCapture != null &&
                    TimestampParser.TryGetTicks(input.AsSpan(timeCapture[0], timeCapture[1]),
                        _timeFormat, out var ticks))
                {
                    timeTicks = ticks;
                }
            }

            return true;
        }

        private bool TryParseClassic(ReadOnlySpan<char> record, in ParsedLineComponents parsedLineComponents,
            out long timeTicks)
        {
            timeTicks = -1;
            if (record.Length < 12 || record[4] != '-' || record[7] != '-' ||
                !char.IsWhiteSpace(record[10]) || char.IsWhiteSpace(record[11]) ||
                record[..4].ContainsAnyExceptInRange('0', '9') ||
                record.Slice(5, 2).ContainsAnyExceptInRange('0', '9') ||
                record.Slice(8, 2).ContainsAnyExceptInRange('0', '9'))
                return false;

            if (_firstLineRegex != null)
            {
                var firstLineEnd = record.IndexOfAny('\r', '\n');
                if (firstLineEnd >= 0 && !record[firstLineEnd..].TrimEnd("\r\n").IsEmpty)
                    return false;
            }

            Span<(int Start, int Length)> fields = stackalloc (int, int)[5];
            var position = FindWhiteSpace(record, 12);
            fields[0] = (0, position);
            for (var field = 1; field <= 3; field++)
            {
                var start = SkipWhiteSpace(record, position);
                position = FindWhiteSpace(record, start);
                if (position == start)
                    return false;

                fields[field] = (start, position - start);
            }

            if (position == record.Length)
                return false;

            var messageStart = SkipWhiteSpace(record, position);
            var message = record[messageStart..];
            var lineFeed = _firstLineRegex == null ? message.IndexOf('\n') : -1;
            fields[4] = (messageStart, lineFeed < 0 ? message.Length : lineFeed);

            for (var index = 0; index < _fieldsToStore.Length; index++)
            {
                var field = fields[_fieldsToStore[index]];
                parsedLineComponents.ComponentStart(index) = field.Start;
                parsedLineComponents.ComponentLength(index) = field.Length;
            }

            if (_timeGroupNumber > 0)
            {
                var time = fields[_timeGroupNumber - 1];
                TimestampParser.TryGetTicks(record.Slice(time.Start, time.Length), _timeFormat, out timeTicks);
            }

            return true;
        }

        private static int SkipWhiteSpace(ReadOnlySpan<char> text, int position)
        {
            while (position < text.Length && char.IsWhiteSpace(text[position]))
                position++;
            return position;
        }

        private static int FindWhiteSpace(ReadOnlySpan<char> text, int position)
        {
            while (position < text.Length && !char.IsWhiteSpace(text[position]))
                position++;
            return position;
        }

        private Match? MatchRecord(string input, int beginning, int length, out bool hasContinuation)
        {
            hasContinuation = false;
            if (_firstLineRegex != null)
            {
                var record = input.AsSpan(beginning, length);
                var firstLineLength = record.IndexOfAny('\r', '\n');
                if (firstLineLength >= 0 && record[firstLineLength..].TrimEnd("\r\n").Length > 0)
                {
                    var firstLineMatch = _firstLineRegex.Match(input, beginning, firstLineLength);
                    if (firstLineMatch.Success)
                    {
                        hasContinuation = true;
                        return firstLineMatch;
                    }
                }
            }

            var match = _regex.Match(input, beginning, length);
            return match.Success ? match : null;
        }

        private void ExtendLastComponent(in ParsedLineComponents components, int matchEnd, int recordEnd)
        {
            for (var i = _fieldsToStore.Length - 1; i >= 0; i--)
            {
                var start = components.ComponentStart(i);
                if (start + components.ComponentLength(i) != matchEnd)
                    continue;

                components.ComponentLength(i) = recordEnd - start;
                return;
            }
        }
    }
}
