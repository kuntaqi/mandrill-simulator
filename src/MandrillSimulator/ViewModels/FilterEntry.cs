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

    public int Count
    {
        get => _count;
        set => Set(ref _count, value);
    }
}
