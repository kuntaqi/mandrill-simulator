using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MandrillSimulator.Converters;

public class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var count = value switch
        {
            int n => n,
            ICollection collection => collection.Count,
            _ => 0
        };

        var invert = string.Equals(parameter as string, "invert", StringComparison.OrdinalIgnoreCase);
        var visible = count > 0;
        if (invert) visible = !visible;

        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
