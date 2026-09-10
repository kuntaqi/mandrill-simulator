namespace MandrillSimulator.Api;

public interface IMandrillEndpoint
{
    // Normalised route, e.g. "/messages/send". See SimulatorServer.NormalisePath.
    string Route { get; }

    Task<object?> HandleAsync(ApiRequest request, CancellationToken cancellationToken);
}
