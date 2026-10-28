using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Wallet.Application;
using Wallet.Domain.Entities;
using Wallet.Domain.Entities.Status;
using Wallet.Infrastructure;
using Xunit;
using DomainWallet = Wallet.Domain.Entities.Wallet;

namespace Wallet.Tests;

[Collection("SqlIntegration")]
[Trait("Category", "Sql")]
public sealed class SqlDevelopmentFundingTests
{
    private static DbContextOptions<WalletDbContext> Options(params IInterceptor[] interceptors) =>
        new DbContextOptionsBuilder<WalletDbContext>()
            .UseSqlServer(Environment.GetEnvironmentVariable("WALLET_TEST_CONNECTION")!, sql => sql.MaxBatchSize(1))
            .AddInterceptors(interceptors).Options;

    private static DevelopmentFundingService Service(params IInterceptor[] interceptors) =>
        new(new SqlDevelopmentFundingStore(Options(interceptors), TimeProvider.System));

    private static async Task<(User User, DomainWallet Wallet)> Seed()
    {
        var user = User.Create("Funding", "SQL", $"fund-{Guid.NewGuid():N}@example.com", DateTimeOffset.UtcNow);
        user.SetPasswordHash(new IdentityPasswordService().Hash(user, IdentityTestHost.Password));
        var wallet = DomainWallet.Create(user.Id, DateTimeOffset.UtcNow);
        await using var db = new WalletDbContext(Options());
        db.Users.Add(user); db.Wallets.Add(wallet);
        await db.SaveChangesAsync();
        return (user, wallet);
    }

    private static async Task Cleanup(Guid userId, Guid walletId)
    {
        await using var db = new WalletDbContext(Options());
        await db.Movements.Where(m => m.WalletId == walletId).ExecuteDeleteAsync();
        await db.DevelopmentFundings.Where(f => f.WalletId == walletId).ExecuteDeleteAsync();
        await db.Wallets.Where(w => w.Id == walletId).ExecuteDeleteAsync();
        await db.Users.Where(u => u.Id == userId).ExecuteDeleteAsync();
    }

    [AzureSqlFact]
    public async Task Repetition_returns_original_balance_and_conflicting_content_does_not_credit()
    {
        var (user, wallet) = await Seed();
        try
        {
            var request = new DevelopmentFundingRequest(wallet.Id, 100m, Guid.NewGuid());
            var created = await Service().FundAsync(request, CancellationToken.None);
            await Service().FundAsync(request with { Amount = 50m, IdempotencyKey = Guid.NewGuid() }, CancellationToken.None);
            var repeated = await Service().FundAsync(request, CancellationToken.None);
            Assert.True(created.Created); Assert.False(repeated.Created);
            Assert.Equal(created with { Created = false }, repeated);
            Assert.Equal(100m, repeated.BalanceAfter);
            Assert.Equal("IDEMPOTENCY_CONFLICT", (await Assert.ThrowsAsync<FundingRejectedException>(() =>
                Service().FundAsync(request with { Amount = 101m }, CancellationToken.None))).Code);
            await using var db = new WalletDbContext(Options());
            Assert.Equal(150m, (await db.Wallets.SingleAsync(w => w.Id == wallet.Id)).Balance);
            Assert.Equal(2, await db.DevelopmentFundings.CountAsync(f => f.WalletId == wallet.Id));
            Assert.Equal(2, await db.Movements.CountAsync(m => m.WalletId == wallet.Id));
        }
        finally { await Cleanup(user.Id, wallet.Id); }
    }

    [AzureSqlFact]
    public async Task Same_key_across_wallets_conflicts_without_affecting_other_wallet()
    {
        var first = await Seed(); var second = await Seed();
        try
        {
            var request = new DevelopmentFundingRequest(first.Wallet.Id, 10m, Guid.NewGuid());
            await Service().FundAsync(request, CancellationToken.None);
            Assert.Equal("IDEMPOTENCY_CONFLICT", (await Assert.ThrowsAsync<FundingRejectedException>(() =>
                Service().FundAsync(request with { WalletId = second.Wallet.Id }, CancellationToken.None))).Code);
            await using var db = new WalletDbContext(Options());
            Assert.Equal(0m, (await db.Wallets.SingleAsync(w => w.Id == second.Wallet.Id)).Balance);
        }
        finally { await Cleanup(first.User.Id, first.Wallet.Id); await Cleanup(second.User.Id, second.Wallet.Id); }
    }

