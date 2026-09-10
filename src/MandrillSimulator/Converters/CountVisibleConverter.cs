using System.Collections;
using System.Globalization;
using Avalonia.Data.Converters;

namespace MandrillSimulator.Converters;

public class CountVisibleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var count = value switch
        {
            int n => n,
            ICollection collection => collection.Count,
            _ => 0
        };

        var visible = count > 0;
        if (string.Equals(parameter as string, "invert", StringComparison.OrdinalIgnoreCase)) visible = !visible;
        return visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
