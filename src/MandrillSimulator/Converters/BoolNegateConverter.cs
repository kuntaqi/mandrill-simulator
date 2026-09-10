using System.Globalization;
using Avalonia.Data.Converters;

namespace MandrillSimulator.Converters;

public class BoolNegateConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;
}
