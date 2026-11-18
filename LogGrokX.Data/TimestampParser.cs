using System;
using System.Globalization;

namespace LogGrokX.Data
{
    public static class TimestampParser
    {
        private const string DateHourFormat = "yyyy-MM-dd HH:mm:ss.fff";
        private const string DateTimeShortFormat = "yyyy-MM-dd HH:mm:ss";
        private const string TimeOnlyFormat = "HH:mm:ss.fff";
        private const string TimeOnlyShortFormat = "HH:mm:ss";
        private const int DateTimeLength = 19;

        public static bool TryGetTicks(ReadOnlySpan<char> text, string? format, out long ticks)
        {
            ticks = -1;
            if (text.IsEmpty)
                return false;

            var hasFormat = !string.IsNullOrWhiteSpace(format);
            if (hasFormat
                ? TryParseFast(text, format!, out ticks)
                : TryParseDate(text, text.Length == DateTimeLength ? 0 : 3, out ticks))
                return true;

            DateTime timestamp;
            var parsed = hasFormat
                ? DateTime.TryParseExact(text, format!, CultureInfo.InvariantCulture, DateTimeStyles.None,
                    out timestamp)
                : DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out timestamp);

            if (!parsed)
                return false;

            ticks = hasFormat && IsTimeOnly(format!)
                ? timestamp.TimeOfDay.Ticks
                : timestamp.Ticks;

            return true;
        }

        private static bool TryParseFast(ReadOnlySpan<char> text, string format, out long ticks)
        {
            ticks = -1;

            switch (format)
            {
                case DateHourFormat:
                    return TryParseDate(text, 3, out ticks);
                case DateTimeShortFormat:
                    return TryParseDate(text, 0, out ticks);
                case TimeOnlyFormat:
                    return TryParseTime(text, 3, out ticks);
                case TimeOnlyShortFormat:
                    return TryParseTime(text, 0, out ticks);
                default:
                    return false;
            }
        }

        private static bool TryParseDate(ReadOnlySpan<char> text, int fractionDigits, out long ticks)
        {
            ticks = -1;
            var length = DateTimeLength + (fractionDigits > 0 ? 1 + fractionDigits : 0);
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

        private static bool IsTimeOnly(string format) =>
            format.IndexOf('y') < 0 && format.IndexOf('M') < 0 && format.IndexOf('d') < 0;
    }
}
