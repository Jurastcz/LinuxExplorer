namespace LinuxExplorer.Helpers;

/// <summary>
/// Shared formatting helpers for file system display.
/// </summary>
public static class FileSystemHelper
{
    private const long KB = 1024;
    private const long MB = KB * 1024;
    private const long GB = MB * 1024;
    private const long TB = GB * 1024;

    /// <summary>Converts a byte count to a human-readable size string.</summary>
    public static string FormatSize(long bytes) => bytes switch
    {
        < KB => $"{bytes} B",
        < MB => $"{bytes / (double)KB:F1} KB",
        < GB => $"{bytes / (double)MB:F1} MB",
        < TB => $"{bytes / (double)GB:F1} GB",
        _ => $"{bytes / (double)TB:F1} TB"
    };
}
