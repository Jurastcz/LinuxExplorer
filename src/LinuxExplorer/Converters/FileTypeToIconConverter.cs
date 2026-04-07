using System.Globalization;
using System.Windows.Data;
using LinuxExplorer.ExtFileSystem.Ext;

namespace LinuxExplorer.Converters;

/// <summary>Converts a <see cref="FileType"/> value to a Segoe MDL2 Assets icon character.</summary>
[ValueConversion(typeof(FileType), typeof(string))]
public sealed class FileTypeToIconConverter : IValueConverter
{
    /// <inheritdoc/>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not FileType ft) return "\uE9CE"; // unknown

        return ft switch
        {
            FileType.Directory => "\uE8B7",    // Folder
            FileType.RegularFile => "\uE7C3",  // Document
            FileType.Symlink => "\uE71B",      // Link
            FileType.BlockDevice => "\uE9A0",  // HDD
            FileType.CharDevice => "\uE9A0",   // HDD
            FileType.Fifo => "\uE8A0",         // Pipe-like
            FileType.Socket => "\uEDA3",       // Network
            _ => "\uE9CE"
        };
    }

    /// <inheritdoc/>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
