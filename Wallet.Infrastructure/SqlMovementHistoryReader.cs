using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Wallet.Application;
using Wallet.Domain;

namespace Wallet.Infrastructure;

public sealed class SqlMovementHistoryReader(WalletDbContext db) : IMovementHistoryReader
{
    public async Task<IReadOnlyList<Movement>> ReadAsync(Guid walletId, int count, MovementPosition? before, CancellationToken ct)
    {
        try
        {
            // Comparación y orden de UUID nativos de SQL Server; no mezclo el orden de Guid de .NET.
            var query = before is null ? db.Movements.Where(m => m.WalletId == walletId)
                : db.Movements.FromSqlInterpolated($"SELECT * FROM [Movements] WHERE [WalletId] = {walletId} AND ([CreatedAt] < {before.CreatedAt} OR ([CreatedAt] = {before.CreatedAt} AND [Id] < {before.Id}))");
            return await query.AsNoTracking().OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
                .Take(count).ToListAsync(ct);
        }
        catch (SqlException e) { throw new StorageUnavailableException(e); }
    }
}
