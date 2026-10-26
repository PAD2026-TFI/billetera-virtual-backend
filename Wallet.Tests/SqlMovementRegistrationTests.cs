using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Wallet.Application;
using Wallet.Domain;
using Wallet.Infrastructure;
using Xunit;
using DomainWallet = Wallet.Domain.Wallet;

namespace Wallet.Tests;

[Trait("Category", "Sql")]
public sealed class SqlMovementRegistrationTests
{
    [AzureSqlFact]
    public async Task Transfer_has_two_consistent_movements_and_SQL_rejects_duplicates_and_orphans()
    {
        var options = new DbContextOptionsBuilder<WalletDbContext>()
            .UseSqlServer(Environment.GetEnvironmentVariable("WALLET_TEST_CONNECTION")!).Options;
        var users = Enumerable.Range(0, 2).Select(_ =>
        {
            var user = User.Create("Movement", "SQL", $"mov-{Guid.NewGuid():N}@example.com", DateTimeOffset.UtcNow);
            user.SetPasswordHash(new IdentityPasswordService().Hash(user, IdentityTestHost.Password)); return user;
        }).ToArray();
        var wallets = users.Select(u => DomainWallet.Create(u.Id, DateTimeOffset.UtcNow)).ToArray();
        var ids = wallets.Select(w => w.Id).ToArray(); var userIds = users.Select(u => u.Id).ToArray();
        try
        {
            await using (var db = new WalletDbContext(options))
            { db.Users.AddRange(users); db.Wallets.AddRange(wallets); await db.SaveChangesAsync(); }
            await new DevelopmentFundingService(new SqlDevelopmentFundingStore(options, TimeProvider.System))
                .FundAsync(new(ids[0], 100m, Guid.NewGuid()), CancellationToken.None);
            var command = new TransferCommand(users[0].Id, ids[1], 20m, Guid.NewGuid());
            var store = new SqlTransferStore(options, TimeProvider.System);
            var result = await store.ExecuteAsync(command, TransferService.Fingerprint(ids[1], 20m), CancellationToken.None);
            var replay = await store.ExecuteAsync(command, TransferService.Fingerprint(ids[1], 20m), CancellationToken.None);
            Assert.False(replay.Created);
            Transfer transfer;
            await using (var db = new WalletDbContext(options))
            {
                transfer = await db.Transfers.AsNoTracking().SingleAsync(t => t.Id == result.Transfer.Id);
                var movements = await db.Movements.Where(m => m.TransferId == transfer.Id).ToListAsync();
                Assert.Equal(2, movements.Count);
                Assert.Contains(movements, m => m.WalletId == ids[0] && m.Type == MovementType.Debit && m.BalanceAfter == 80m);
                Assert.Contains(movements, m => m.WalletId == ids[1] && m.Type == MovementType.Credit && m.BalanceAfter == 20m);
                Assert.All(movements, m => Assert.Equal(20m, m.Amount));
                Assert.Equal(1, await db.Movements.CountAsync(m => m.WalletId == ids[0] && m.Source == MovementSource.DevelopmentFunding));
            }
            await using (var duplicate = new WalletDbContext(options))
            {
                duplicate.Movements.Add(Movement.ForTransfer(transfer, true, 80m));
                await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
            }
            await using (var orphan = new WalletDbContext(options))
            {
                var nonexistent = Transfer.Complete(ids[0], ids[1], 20m, Guid.NewGuid(), TransferService.Fingerprint(ids[1], 20m), DateTimeOffset.UtcNow);
                orphan.Movements.Add(Movement.ForTransfer(nonexistent, true, 80m));
                await Assert.ThrowsAsync<DbUpdateException>(() => orphan.SaveChangesAsync());
            }
            await using (var invalidSource = new WalletDbContext(options))
                await Assert.ThrowsAsync<SqlException>(() => invalidSource.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE [Movements] SET [Source] = 'DevelopmentFunding' WHERE [TransferId] = {transfer.Id}"));
            await using (var db = new WalletDbContext(options))
                Assert.Equal(2, await db.Movements.CountAsync(m => m.TransferId == transfer.Id));
        }
        finally
        {
            await using var db = new WalletDbContext(options);
            await db.Movements.Where(m => ids.Contains(m.WalletId)).ExecuteDeleteAsync();
            await db.Transfers.Where(t => ids.Contains(t.SourceWalletId)).ExecuteDeleteAsync();
            await db.DevelopmentFundings.Where(f => ids.Contains(f.WalletId)).ExecuteDeleteAsync();
            await db.Wallets.Where(w => ids.Contains(w.Id)).ExecuteDeleteAsync();
            await db.Users.Where(u => userIds.Contains(u.Id)).ExecuteDeleteAsync();
        }
    }
}
