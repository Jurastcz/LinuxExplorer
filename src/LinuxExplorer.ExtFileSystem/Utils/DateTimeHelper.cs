namespace LinuxExplorer.ExtFileSystem.Utils;

/// <summary>
/// Helper methods for converting ext filesystem Unix timestamps.
/// </summary>
public static class DateTimeHelper
{
    private static readonly DateTimeOffset UnixEpoch = DateTimeOffset.UnixEpoch;

    /// <summary>
    /// Converts a 32-bit Unix timestamp to a <see cref="DateTimeOffset"/>.
    /// </summary>
    /// <param name="unixTimestamp">Seconds since the Unix epoch (January 1, 1970 UTC).</param>
    /// <returns>The corresponding <see cref="DateTimeOffset"/> in UTC.</returns>
    public static DateTimeOffset FromUnixTimestamp(uint unixTimestamp) =>
        UnixEpoch.AddSeconds(unixTimestamp);

    /// <summary>
    /// Converts a <see cref="DateTimeOffset"/> to a 32-bit Unix timestamp.
    /// </summary>
    /// <param name="dt">The date/time to convert.</param>
    /// <returns>Seconds since the Unix epoch, clamped to uint range.</returns>
    public static uint ToUnixTimestamp(DateTimeOffset dt)
    {
        long seconds = dt.ToUnixTimeSeconds();
        return seconds < 0 ? 0 : seconds > uint.MaxValue ? uint.MaxValue : (uint)seconds;
    }

    /// <summary>
    /// Formats a Unix timestamp as a human-readable local time string.
    /// </summary>
    /// <param name="unixTimestamp">Seconds since the Unix epoch.</param>
    /// <returns>A formatted date/time string.</returns>
    public static string Format(uint unixTimestamp)
    {
        if (unixTimestamp == 0) return "-";
        var dt = FromUnixTimestamp(unixTimestamp).ToLocalTime();
        return dt.ToString("yyyy-MM-dd HH:mm:ss");
    }
}
