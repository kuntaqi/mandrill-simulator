namespace MandrillSimulator.Api;

public class ExportJobDto
{
    public string id { get; init; } = string.Empty;
    public string created_at { get; init; } = string.Empty;
    public string type { get; init; } = "activity";
    public string? finished_at { get; init; }
    public string state { get; init; } = "complete";
    public string? result_url { get; init; }
}
