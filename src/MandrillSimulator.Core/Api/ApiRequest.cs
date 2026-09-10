using System.Text.Json;

namespace MandrillSimulator.Api;

public record ApiRequest(JsonElement Payload, string RawBody, string Route);
