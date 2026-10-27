using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Wallet.Application;
using Wallet.Domain;
using DomainWallet = Wallet.Domain.Wallet;

namespace Wallet.Infrastructure;

public sealed class SqlTransferStore(DbContextOptions<WalletDbContext> options, TimeProvider clock) : ITransferStore
{
    public async Task<TransferExecution> ExecuteAsync(TransferCommand command, string hash, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await using var db = new WalletDbContext(options);
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                var sourceId = await db.Wallets.AsNoTracking().Where(w => w.UserId == command.SourceUserId)
                    .Select(w => w.Id).SingleOrDefaultAsync(ct);
                if (sourceId == Guid.Empty) throw new TransferRejectedException("SOURCE_WALLET_NOT_FOUND");

                // Recuperar una confirmada no requiere otra vez saldo ni un destino aún activo.
                var previous = await Previous(db, sourceId, command.IdempotencyKey, ct);
                if (previous is not null) return Replay(previous, command, hash);
                if (sourceId == command.DestinationWalletId) throw new TransferRejectedException("SAME_WALLET");

                var locked = new Dictionary<Guid, DomainWallet>();
                foreach (var id in new[] { sourceId, command.DestinationWalletId }.OrderBy(id => id))
                {
                    var wallet = await db.Wallets.FromSqlInterpolated(
                        $"SELECT * FROM [Wallets] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {id}").SingleOrDefaultAsync(ct);
                    if (wallet is null) throw new TransferRejectedException(id == sourceId ? "SOURCE_WALLET_NOT_FOUND" : "DESTINATION_UNAVAILABLE");
                    locked.Add(id, wallet);
                }
                // Otra solicitud con la misma clave pudo confirmar mientras esperábamos el lock.
                previous = await Previous(db, sourceId, command.IdempotencyKey, ct);
                if (previous is not null) return Replay(previous, command, hash);

                var source = locked[sourceId]; var destination = locked[command.DestinationWalletId];
                foreach (var userId in new[] { source.UserId, destination.UserId }.Distinct().OrderBy(id => id))
                {
                    var user = await db.Users.FromSqlInterpolated(
                        $"SELECT * FROM [Users] WITH (HOLDLOCK) WHERE [Id] = {userId}").SingleAsync(ct);
                    if (!user.IsActive) throw new TransferRejectedException(userId == source.UserId ? "INVALID_SESSION" : "DESTINATION_UNAVAILABLE");
                }
                if (source.Currency != "ARS" || destination.Currency != "ARS") throw new TransferRejectedException("DESTINATION_UNAVAILABLE");
                if (source.Balance < command.Amount) throw new TransferRejectedException("INSUFFICIENT_FUNDS");
                if (command.Amount > MoneyRules.MaxBalance - destination.Balance) throw new TransferRejectedException("DESTINATION_BALANCE_LIMIT");

                var transfer = Transfer.Complete(source.Id, destination.Id, command.Amount, command.IdempotencyKey, hash, clock.GetUtcNow());
                ct.ThrowIfCancellationRequested();
                transfer.ApplyTo(source, destination);
                db.Transfers.Add(transfer);
                db.Movements.AddRange(Movement.ForCompletedTransfer(transfer, source, destination));
                await db.SaveChangesAsync(ct);
                ct.ThrowIfCancellationRequested();
                await transaction.CommitAsync(ct);
                return new(TransferResponse.From(transfer), true);
            }
            catch (DbUpdateConcurrencyException) when (attempt < 2) { }
            catch (DbUpdateException e) when (attempt < 2 && e.InnerException is SqlException { Number: 2601 or 2627 or 1205 }) { }
            catch (SqlException e) when (attempt < 2 && e.Number == 1205) { }
            catch (DbUpdateConcurrencyException) { throw new TransferRejectedException("CONCURRENCY_CONFLICT"); }
            catch (DbUpdateException e) when (e.InnerException is SqlException { Number: 2601 or 2627 or 1205 })
            { throw new TransferRejectedException("CONCURRENCY_CONFLICT"); }
            catch (SqlException e) when (e.Number == 1205) { throw new TransferRejectedException("CONCURRENCY_CONFLICT"); }
            catch (DbUpdateException e) when (e.InnerException is SqlException) { throw new StorageUnavailableException(e); }
            catch (SqlException e) { throw new StorageUnavailableException(e); }
            // La disposición de la transacción revierte cualquier intento sin commit.
        }
    }
    private static Task<Transfer?> Previous(WalletDbContext db, Guid source, Guid key, CancellationToken ct) =>
        db.Transfers.AsNoTracking().SingleOrDefaultAsync(t => t.SourceWalletId == source && t.IdempotencyKey == key, ct);
    private static TransferExecution Replay(Transfer transfer, TransferCommand command, string hash)
    {
        if (!transfer.MatchesRequest(command.DestinationWalletId, command.Amount, hash))
            throw new TransferRejectedException("IDEMPOTENCY_CONFLICT");
        return new(TransferResponse.From(transfer), false);
    }
}
