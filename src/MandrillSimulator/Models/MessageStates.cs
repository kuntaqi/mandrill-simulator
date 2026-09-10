namespace MandrillSimulator.Models;

public static class MessageStates
{
    // Ame.Email.Mandrill deserialises these into an enum, so casing matters and
    // soft-bounced is the one value that is not a straight lowercase of the name.
    public static string ToWire(MessageState state) => state switch
    {
        MessageState.SoftBounced => "soft-bounced",
        _ => state.ToString().ToLowerInvariant()
    };

    public static MessageState FromWire(string? wire) => (wire ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "soft-bounced" or "softbounced" => MessageState.SoftBounced,
        "queued" => MessageState.Queued,
        "rejected" => MessageState.Rejected,
        "invalid" => MessageState.Invalid,
        "scheduled" => MessageState.Scheduled,
        "bounced" => MessageState.Bounced,
        "spam" => MessageState.Spam,
        "unsub" => MessageState.Unsub,
        "deferred" => MessageState.Deferred,
        _ => MessageState.Sent
    };

    public static bool IsSendState(MessageState state) =>
        state is MessageState.Sent or MessageState.Queued or MessageState.Rejected
            or MessageState.Invalid or MessageState.Scheduled;

    public static IReadOnlyList<MessageState> All { get; } = Enum.GetValues<MessageState>();
}