    [AzureSqlFact]
    public async Task Concurrent_duplicates_credit_once_and_distinct_keys_do_not_lose_updates()
    {
        var (user, wallet) = await Seed();
        try
        {
            var request = new DevelopmentFundingRequest(wallet.Id, 10m, Guid.NewGuid());
            var duplicates = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Service().FundAsync(request, CancellationToken.None)));
            Assert.Single(duplicates, f => f.Created);
            Assert.Single(duplicates.Select(f => f.Id).Distinct());
            await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Service().FundAsync(
                request with { IdempotencyKey = Guid.NewGuid() }, CancellationToken.None)));
            await using var db = new WalletDbContext(Options());
            Assert.Equal(90m, (await db.Wallets.SingleAsync(w => w.Id == wallet.Id)).Balance);
            Assert.Equal(9, await db.DevelopmentFundings.CountAsync(f => f.WalletId == wallet.Id));
            var movements = await db.Movements.Where(m => m.WalletId == wallet.Id).ToListAsync();
            Assert.Equal(9, movements.Count);
            Assert.Equal(90m, movements.Sum(m => m.Amount));
            Assert.Equal(Enumerable.Range(1, 9).Select(i => i * 10m), movements.Select(m => m.BalanceAfter).OrderBy(x => x));
        }
        finally { await Cleanup(user.Id, wallet.Id); }
    }

    [AzureSqlFact]
    public async Task Failure_after_first_write_rolls_back_all_changes_and_same_key_can_retry()
    {
        var (user, wallet) = await Seed();
        try
        {
            var failure = new FailOnSecondWrite();
            var request = new DevelopmentFundingRequest(wallet.Id, 10m, Guid.NewGuid());
            var error = await Record.ExceptionAsync(() => Service(failure).FundAsync(request, CancellationToken.None));
            Assert.NotNull(error);
            Assert.IsType<InjectedWriteFailure>(error.GetBaseException());
            Assert.True(failure.Fired);
            await using (var db = new WalletDbContext(Options()))
            {
                Assert.Equal(0m, (await db.Wallets.SingleAsync(w => w.Id == wallet.Id)).Balance);
                Assert.False(await db.DevelopmentFundings.AnyAsync(f => f.WalletId == wallet.Id));
                Assert.False(await db.Movements.AnyAsync(m => m.WalletId == wallet.Id));
            }
            Assert.True((await Service().FundAsync(request, CancellationToken.None)).Created);
        }
        finally { await Cleanup(user.Id, wallet.Id); }
    }

    [AzureSqlFact]
    public async Task Inactive_owner_and_overflow_leave_all_records_unchanged()
    {
        var (user, wallet) = await Seed();
        try
        {
            var request = new DevelopmentFundingRequest(wallet.Id, MoneyRules.MaxBalance, Guid.NewGuid());
            await Service().FundAsync(request, CancellationToken.None);
            await Assert.ThrowsAsync<ArgumentException>(() => Service().FundAsync(request with { Amount = 0.01m, IdempotencyKey = Guid.NewGuid() }, CancellationToken.None));
            await using (var db = new WalletDbContext(Options()))
            {
                var owner = await db.Users.SingleAsync(u => u.Id == user.Id); owner.Disable(); await db.SaveChangesAsync();
            }
            Assert.Equal("USER_INACTIVE", (await Assert.ThrowsAsync<FundingRejectedException>(() =>
                Service().FundAsync(request, CancellationToken.None))).Code);
            await using (var db = new WalletDbContext(Options()))
            {
                Assert.Equal(MoneyRules.MaxBalance, (await db.Wallets.SingleAsync(w => w.Id == wallet.Id)).Balance);
                Assert.Equal(1, await db.DevelopmentFundings.CountAsync(f => f.WalletId == wallet.Id));
                Assert.Equal(1, await db.Movements.CountAsync(m => m.WalletId == wallet.Id));
            }
        }
        finally { await Cleanup(user.Id, wallet.Id); }
    }

    [AzureSqlFact]
    public async Task Lost_confirmation_after_commit_is_recovered_by_same_key_without_second_credit()
    {
        var (user, wallet) = await Seed();
        try
        {
            var request = new DevelopmentFundingRequest(wallet.Id, 10m, Guid.NewGuid());
            var failure = new FailAfterCommit();
            var error = await Record.ExceptionAsync(() => Service(failure).FundAsync(request, CancellationToken.None));
            Assert.NotNull(error);
            Assert.IsType<InjectedCommitFailure>(error.GetBaseException());
            Assert.True(failure.Fired);
            var recovered = await Service().FundAsync(request, CancellationToken.None);
            Assert.False(recovered.Created);
            Assert.Equal(10m, recovered.BalanceAfter);
            await using var db = new WalletDbContext(Options());
            Assert.Equal(10m, (await db.Wallets.SingleAsync(w => w.Id == wallet.Id)).Balance);
            Assert.Equal(1, await db.DevelopmentFundings.CountAsync(f => f.WalletId == wallet.Id));
            Assert.Equal(1, await db.Movements.CountAsync(m => m.WalletId == wallet.Id));
        }
        finally { await Cleanup(user.Id, wallet.Id); }
    }

    private sealed class InjectedCommitFailure : Exception { }
    private sealed class FailAfterCommit : DbTransactionInterceptor
    {
        internal bool Fired;
        public override Task TransactionCommittedAsync(DbTransaction transaction,
            TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            Fired = true;
            throw new InjectedCommitFailure();
        }
    }

    private sealed class InjectedWriteFailure : Exception { }

    private sealed class FailOnSecondWrite : DbCommandInterceptor
    {
        private int _writes;
        internal bool Fired;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if ((command.CommandText.Contains("INSERT INTO", StringComparison.Ordinal)
                || command.CommandText.Contains("UPDATE [Wallets]", StringComparison.Ordinal)) && ++_writes == 2)
            {
                Fired = true;
                throw new InjectedWriteFailure();
            }
            return ValueTask.FromResult(result);
        }
    }
}
