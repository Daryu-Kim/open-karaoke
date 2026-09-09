namespace OpenKaraoke.Core.Formatting;

/// <summary>
/// Formats playback time for display (e.g. "3:05", "1:02:30").
/// Shared by the player UI so the formatting logic is unit-testable.
/// </summary>
public static class MediaTimeFormatter
{
    public static string Format(TimeSpan time)
    {
        if (time < TimeSpan.Zero)
        {
            time = TimeSpan.Zero;
        }

        long totalSeconds = (long)time.TotalSeconds;
        long hours = totalSeconds / 3600;
        long minutes = (totalSeconds % 3600) / 60;
        long seconds = totalSeconds % 60;

        return hours > 0
            ? $"{hours}:{minutes:00}:{seconds:00}"
            : $"{minutes:00}:{seconds:00}";
    }

    public static string Format(double totalSeconds) => Format(TimeSpan.FromSeconds(totalSeconds));
}
