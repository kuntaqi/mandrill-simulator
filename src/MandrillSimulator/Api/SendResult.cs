namespace MandrillSimulator.Api;

// Wire shape of a /messages/send.json result entry. Lower-case names are
// deliberate - this is serialised straight onto the wire.
public class SendResult
{
    public string email { get; init; } = string.Empty;
    public string _id { get; init; } = string.Empty;
    public string status { get; init; } = "sent";
    public string? reject_reason { get; init; }
}
