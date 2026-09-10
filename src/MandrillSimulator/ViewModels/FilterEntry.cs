namespace MandrillSimulator.ViewModels;

public class FilterEntry : ObservableObject
{
    private int _count;

    public FilterEntry(string label, FilterKind kind, string? value = null)
    {
        Label = label;
        Kind = kind;
        Value = value;
    }

    public string Label { get; }
    public FilterKind Kind { get; }
    public string? Value { get; }

    // Drives the small colour square in the rail; "" means no square (All messages).
    public string DotState => Kind == FilterKind.State ? Value ?? string.Empty : string.Empty;

    public bool HasDot => Kind == FilterKind.State;

    public bool IsUntagged => Value == "(untagged)";

    public int Count
    {
        get => _count;
        set => Set(ref _count, value);
    }
}
