using System.Globalization;
using System.Windows.Data;
using LinuxExplorer.Helpers;

namespace LinuxExplorer.Converters;

/// <summary>
/// Converts a <see cref="long"/> byte count to a human-readable size string.
/// </summary>
[ValueConversion(typeof(long), typeof(string))]
public class FileSizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not long bytes) return string.Empty;
        return FileSystemHelper.FormatSize(bytes);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}