using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace MandrillSimulator.Converters;

// Maps a wire state ("sent", "soft-bounced", ...) to the chip colours. The
// parameter picks which half: "fg" for text and dot, anything else for the fill.
public class StateBrushConverter : IValueConverter
{
    private static readonly Dictionary<string, (string Foreground, string Background)> Palette = new()
    {
        ["sent"] = ("#1A7F37", "#E7F5EC"),
        ["queued"] = ("#9A6700", "#FFF6DF"),
        ["scheduled"] = ("#0F62B0", "#E8F1FB"),
        ["rejected"] = ("#C0342B", "#FDECEA"),
        ["invalid"] = ("#C0342B", "#FDECEA"),
        ["bounced"] = ("#B5540A", "#FCF0E5"),
        ["soft-bounced"] = ("#B5540A", "#FCF0E5"),
        ["spam"] = ("#7A4FBF", "#F3EDFC"),
        ["unsub"] = ("#5A5F66", "#EFF1F3"),
        ["deferred"] = ("#9A6700", "#FFF6DF")
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var state = value as string ?? string.Empty;
        var entry = Palette.TryGetValue(state, out var found)
            ? found
            : (Foreground: "#5A5F66", Background: "#EFF1F3");

        var wantsForeground = string.Equals(parameter as string, "fg", StringComparison.OrdinalIgnoreCase);
        return SolidColorBrush.Parse(wantsForeground ? entry.Foreground : entry.Background);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
