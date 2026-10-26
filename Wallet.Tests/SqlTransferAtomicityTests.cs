using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Wallet.Application;
using Wallet.Domain;
using Wallet.Infrastructure;
using Xunit;
using DomainWallet = Wallet.Domain.Wallet;

namespace Wallet.Tests;

[Trait("Category", "Sql")]
public sealed class SqlTransferAtomicityTests
{
    private static DbContextOptions<WalletDbContext> Options(params IInterceptor[] interceptors) =>
        new DbContextOptionsBuilder<WalletDbContext>().UseSqlServer(Environment.GetEnvironmentVariable("WALLET_TEST_CONNECTION")!,
            sql => sql.MaxBatchSize(1)).AddInterceptors(interceptors).Options;

    private sealed class Fixture : IAsyncDisposable
    {
        public User Sender { get; private set; } = null!;
        public Guid SourceId { get; private set; }
        public Guid DestinationId { get; private set; }
        private Guid _receiverId;
        public static async Task<Fixture> Create()
        {
            var users = Enumerable.Range(0, 2).Select(_ =>
            {
                var user = User.Create("Atomic", "SQL", $"atomic-{Guid.NewGuid():N}@example.com", DateTimeOffset.UtcNow);
                user.SetPasswordHash(new IdentityPasswordService().Hash(user, IdentityTestHost.Password)); return user;
            }).ToArray();
            var wallets = users.Select(u => DomainWallet.Create(u.Id, DateTimeOffset.UtcNow)).ToArray();
            await using (var db = new WalletDbContext(Options()))
            { db.Users.AddRange(users); db.Wallets.AddRange(wallets); await db.SaveChangesAsync(); }
            await new DevelopmentFundingService(new SqlDevelopmentFundingStore(Options(), TimeProvider.System))
                .FundAsync(new(wallets[0].Id, 100m, Guid.NewGuid()), CancellationToken.None);
            return new() { Sender = users[0], _receiverId = users[1].Id, SourceId = wallets[0].Id, DestinationId = wallets[1].Id };
        }
        public Task<TransferExecution> Transfer(Guid key, CancellationToken ct, params IInterceptor[] interceptors) =>
            new SqlTransferStore(Options(interceptors), TimeProvider.System).ExecuteAsync(new(Sender.Id, DestinationId, 20m, key),
                TransferService.Fingerprint(DestinationId, 20m), ct);
        public async Task AssertUnchanged()
        {
            await using var db = new WalletDbContext(Options());
            Assert.Equal(100m, (await db.Wallets.SingleAsync(w => w.Id == SourceId)).Balance);
            Assert.Equal(0m, (await db.Wallets.SingleAsync(w => w.Id == DestinationId)).Balance);
            Assert.False(await db.Transfers.AnyAsync(t => t.SourceWalletId == SourceId));
            Assert.False(await db.Movements.AnyAsync(m => m.TransferId != null && (m.WalletId == SourceId || m.WalletId == DestinationId)));
            Assert.Equal(1, await db.Movements.CountAsync(m => m.WalletId == SourceId && m.Source == MovementSource.DevelopmentFunding));
        }
        public async ValueTask DisposeAsync()
        {
            await using var db = new WalletDbContext(Options());
            await db.Movements.Where(m => m.WalletId == SourceId || m.WalletId == DestinationId).ExecuteDeleteAsync();
            await db.Transfers.Where(t => t.SourceWalletId == SourceId).ExecuteDeleteAsync();
            await db.DevelopmentFundings.Where(f => f.WalletId == SourceId).ExecuteDeleteAsync();
            await db.Wallets.Where(w => w.Id == SourceId || w.Id == DestinationId).ExecuteDeleteAsync();
            await db.Users.Where(u => u.Id == Sender.Id || u.Id == _receiverId).ExecuteDeleteAsync();
        }
    }

    [AzureSqlFact]
    public async Task Cancellation_after_first_wallet_write_rolls_back_all_changes_and_preserves_retry_key()
    {
        await using var fixture = await Fixture.Create(); using var cancellation = new CancellationTokenSource();
        var interceptor = new CancelAfterWalletWrite(cancellation); var key = Guid.NewGuid();
        var error = await Record.ExceptionAsync(() => fixture.Transfer(key, cancellation.Token, interceptor));
        Assert.NotNull(error); Assert.True(interceptor.Fired);
        Assert.True(cancellation.IsCancellationRequested);
        await fixture.AssertUnchanged();
        Assert.True((await fixture.Transfer(key, CancellationToken.None)).Created);
    }

    [AzureSqlFact]
    public async Task Failure_before_commit_rolls_back_balances_transfer_and_movements()
    {
        await using var fixture = await Fixture.Create(); var interceptor = new FailBeforeCommit(); var key = Guid.NewGuid();
        var error = await Record.ExceptionAsync(() => fixture.Transfer(key, CancellationToken.None, interceptor));
        Assert.NotNull(error); Assert.IsType<InjectedFailure>(error.GetBaseException()); Assert.True(interceptor.Fired);
        await fixture.AssertUnchanged(); Assert.True((await fixture.Transfer(key, CancellationToken.None)).Created);
    }

    [AzureSqlFact]
    public async Task Retries_are_bounded_and_a_fresh_attempt_can_confirm_only_once()
    {
        await using var fixture = await Fixture.Create(); var key = Guid.NewGuid(); var always = new ConcurrencyFailure(int.MaxValue);
        var error = await Assert.ThrowsAsync<TransferRejectedException>(() => fixture.Transfer(key, CancellationToken.None, always));
        Assert.Equal("CONCURRENCY_CONFLICT", error.Code); Assert.Equal(3, always.Calls);
        await fixture.AssertUnchanged();
        var once = new ConcurrencyFailure(1);
        Assert.True((await fixture.Transfer(key, CancellationToken.None, once)).Created); Assert.Equal(2, once.Calls);
        Assert.False((await fixture.Transfer(key, CancellationToken.None)).Created);
        await using var db = new WalletDbContext(Options());
        Assert.Equal(80m, (await db.Wallets.SingleAsync(w => w.Id == fixture.SourceId)).Balance);
        Assert.Equal(20m, (await db.Wallets.SingleAsync(w => w.Id == fixture.DestinationId)).Balance);
        Assert.Equal(1, await db.Transfers.CountAsync(t => t.SourceWalletId == fixture.SourceId));
    }
    private sealed class CancelAfterWalletWrite(CancellationTokenSource cancellation) : DbCommandInterceptor
    {
        internal bool Fired;
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (!Fired && command.CommandText.Contains("UPDATE [Wallets]", StringComparison.Ordinal))
            { Fired = true; cancellation.Cancel(); }
            return ValueTask.FromResult(result);
        }
    }
    private sealed class InjectedFailure : Exception { }
    private sealed class FailBeforeCommit : DbTransactionInterceptor
    {
        internal bool Fired;
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        { Fired = true; throw new InjectedFailure(); }
    }
    private sealed class ConcurrencyFailure(int failures) : SaveChangesInterceptor
    {
        internal int Calls;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (++Calls <= failures) throw new DbUpdateConcurrencyException("Conflicto inducido.");
            return ValueTask.FromResult(result);
        }
    }
}
