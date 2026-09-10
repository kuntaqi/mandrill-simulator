using MandrillSimulator.Models;
using MandrillSimulator.Store;

namespace MandrillSimulator.Api;

public class SearchEndpoint : IMandrillEndpoint
{
    private readonly MessageStore _store;

    public SearchEndpoint(MessageStore store)
    {
        _store = store;
    }

    public string Route => "/messages/search";

    public Task<object?> HandleAsync(ApiRequest request, CancellationToken cancellationToken)
    {
        var payload = request.Payload;

        var matches = _store.Search(
            payload.GetString("query"),
            payload.GetLooseDate("date_from"),
            payload.GetLooseDate("date_to"),
            payload.GetStringArray("tags"),
            payload.GetStringArray("senders"),
            payload.GetLooseInt("limit", 100));

        var results = matches.Select(ToDto).Cast<object>().ToList();
        return Task.FromResult<object?>(results);
    }

    private static SearchResultDto ToDto(CapturedMessage message) => new()
    {
        _id = message.Id,
        bounce_description = message.BounceDescription,
        clicks = message.Clicks,
        diag = null,
        email = message.ToEmail,
        opens = message.Opens,
        sender = message.FromEmail,
        state = MessageStates.ToWire(message.State),
        subject = message.Subject,
        tags = message.Tags.ToArray(),
        ts = message.UnixTimestamp
    };
}
