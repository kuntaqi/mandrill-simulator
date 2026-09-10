using System.Globalization;
using System.Text;
using MandrillSimulator.Models;
using MandrillSimulator.Store;

namespace MandrillSimulator.Api;

public class ExportsActivityEndpoint : IMandrillEndpoint
{
    private readonly MessageStore _messages;
    private readonly ExportJobStore _jobs;
    private readonly Func<string> _baseUrl;

    public ExportsActivityEndpoint(MessageStore messages, ExportJobStore jobs, Func<string> baseUrl)
    {
        _messages = messages;
        _jobs = jobs;
        _baseUrl = baseUrl;
    }

    public string Route => "/exports/activity";

    public Task<object?> HandleAsync(ApiRequest request, CancellationToken cancellationToken)
    {
        var payload = request.Payload;

        var matches = _messages.Search(
            query: null,
            payload.GetLooseDate("date_from"),
            payload.GetLooseDate("date_to"),
            payload.GetStringArray("tags"),
            payload.GetStringArray("senders"),
            limit: int.MaxValue);

        var states = payload.GetStringArray("states");
        if (states.Count > 0)
        {
            var wanted = states.Select(MessageStates.FromWire).ToHashSet();
            matches = matches.Where(m => wanted.Contains(m.State)).ToList();
        }

        var job = new ExportJob
        {
            // The real API queues the export and mails a link. Completing it
            // immediately is the whole point of a simulator.
            State = "complete",
            FinishedAt = DateTimeOffset.Now,
            Csv = BuildCsv(matches)
        };
        job.ResultUrl = $"{_baseUrl().TrimEnd('/')}/exports/download/{job.Id}.csv";
        _jobs.Add(job);

        return Task.FromResult<object?>(ExportJobMapper.ToDto(job));
    }

    private static string BuildCsv(IReadOnlyList<CapturedMessage> messages)
    {
        var csv = new StringBuilder();
        csv.AppendLine("Date,Email Address,Sender,Subject,Status,Tags,Opens,Clicks,Bounce Detail");

        foreach (var message in messages)
        {
            csv.Append(Escape(message.ReceivedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))).Append(',');
            csv.Append(Escape(message.ToEmail)).Append(',');
            csv.Append(Escape(message.FromEmail)).Append(',');
            csv.Append(Escape(message.Subject)).Append(',');
            csv.Append(Escape(MessageStates.ToWire(message.State))).Append(',');
            csv.Append(Escape(string.Join(' ', message.Tags))).Append(',');
            csv.Append(message.Opens).Append(',');
            csv.Append(message.Clicks).Append(',');
            csv.AppendLine(Escape(message.BounceDescription ?? string.Empty));
        }

        return csv.ToString();
    }

    private static string Escape(string value) =>
        value.Contains(',') || value.Contains('"')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
}
