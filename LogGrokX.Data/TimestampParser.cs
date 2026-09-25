using System;
using System.Globalization;

namespace LogGrokX.Data
{
    /// <summary>
    /// Pre-analyzed timestamp format. Parsing the format string is done once
    /// (usually in a parser constructor) instead of once per log line.
    /// </summary>
    public sealed class TimestampFormat
    {
        internal enum FastPath
        {
            None,
            DateTimeMillis,
            DateTimeSeconds,
            TimeMillis,
            TimeSeconds
        }

        public static readonly TimestampFormat Empty = new(null);

        private TimestampFormat(string? format)
        {
            Raw = format;
            HasFormat = !string.IsNullOrWhiteSpace(format);
            if (!HasFormat)
            {
                Path = FastPath.None;
                IsTimeOnly = false;
                return;
            }

            Path = format switch
            {
                TimestampParser.DateHourFormat => FastPath.DateTimeMillis,
                TimestampParser.DateTimeShortFormat => FastPath.DateTimeSeconds,
                TimestampParser.TimeOnlyFormat => FastPath.TimeMillis,
                TimestampParser.TimeOnlyShortFormat => FastPath.TimeSeconds,
                _ => FastPath.None
            };

            IsTimeOnly = format!.IndexOf('y') < 0 && format.IndexOf('M') < 0 && format.IndexOf('d') < 0;
        }

        public static TimestampFormat Create(string? format) =>
            string.IsNullOrWhiteSpace(format) ? Empty : new TimestampFormat(format);

        internal string? Raw { get; }

        internal bool HasFormat { get; }

        internal FastPath Path { get; }

        internal bool IsTimeOnly { get; }
    }

    public static class TimestampParser
    {
        internal const string DateHourFormat = "yyyy-MM-dd HH:mm:ss.fff";
        internal const string DateTimeShortFormat = "yyyy-MM-dd HH:mm:ss";
        internal const string TimeOnlyFormat = "HH:mm:ss.fff";
        internal const string TimeOnlyShortFormat = "HH:mm:ss";

        public static bool TryGetTicks(ReadOnlySpan<char> text, string? format, out long ticks) =>
            TryGetTicks(text, TimestampFormat.Create(format), out ticks);

        public static bool TryGetTicks(ReadOnlySpan<char> text, TimestampFormat format, out long ticks)
        {
            ticks = -1;
            if (text.IsEmpty)
                return false;

            if (TryParseFast(text, format.Path, out ticks))
                return true;

            DateTime timestamp;
            var parsed = format.HasFormat
                ? DateTime.TryParseExact(text, format.Raw!, CultureInfo.InvariantCulture, DateTimeStyles.None,
                    out timestamp)
                : DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out timestamp);

            if (!parsed)
                return false;

            ticks = format.HasFormat && format.IsTimeOnly
                ? timestamp.TimeOfDay.Ticks
                : timestamp.Ticks;

            return true;
        }

        private static bool TryParseFast(ReadOnlySpan<char> text, TimestampFormat.FastPath path, out long ticks)
        {
            ticks = -1;

            switch (path)
            {
                case TimestampFormat.FastPath.DateTimeMillis:
                    return TryParseDate(text, 3, out ticks);
                case TimestampFormat.FastPath.DateTimeSeconds:
                    return TryParseDate(text, 0, out ticks);
                case TimestampFormat.FastPath.TimeMillis:
                    return TryParseTime(text, 3, out ticks);
                case TimestampFormat.FastPath.TimeSeconds:
                    return TryParseTime(text, 0, out ticks);
                default:
                    return false;
            }
        }

        private static bool TryParseDate(ReadOnlySpan<char> text, int fractionDigits, out long ticks)
        {
            ticks = -1;
            var length = 19 + (fractionDigits > 0 ? 1 + fractionDigits : 0);
            if (text.Length != length)
                return false;

            if (text[4] != '-' || text[7] != '-' || text[10] != ' ' ||
                text[13] != ':' || text[16] != ':')
                return false;

            if (fractionDigits > 0 && text[19] != '.')
                return false;

            if (!TryReadDigits(text, 0, 4, out var year) ||
                !TryReadDigits(text, 5, 2, out var month) ||
                !TryReadDigits(text, 8, 2, out var day) ||
                !TryReadDigits(text, 11, 2, out var hour) ||
                !TryReadDigits(text, 14, 2, out var minute) ||
                !TryReadDigits(text, 17, 2, out var second))
                return false;

            var millisecond = 0;
            if (fractionDigits > 0)
            {
                if (!TryReadDigits(text, 20, 3, out millisecond))
                    return false;
            }

            if (year < 1 || year > 9999 || month < 1 || month > 12 ||
                day < 1 || day > DateTime.DaysInMonth(year, month) ||
                hour > 23 || minute > 59 || second > 59)
                return false;

            ticks = new DateTime(year, month, day, hour, minute, second, millisecond,
                DateTimeKind.Unspecified).Ticks;
            return true;
        }

        private static bool TryParseTime(ReadOnlySpan<char> text, int fractionDigits, out long ticks)
        {
            ticks = -1;
            var length = 8 + (fractionDigits > 0 ? 1 + fractionDigits : 0);
            if (text.Length != length)
                return false;

            if (text[2] != ':' || text[5] != ':')
                return false;

            if (fractionDigits > 0 && text[8] != '.')
                return false;

            if (!TryReadDigits(text, 0, 2, out var hour) ||
                !TryReadDigits(text, 3, 2, out var minute) ||
                !TryReadDigits(text, 6, 2, out var second))
                return false;

            var millisecond = 0;
            if (fractionDigits > 0)
            {
                if (!TryReadDigits(text, 9, 3, out millisecond))
                    return false;
            }

            if (hour > 23 || minute > 59 || second > 59)
                return false;

            ticks = hour * TimeSpan.TicksPerHour +
                    minute * TimeSpan.TicksPerMinute +
                    second * TimeSpan.TicksPerSecond +
                    millisecond * TimeSpan.TicksPerMillisecond;
            return true;
        }

        private static bool TryReadDigits(ReadOnlySpan<char> text, int start, int count, out int value)
        {
            value = 0;
            for (var i = 0; i < count; i++)
            {
                var digit = text[start + i] - '0';
                if ((uint) digit > 9)
                    return false;
                value = value * 10 + digit;
            }

            return true;
        }

        public static string Format(long ticks)
        {
            if (ticks < 0)
                return string.Empty;

            var timestamp = new DateTime(ticks);
            return timestamp.Year <= 1
                ? timestamp.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)
                : timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        }

    }
}
