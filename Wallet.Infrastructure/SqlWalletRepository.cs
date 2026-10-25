using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Wallet.Application;
using DomainWallet = Wallet.Domain.Wallet;

namespace Wallet.Infrastructure;

public sealed class SqlWalletRepository(WalletDbContext db) : IWalletRepository
{
    public async Task<WalletCreation> CreateOrGetAsync(DomainWallet candidate, CancellationToken ct)
    {
        try
        {
            var existing = await FindAsync(candidate.UserId, ct);
            if (existing is not null) return new(existing, false);

            db.Wallets.Add(candidate);
            try
            {
                await db.SaveChangesAsync(ct);
                return new(candidate, true);
            }
            catch (DbUpdateException e) when (e.InnerException is SqlException { Number: 2601 or 2627 })
            {
                // Otra solicitud pudo crearla entre el SELECT y el INSERT.
                db.Entry(candidate).State = EntityState.Detached;
                existing = await FindAsync(candidate.UserId, ct);
                if (existing is null) throw new StorageUnavailableException(e);
                return new(existing, false);
            }
        }
        catch (DbUpdateException e) when (e.InnerException is SqlException)
        {
            throw new StorageUnavailableException(e);
        }
        catch (SqlException e)
        {
            throw new StorageUnavailableException(e);
        }
    }

    private Task<DomainWallet?> FindAsync(Guid userId, CancellationToken ct) =>
        db.Wallets.AsNoTracking().SingleOrDefaultAsync(w => w.UserId == userId, ct);
}
