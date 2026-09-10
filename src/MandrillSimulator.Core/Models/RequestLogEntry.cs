namespace MandrillSimulator.Models;

public record RequestLogEntry(
    DateTimeOffset At,
    string Method,
    string RawPath,
    string Route,
    int StatusCode,
    long ElapsedMs);
