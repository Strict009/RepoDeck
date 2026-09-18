using System.Globalization;

namespace RepoDeck.Infrastructure;

/// <summary>Formatting helpers for numbers, dates and sizes shown to non-technical users.</summary>
public static class Humanize
{
    /// <summary>12345 -> "12.3k", 1500000 -> "1.5M".</summary>
    public static string Count(int value)
    {
        if (value < 0) return "0";
        if (value < 1_000) return value.ToString(CultureInfo.InvariantCulture);

        if (value < 1_000_000)
        {
            var thousands = value / 1_000d;
            return thousands < 10
                ? thousands.ToString("0.0", CultureInfo.InvariantCulture) + "k"
                : Math.Floor(thousands).ToString(CultureInfo.InvariantCulture) + "k";
        }

        var millions = value / 1_000_000d;
        return millions.ToString("0.0", CultureInfo.InvariantCulture) + "M";
    }

    /// <summary>"3 days ago", "last year". Returns "unknown" for null.</summary>
    public static string RelativeTime(DateTimeOffset? value, DateTimeOffset? now = null)
    {
        if (value is null) return "unknown";

        var reference = now ?? DateTimeOffset.UtcNow;
        var delta = reference - value.Value;

        if (delta < TimeSpan.Zero) return "just now";
        if (delta.TotalSeconds < 60) return "just now";
        if (delta.TotalMinutes < 60) return Plural((int)delta.TotalMinutes, "minute") + " ago";
        if (delta.TotalHours < 24) return Plural((int)delta.TotalHours, "hour") + " ago";
        if (delta.TotalDays < 30) return Plural((int)delta.TotalDays, "day") + " ago";

        var months = (int)(delta.TotalDays / 30.44);
        if (months < 12) return Plural(Math.Max(months, 1), "month") + " ago";

        var years = (int)(delta.TotalDays / 365.25);
        return Plural(Math.Max(years, 1), "year") + " ago";
    }

    /// <summary>How long until a moment in the future, e.g. "in 43 minutes".</summary>
    /// <remarks>
    /// <see cref="RelativeTime"/> only ever renders the past, so borrowing it for a
    /// GitHub rate-limit reset produced sentences like "Resets 43 minutes ago" about
    /// something that had not happened yet.
    /// </remarks>
    public static string TimeUntil(DateTimeOffset? value, DateTimeOffset? now = null)
    {
        if (value is null) return "unknown";

        var delta = value.Value - (now ?? DateTimeOffset.UtcNow);

        if (delta <= TimeSpan.Zero) return "shortly";
        if (delta.TotalSeconds < 60) return "in less than a minute";
        if (delta.TotalMinutes < 60) return "in " + Plural((int)delta.TotalMinutes, "minute");
        if (delta.TotalHours < 24) return "in " + Plural((int)delta.TotalHours, "hour");

        return "in " + Plural(Math.Max((int)delta.TotalDays, 1), "day");
    }

    /// <summary>Bytes to a short human size, e.g. "4.2 MB".</summary>
    public static string FileSize(long bytes)
    {
        if (bytes < 0) return "unknown";
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        var format = unit == 0 ? "0" : size < 10 ? "0.0" : "0";
        return size.ToString(format, CultureInfo.InvariantCulture) + " " + units[unit];
    }

    /// <summary>
    /// Bytes with one decimal place, for a counter that is moving while somebody watches.
    /// </summary>
    /// <remarks>
    /// <see cref="FileSize"/> drops decimals above ten, which is right for "57 MB" on a
    /// confirmation and wrong for a download readout: it would sit on "18 MB" for several
    /// seconds and then jump, which reads as a stall rather than as progress.
    /// </remarks>
    public static string FileSizePrecise(long bytes)
    {
        if (bytes < 0) return "unknown";

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        var unit = 0;

        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        var format = unit == 0 ? "0" : "0.0";
        return size.ToString(format, CultureInfo.InvariantCulture) + " " + units[unit];
    }

    public static string Percent(double share) =>
        (share * 100).ToString(share >= 0.1 ? "0" : "0.0", CultureInfo.InvariantCulture) + "%";

    private static string Plural(int count, string noun) =>
        count == 1 ? $"1 {noun}" : $"{count} {noun}s";
}
