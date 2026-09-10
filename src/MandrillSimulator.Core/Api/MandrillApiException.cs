namespace MandrillSimulator.Api;

// Rendered as a 500 with Mandrill's error body, which is what the client's
// error path expects to deserialise.
public class MandrillApiException : Exception
{
    public MandrillApiException(string name, string message, int code = -1) : base(message)
    {
        Name = name;
        Code = code;
    }

    public string Name { get; }
    public int Code { get; }
}
