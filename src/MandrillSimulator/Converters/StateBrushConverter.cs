using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace MandrillSimulator.Converters;

public class StateBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var state = value as string ?? string.Empty;
        var hex = state switch
        {
            "sent" => "#1A7F37",
            "queued" => "#9A6700",
            "scheduled" => "#0969DA",
            "rejected" or "invalid" => "#CF222E",
            "bounced" or "soft-bounced" => "#BC4C00",
            "spam" => "#8250DF",
            "unsub" => "#6E7781",
            "deferred" => "#9A6700",
            _ => "#6E7781"
        };

        return (SolidColorBrush)new BrushConverter().ConvertFrom(hex)!;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
