namespace MandrillSimulator.Api;

// Carries both shapes on purpose: Mandrill's own lower-case fields, and the
// Pascal-case ones a caller deserialising into a StructResult-style model needs.
public class RejectDeleteResult
{
    public string email { get; init; } = string.Empty;
    public bool deleted { get; init; }
    public string? subaccount { get; init; }
    public bool Status { get; init; }
    public string Message { get; init; } = string.Empty;
    public object[] Results { get; init; } = [];
}
