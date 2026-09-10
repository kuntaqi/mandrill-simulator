using System.Text.Json;

namespace MandrillSimulator.Api;

public static class MandrillJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null
    };

    public static string Serialize(object value) => JsonSerializer.Serialize(value, Options);

    public static string? GetString(this JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind is JsonValueKind.String
            ? value.GetString()
            : null;

    public static bool GetBool(this JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.True;

    public static List<string> GetStringArray(this JsonElement element, string name)
    {
        var items = new List<string>();
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.Array)
        {
            return items;
        }

        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } s)
                items.Add(s);
        }

        return items;
    }

    // The client sends limit as a string ("50"), so accept either form.
    public static int GetLooseInt(this JsonElement element, string name, int fallback)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
            return fallback;

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var n) => n,
            JsonValueKind.String when int.TryParse(value.GetString(), out var n) => n,
            _ => fallback
        };
    }

    // Mandrill dates are "yyyy-MM-dd HH:mm:ss"; empty strings are common.
    public static DateTimeOffset? GetLooseDate(this JsonElement element, string name)
    {
        var raw = element.GetString(name);
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return DateTimeOffset.TryParse(raw, out var parsed) ? parsed : null;
    }
}
