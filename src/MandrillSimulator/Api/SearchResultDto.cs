namespace MandrillSimulator.Api;

// Wire shape of a messages/search result. Lower-case names are deliberate.
public class SearchResultDto
{
    public string _id { get; init; } = string.Empty;
    public string? bounce_description { get; init; }
    public int clicks { get; init; }
    public string? diag { get; init; }
    public string email { get; init; } = string.Empty;
    public Dictionary<string, string> metadata { get; init; } = [];
    public int opens { get; init; }
    public string sender { get; init; } = string.Empty;
    public List<object> smtp_events { get; init; } = [];
    public string state { get; init; } = "sent";
    public string subject { get; init; } = string.Empty;
    public string[] tags { get; init; } = [];
    public long ts { get; init; }
}
