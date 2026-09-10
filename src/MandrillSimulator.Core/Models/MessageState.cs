namespace MandrillSimulator.Models;

// Send responses only ever carry the first five. The rest exist solely on
// messages/search results, but the inbox presents one merged filter list.
public enum MessageState
{
    Sent,
    Queued,
    Rejected,
    Invalid,
    Scheduled,
    Bounced,
    SoftBounced,
    Spam,
    Unsub,
    Deferred
}
