using System;
using System.Globalization;
using LogGrokX.Localization;

namespace LogGrokX;

internal static class DurationFormatter
{
    public static string Format(long ticks)
    {
        if (ticks <= 0)
            return Strings.Format("DurationMilliseconds", 0);

        if (ticks < TimeSpan.TicksPerSecond)
        {
            var milliseconds = ticks / TimeSpan.TicksPerMillisecond;
            return Strings.Format("DurationMilliseconds", milliseconds);
        }

        if (ticks < TimeSpan.TicksPerMinute)
        {
            var seconds = (double)ticks / TimeSpan.TicksPerSecond;
            return Strings.Format("DurationSeconds", seconds.ToString("0.#", CultureInfo.CurrentUICulture));
        }

        if (ticks < TimeSpan.TicksPerHour)
        {
            var seconds = ticks / TimeSpan.TicksPerSecond;
            var minutes = seconds / 60;
            var remainder = seconds % 60;
            return WithRemainder("DurationMinutes", minutes, "DurationSeconds", remainder);
        }

        if (ticks < TimeSpan.TicksPerDay)
        {
            var minutes = ticks / TimeSpan.TicksPerMinute;
            var hours = minutes / 60;
            var remainder = minutes % 60;
            return WithRemainder("DurationHours", hours, "DurationMinutes", remainder);
        }

        var totalHours = ticks / TimeSpan.TicksPerHour;
        var days = totalHours / 24;
        var restHours = totalHours % 24;
        return WithRemainder("DurationDays", days, "DurationHours", restHours);
    }

    private static string WithRemainder(string unit, long value, string remainderUnit, long remainder) =>
        remainder == 0
            ? Strings.Format(unit, value)
            : Strings.Format(unit, value) + " " + Strings.Format(remainderUnit, remainder);
}
