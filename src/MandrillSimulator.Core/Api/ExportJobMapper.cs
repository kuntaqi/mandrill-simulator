using System.Globalization;
using MandrillSimulator.Models;

namespace MandrillSimulator.Api;

public static class ExportJobMapper
{
    private const string Format = "yyyy-MM-dd HH:mm:ss";

    public static ExportJobDto ToDto(ExportJob job) => new()
    {
        id = job.Id,
        created_at = job.CreatedAt.ToString(Format, CultureInfo.InvariantCulture),
        type = job.Type,
        finished_at = job.FinishedAt?.ToString(Format, CultureInfo.InvariantCulture),
        state = job.State,
        result_url = job.ResultUrl
    };
}
