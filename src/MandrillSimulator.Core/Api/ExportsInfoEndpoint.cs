using MandrillSimulator.Store;

namespace MandrillSimulator.Api;

public class ExportsInfoEndpoint : IMandrillEndpoint
{
    private readonly ExportJobStore _jobs;

    public ExportsInfoEndpoint(ExportJobStore jobs)
    {
        _jobs = jobs;
    }

    public string Route => "/exports/info";

    public Task<object?> HandleAsync(ApiRequest request, CancellationToken cancellationToken)
    {
        var id = request.Payload.GetString("id");
        if (string.IsNullOrWhiteSpace(id))
            throw new MandrillApiException("ValidationError", "No export id supplied.");

        var job = _jobs.Find(id)
                  ?? throw new MandrillApiException("Unknown_Export", $"No export exists with the id '{id}'.");

        return Task.FromResult<object?>(ExportJobMapper.ToDto(job));
    }
}
