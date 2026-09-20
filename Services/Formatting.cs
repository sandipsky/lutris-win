using System;

namespace Lutris.Services;

public static class Formatting
{
    public static string Duration(long milliseconds)
    {
        if (milliseconds <= 0) return "—";
        var span = TimeSpan.FromMilliseconds(milliseconds);
        if (span.TotalHours >= 1) return $"{(int)span.TotalHours}h {span.Minutes}m";
        if (span.TotalMinutes >= 1) return $"{span.Minutes}m {span.Seconds}s";
        return $"{span.Seconds}s";
    }

    /// <summary>"Just now", "3 hours ago", "Yesterday", or a short date for older values.</summary>
    public static string RelativeTime(long? unixMilliseconds)
    {
        if (unixMilliseconds is not long value) return "Never";

        var when = DateTimeOffset.FromUnixTimeMilliseconds(value).ToLocalTime();
        var now = DateTimeOffset.Now;
        var elapsed = now - when;

        if (elapsed < TimeSpan.FromMinutes(1)) return "Just now";
        if (elapsed < TimeSpan.FromHours(1))
        {
            var minutes = (int)elapsed.TotalMinutes;
            return minutes == 1 ? "1 minute ago" : $"{minutes} minutes ago";
        }
        if (elapsed < TimeSpan.FromHours(24) && when.Date == now.Date)
        {
            var hours = (int)elapsed.TotalHours;
            return hours == 1 ? "1 hour ago" : $"{hours} hours ago";
        }
        if (when.Date == now.Date.AddDays(-1)) return "Yesterday";
        if (elapsed < TimeSpan.FromDays(7)) return $"{(int)elapsed.TotalDays} days ago";
        return when.Year == now.Year ? when.ToString("MMM d") : when.ToString("MMM d, yyyy");
    }

    public static string AbsoluteDate(long? unixMilliseconds)
    {
        if (unixMilliseconds is not long value) return "—";
        var when = DateTimeOffset.FromUnixTimeMilliseconds(value).ToLocalTime();
        return $"{when:MMM d, yyyy} at {when:t}";
    }
}
