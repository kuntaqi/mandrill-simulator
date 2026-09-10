namespace MandrillSimulator.Models;

public class ExportJob
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;
    public DateTimeOffset? FinishedAt { get; set; }
    public string Type { get; init; } = "activity";
    public string State { get; set; } = "waiting";
    public string? ResultUrl { get; set; }
    public string Csv { get; set; } = string.Empty;
}
