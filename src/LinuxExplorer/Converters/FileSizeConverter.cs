using System.Globalization;
using System.Windows.Data;

namespace LinuxExplorer.Converters;

/// <summary>
/// Converts a <see cref="long"/> byte count to a human-readable size string.
/// </summary>
[ValueConversion(typeof(long), typeof(string))]
public class FileSizeConverter : IValueConverter
{
    private const long KB = 1024;
    private const long MB = KB * 1024;
    private const long GB = MB * 1024;
    private const long TB = GB * 1024;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not long bytes) return string.Empty;

        return bytes switch
        {
            < KB => $"{bytes} B",
            < MB => $"{bytes / (double)KB:F1} KB",
            < GB => $"{bytes / (double)MB:F1} MB",
            < TB => $"{bytes / (double)GB:F1} GB",
            _ => $"{bytes / (double)TB:F1} TB"
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
