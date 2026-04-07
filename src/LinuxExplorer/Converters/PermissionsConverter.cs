using System.Globalization;
using System.Windows.Data;
using LinuxExplorer.ExtFileSystem.Utils;

namespace LinuxExplorer.Converters;

/// <summary>Converts a Unix mode (ushort) to a permission string like <c>-rw-r--r--</c>.</summary>
[ValueConversion(typeof(ushort), typeof(string))]
public sealed class PermissionsConverter : IValueConverter
{
    /// <inheritdoc/>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ushort mode)
            return PermissionHelper.ToPermissionString(mode);
        return string.Empty;
    }

    /// <inheritdoc/>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
