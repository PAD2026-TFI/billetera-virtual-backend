using Wallet.Domain;

namespace Wallet.Application;

public interface IExternalClientRepository
{
    Task<ExternalClient?> FindByApiKeyHashAsync(string apiKeyHash, CancellationToken ct);
    Task<ExternalClient?> FindByIdAsync(Guid id, CancellationToken ct);
    Task CreateAsync(ExternalClient client, CancellationToken ct);
}
