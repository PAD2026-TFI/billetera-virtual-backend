using Microsoft.EntityFrameworkCore;
using Wallet.Application;
using Wallet.Domain;

namespace Wallet.Infrastructure;

public sealed class SqlExternalClientRepository(WalletDbContext db) : IExternalClientRepository
{
    public async Task<ExternalClient?> FindByApiKeyHashAsync(string apiKeyHash, CancellationToken ct)
    {
        return await db.ExternalClients.SingleOrDefaultAsync(c => c.ApiKeyHash == apiKeyHash, ct);
    }

    public async Task<ExternalClient?> FindByIdAsync(Guid id, CancellationToken ct)
    {
        return await db.ExternalClients.SingleOrDefaultAsync(c => c.Id == id, ct);
    }

    public async Task<ExternalClient?> FindByCuitAsync(string cuit, CancellationToken ct)
    {
        return await db.ExternalClients.SingleOrDefaultAsync(c => c.Cuit == cuit, ct);
    }

    public async Task CreateAsync(ExternalClient client, CancellationToken ct)
    {
        db.ExternalClients.Add(client);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(ExternalClient client, CancellationToken ct)
    {
        db.ExternalClients.Update(client);
        await db.SaveChangesAsync(ct);
    }
}
