using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Wallet.Application;
using Wallet.Domain;

namespace Wallet.Infrastructure;

// Sólo la herramienta local registra este servicio; no está disponible desde la API.
public sealed class SqlDevelopmentFundingStore(DbContextOptions<WalletDbContext> options, TimeProvider clock) : IDevelopmentFundingStore
{
    public async Task<DevelopmentFundingResult> FundAsync(DevelopmentFundingRequest request, string hash, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                // Contexto nuevo en cada intento: ningún estado fallido sobrevive al rollback.
                await using var db = new WalletDbContext(options);
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                var wallet = await db.Wallets.FromSqlInterpolated(
                    $"SELECT * FROM [Wallets] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {request.WalletId}")
                    .SingleOrDefaultAsync(ct) ?? throw new FundingRejectedException("WALLET_NOT_FOUND");
                var isActive = await db.Database.SqlQuery<bool>(
                    $"SELECT [IsActive] AS [Value] FROM [WalletOwners] WITH (HOLDLOCK) WHERE [Id] = {wallet.OwnerId}").SingleAsync(ct);
                if (!isActive) throw new FundingRejectedException("USER_INACTIVE");

                var previous = await db.DevelopmentFundings.AsNoTracking()
                    .SingleOrDefaultAsync(f => f.IdempotencyKey == request.IdempotencyKey, ct);
                if (previous is not null)
                {
                    if (previous.RequestHash != hash || previous.WalletId != request.WalletId
                        || previous.Amount.Amount != request.Amount || previous.Currency != "ARS") throw new FundingRejectedException("IDEMPOTENCY_CONFLICT");
                    var movement = await db.Movements.AsNoTracking().SingleAsync(m => m.DevelopmentFundingId == previous.Id, ct);
                    await transaction.CommitAsync(ct);
                    return Result(previous, movement.BalanceAfter.Amount, false);
                }

                wallet.Credit(request.Amount);
                var funding = DevelopmentFunding.Create(wallet.Id, request.Amount, request.IdempotencyKey, hash, clock.GetUtcNow());
                db.DevelopmentFundings.Add(funding);
                db.Movements.Add(Movement.ForDevelopmentFunding(funding, wallet.Balance));
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return Result(funding, wallet.Balance.Amount, true);
                // DisposeAsync revierte cualquier transacción que no llegó a confirmar.
            }
            catch (DbUpdateConcurrencyException) when (attempt < 2) { }
            catch (DbUpdateException e) when (attempt < 2 && e.InnerException is SqlException { Number: 2601 or 2627 or 1205 }) { }
            catch (SqlException e) when (attempt < 2 && e.Number == 1205) { }
            catch (DbUpdateConcurrencyException) { throw new FundingRejectedException("CONCURRENCY_CONFLICT"); }
            catch (DbUpdateException e) when (e.InnerException is SqlException) { throw new StorageUnavailableException(e); }
            catch (SqlException e) { throw new StorageUnavailableException(e); }
        }
    }

    private static DevelopmentFundingResult Result(DevelopmentFunding funding, decimal balance, bool created) =>
        new(funding.Id, funding.WalletId, funding.Amount.Amount, funding.Currency, funding.CreatedAt, balance, created);
}
