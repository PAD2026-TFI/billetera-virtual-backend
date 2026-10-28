using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Wallet.Application;
using Wallet.Domain;
namespace Wallet.Infrastructure;

public sealed class SqlUserRepository(WalletDbContext db) : IUserRepository
{
    public Task<User?> FindByEmailAsync(string normalizedEmail, CancellationToken ct) =>
        ExecuteAsync(() => db.Users.SingleOrDefaultAsync(x => x.NormalizedEmail == normalizedEmail, ct));
    public Task<User?> FindByIdAsync(Guid id, CancellationToken ct) =>
        ExecuteAsync(() => db.Users.SingleOrDefaultAsync(x => x.Id == id, ct));
    public async Task AddAsync(User user, CancellationToken ct)
    {
        db.Users.Add(user);
        try { await ExecuteAsync(() => db.SaveChangesAsync(ct)); }
        catch (DuplicateEmailException) { db.Entry(user).State = EntityState.Detached; throw; }
    }
    public async Task SaveAsync(CancellationToken ct) => await ExecuteAsync(() => db.SaveChangesAsync(ct));
    public async Task<bool> IsReadyAsync(CancellationToken ct)
    {
        try
        {
            // CanConnect o SELECT Users.Id no detectan migraciones ni tablas financieras faltantes.
            if ((await db.Database.GetPendingMigrationsAsync(ct)).Any()) return false;
            await db.Users.AsNoTracking().Take(1).ToListAsync(ct);
            await db.Wallets.AsNoTracking().Take(1).ToListAsync(ct);
            await db.Transfers.AsNoTracking().Take(1).ToListAsync(ct);
            await db.DevelopmentFundings.AsNoTracking().Take(1).ToListAsync(ct);
            await db.Movements.AsNoTracking().Take(1).ToListAsync(ct);
            await db.Payments.AsNoTracking().Take(1).ToListAsync(ct);
            await db.PaymentReceipts.AsNoTracking().Take(1).ToListAsync(ct);
            return true;
        }
        catch (SqlException) { return false; }
        catch (InvalidOperationException) { return false; }
    }
    private static async Task<T> ExecuteAsync<T>(Func<Task<T>> operation)
    {
        try { return await operation(); }
        catch (DbUpdateConcurrencyException) { throw new ConcurrentUpdateException(); }
        catch (DbUpdateException e) when (e.InnerException is SqlException { Number: 2601 or 2627 })
        { throw new DuplicateEmailException(); }
        catch (DbUpdateException e) when (e.InnerException is SqlException)
        { throw new StorageUnavailableException(e); }
        catch (SqlException e) { throw new StorageUnavailableException(e); }
    }
}
