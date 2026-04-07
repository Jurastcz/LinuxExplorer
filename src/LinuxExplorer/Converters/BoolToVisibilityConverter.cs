using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LinuxExplorer.Converters;

/// <summary>
/// Converts a <see cref="bool"/> to a <see cref="Visibility"/> value.
/// <c>true</c> → <see cref="Visibility.Visible"/>, <c>false</c> → <see cref="Visibility.Collapsed"/>.
/// </summary>
[ValueConversion(typeof(bool), typeof(Visibility))]
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool flag = value is bool b && b;
        // Support optional inversion via ConverterParameter="Invert"
        if (parameter is string s && s.Equals("Invert", StringComparison.OrdinalIgnoreCase))
            flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool visible = value is Visibility v && v == Visibility.Visible;
        if (parameter is string s && s.Equals("Invert", StringComparison.OrdinalIgnoreCase))
            visible = !visible;
        return visible;
    }
}
