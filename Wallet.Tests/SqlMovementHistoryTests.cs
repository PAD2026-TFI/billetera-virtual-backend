using Microsoft.EntityFrameworkCore;
using Wallet.Application;
using Wallet.Domain.Entities;
using Wallet.Infrastructure;
using Xunit;
using DomainWallet = Wallet.Domain.Entities.Wallet;

namespace Wallet.Tests;

[Collection("SqlIntegration")]
[Trait("Category", "Sql")]
public sealed class SqlMovementHistoryTests
{
    [AzureSqlFact]
    public async Task Equal_timestamp_pages_use_SQL_UUID_order_without_duplicates_or_writes()
    {
        var options = new DbContextOptionsBuilder<WalletDbContext>()
            .UseSqlServer(Environment.GetEnvironmentVariable("WALLET_TEST_CONNECTION")!).Options;
        var user = User.Create("History", "SQL", $"history-{Guid.NewGuid():N}@example.com", DateTimeOffset.UtcNow);
        user.SetPasswordHash(new IdentityPasswordService().Hash(user, IdentityTestHost.Password));
        var wallet = DomainWallet.Create(user.Id, DateTimeOffset.UtcNow);
        try
        {
            await using (var db = new WalletDbContext(options))
            { db.Users.Add(user); db.Wallets.Add(wallet); await db.SaveChangesAsync(); }
            var funding = new DevelopmentFundingService(new SqlDevelopmentFundingStore(options, new FixedClock()));
            for (var i = 0; i < 5; i++) await funding.FundAsync(new(wallet.Id, 1m, Guid.NewGuid()), CancellationToken.None);
            await using var readerDb = new WalletDbContext(options);
            var baseline = await readerDb.Wallets.AsNoTracking().SingleAsync(w => w.Id == wallet.Id);
            var expected = await readerDb.Movements.Where(m => m.WalletId == wallet.Id)
                .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).Select(m => m.Id).ToArrayAsync();
            var reader = new SqlMovementHistoryReader(readerDb);
            var seen = new List<Guid>(); MovementPosition? position = null;
            while (true)
            {
                var page = await reader.ReadAsync(wallet.Id, 2, position, CancellationToken.None);
                if (page.Count == 0) break;
                seen.AddRange(page.Select(m => m.Id)); position = new(page[^1].CreatedAt, page[^1].Id);
            }
            Assert.Equal(expected, seen); Assert.Equal(5, seen.Distinct().Count());
            Assert.Empty(await reader.ReadAsync(Guid.NewGuid(), 2, null, CancellationToken.None));
            Assert.Empty(readerDb.ChangeTracker.Entries());
            var after = await readerDb.Wallets.AsNoTracking().SingleAsync(w => w.Id == wallet.Id);
            Assert.Equal(baseline.Balance, after.Balance); Assert.Equal(baseline.RowVersion, after.RowVersion);
        }
        finally
        {
            await using var cleanup = new WalletDbContext(options);
            await cleanup.Movements.Where(m => m.WalletId == wallet.Id).ExecuteDeleteAsync();
            await cleanup.DevelopmentFundings.Where(f => f.WalletId == wallet.Id).ExecuteDeleteAsync();
            await cleanup.Wallets.Where(w => w.Id == wallet.Id).ExecuteDeleteAsync();
            await cleanup.Users.Where(u => u.Id == user.Id).ExecuteDeleteAsync();
        }
    }
    private sealed class FixedClock : TimeProvider
    { public override DateTimeOffset GetUtcNow() => new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero); }
}
