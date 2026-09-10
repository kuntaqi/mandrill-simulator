using MandrillSimulator.Store;

namespace MandrillSimulator.Api;

public class RejectsDeleteEndpoint : IMandrillEndpoint
{
    private readonly MessageStore _store;

    public RejectsDeleteEndpoint(MessageStore store)
    {
        _store = store;
    }

    public string Route => "/rejects/delete";

    public Task<object?> HandleAsync(ApiRequest request, CancellationToken cancellationToken)
    {
        var email = request.Payload.GetString("email");
        if (string.IsNullOrWhiteSpace(email))
            throw new MandrillApiException("ValidationError", "Invalidate email.");

        var cleared = _store.RemoveRejectsFor(email);

        return Task.FromResult<object?>(new RejectDeleteResult
        {
            email = email,
            deleted = cleared > 0,
            Status = true,
            Message = cleared > 0
                ? $"Cleared the reject state on {cleared} message(s) for {email}."
                : $"No rejected message found for {email}."
        });
    }
}
