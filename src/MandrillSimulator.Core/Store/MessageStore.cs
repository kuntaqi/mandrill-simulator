using MandrillSimulator.Models;

namespace MandrillSimulator.Store;

// Single source of truth for captured mail. The HTTP listener writes to it from
// worker threads and the UI reads from the dispatcher thread, so every public
// member takes the lock.
public class MessageStore
{
    private readonly List<CapturedMessage> _messages = [];
    private readonly Lock _gate = new();

    public event EventHandler<CapturedMessage>? MessageAdded;
    public event EventHandler? Cleared;

    public int Count
    {
        get
        {
            lock (_gate) return _messages.Count;
        }
    }

    public void Add(CapturedMessage message)
    {
        lock (_gate) _messages.Add(message);
        MessageAdded?.Invoke(this, message);
    }

    public IReadOnlyList<CapturedMessage> Snapshot()
    {
        lock (_gate) return _messages.ToList();
    }

    public CapturedMessage? FindById(string id)
    {
        lock (_gate) return _messages.FirstOrDefault(m => m.Id == id);
    }

    public void Clear()
    {
        lock (_gate) _messages.Clear();
        Cleared?.Invoke(this, EventArgs.Empty);
    }

    // Mirrors the filtering messages/search actually applies. Unset criteria are
    // ignored rather than treated as empty, which is how the real API behaves.
    public IReadOnlyList<CapturedMessage> Search(
        string? query,
        DateTimeOffset? dateFrom,
        DateTimeOffset? dateTo,
        IReadOnlyCollection<string>? tags,
        IReadOnlyCollection<string>? senders,
        int limit)
    {
        IEnumerable<CapturedMessage> results = Snapshot();

        if (!string.IsNullOrWhiteSpace(query))
        {
            var q = query.Trim();
            results = results.Where(m =>
                m.Subject.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                m.ToEmail.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                m.Html.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        if (dateFrom.HasValue) results = results.Where(m => m.ReceivedAt >= dateFrom.Value);
        if (dateTo.HasValue) results = results.Where(m => m.ReceivedAt <= dateTo.Value);

        if (tags is { Count: > 0 })
            results = results.Where(m => m.Tags.Any(t => tags.Contains(t, StringComparer.OrdinalIgnoreCase)));

        if (senders is { Count: > 0 })
            results = results.Where(m => senders.Contains(m.FromEmail, StringComparer.OrdinalIgnoreCase));

        return results
            .OrderByDescending(m => m.ReceivedAt)
            .Take(limit > 0 ? limit : 100)
            .ToList();
    }

    public int RemoveRejectsFor(string email)
    {
        lock (_gate)
        {
            var affected = _messages
                .Where(m => string.Equals(m.ToEmail, email, StringComparison.OrdinalIgnoreCase)
                            && m.State is MessageState.Rejected or MessageState.Bounced or MessageState.SoftBounced
                                or MessageState.Spam or MessageState.Unsub)
                .ToList();

            foreach (var message in affected)
            {
                message.State = MessageState.Sent;
                message.RejectReason = null;
                message.BounceDescription = null;
            }

            return affected.Count;
        }
    }
}
