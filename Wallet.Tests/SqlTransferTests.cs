using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Wallet.Application;
using Wallet.Domain;
using Wallet.Infrastructure;
using Xunit;
using DomainWallet = Wallet.Domain.Wallet;

namespace Wallet.Tests;

[Collection("SqlIntegration")]
[Trait("Category", "Sql")]
public sealed class SqlTransferTests
{
    private static DbContextOptions<WalletDbContext> Options(params IInterceptor[] interceptors) =>
        new DbContextOptionsBuilder<WalletDbContext>()
            .UseSqlServer(Environment.GetEnvironmentVariable("WALLET_TEST_CONNECTION")!, sql => sql.MaxBatchSize(1))
            .AddInterceptors(interceptors).Options;
    private static Task<TransferExecution> Execute(Guid userId, Guid destination, decimal amount, Guid key, params IInterceptor[] interceptors) =>
        new SqlTransferStore(Options(interceptors), TimeProvider.System).ExecuteAsync(
            new(userId, destination, amount, key), TransferService.Fingerprint(destination, amount), CancellationToken.None);

    internal sealed class Pair : IAsyncDisposable
    {
        public User Sender { get; } = NewUser();
        public User Receiver { get; } = NewUser();
        public DomainWallet Source { get; }
        public DomainWallet Destination { get; }
        private Pair() { Source = DomainWallet.Create(Sender.Id, DateTimeOffset.UtcNow); Destination = DomainWallet.Create(Receiver.Id, DateTimeOffset.UtcNow); }
        private static User NewUser()
        {
            var user = User.Create("Transfer", "SQL", $"tx-{Guid.NewGuid():N}@example.com", DateTimeOffset.UtcNow);
            user.SetPasswordHash(new IdentityPasswordService().Hash(user, IdentityTestHost.Password)); return user;
        }
        public static async Task<Pair> Create(decimal source = 1000m, decimal destination = 0m)
        {
            var pair = new Pair();
            await using var db = new WalletDbContext(Options());
            db.Users.AddRange(pair.Sender, pair.Receiver); db.Wallets.AddRange(pair.Source, pair.Destination);
            await db.SaveChangesAsync();
            // Preparo fondos usando el caso de uso real de TECH-001, con sus movimientos.
            var funding = new DevelopmentFundingService(new SqlDevelopmentFundingStore(Options(), TimeProvider.System));
            if (source > 0) await funding.FundAsync(new(pair.Source.Id, source, Guid.NewGuid()), CancellationToken.None);
            if (destination > 0) await funding.FundAsync(new(pair.Destination.Id, destination, Guid.NewGuid()), CancellationToken.None);
            return pair;
        }
        public async ValueTask DisposeAsync()
        {
            await using var db = new WalletDbContext(Options());
            var ids = new[] { Source.Id, Destination.Id };
            await db.Movements.Where(m => ids.Contains(m.WalletId)).ExecuteDeleteAsync();
            await db.Transfers.Where(t => ids.Contains(t.SourceWalletId) || ids.Contains(t.DestinationWalletId)).ExecuteDeleteAsync();
            await db.DevelopmentFundings.Where(f => ids.Contains(f.WalletId)).ExecuteDeleteAsync();
            await db.Wallets.Where(w => ids.Contains(w.Id)).ExecuteDeleteAsync();
            await db.Users.Where(u => u.Id == Sender.Id || u.Id == Receiver.Id).ExecuteDeleteAsync();
        }
    }

    [AzureSqlFact]
    public async Task HTTP_transfer_repetition_conflict_and_replay_without_original_funds_follow_contract()
    {
        await using var pair = await Pair.Create();
        await using var host = new IdentityTestHost(Environment.GetEnvironmentVariable("WALLET_TEST_CONNECTION"));
        using var client = host.Client(); client.DefaultRequestHeaders.Authorization = new("Bearer", host.Token(pair.Sender));
        var key = Guid.NewGuid(); client.DefaultRequestHeaders.Add("Idempotency-Key", key.ToString());
        var payload = new { destinationWalletId = pair.Destination.Id, amount = 300m };
        using var created = await client.PostAsJsonAsync("/api/transfers", payload);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var result = (await created.Content.ReadFromJsonAsync<TransferResponse>())!;
        Assert.Equal("Completed", result.Status); Assert.Equal("ARS", result.Currency);
        await Execute(pair.Sender.Id, pair.Destination.Id, 700m, Guid.NewGuid());
        using var repeated = await client.PostAsJsonAsync("/api/transfers", payload);
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal(result, await repeated.Content.ReadFromJsonAsync<TransferResponse>());
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/transfers", new
            { destinationWalletId = pair.Destination.Id, amount = 301m })).StatusCode);
        await using var db = new WalletDbContext(Options());
        Assert.Equal(0m, (await db.Wallets.SingleAsync(w => w.Id == pair.Source.Id)).Balance);
        Assert.Equal(1000m, (await db.Wallets.SingleAsync(w => w.Id == pair.Destination.Id)).Balance);
        var movements = await db.Movements.Where(m => m.TransferId == result.Id).ToListAsync();
        Assert.Equal(2, movements.Count);
        Assert.Contains(movements, m => m.Type == MovementType.Debit && m.WalletId == pair.Source.Id && m.BalanceAfter == 700m);
        Assert.Contains(movements, m => m.Type == MovementType.Credit && m.WalletId == pair.Destination.Id && m.BalanceAfter == 300m);
        Assert.All(movements, m => Assert.Equal(300m, m.Amount));
    }

    [AzureSqlFact]
    public async Task Concurrent_duplicates_transfer_once_and_competing_debits_cannot_overspend()
    {
        await using var pair = await Pair.Create();
        var key = Guid.NewGuid();
        var duplicates = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Execute(pair.Sender.Id, pair.Destination.Id, 300m, key)));
        Assert.Single(duplicates, t => t.Created); Assert.Single(duplicates.Select(t => t.Transfer.Id).Distinct());
        var attempts = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            try { await Execute(pair.Sender.Id, pair.Destination.Id, 300m, Guid.NewGuid()); return "OK"; }
            catch (TransferRejectedException e) { return e.Code; }
        }));
        Assert.Equal(2, attempts.Count(x => x == "OK"));
        Assert.Equal(4, attempts.Count(x => x == "INSUFFICIENT_FUNDS"));
        await using var db = new WalletDbContext(Options());
        var balances = await db.Wallets.Where(w => w.Id == pair.Source.Id || w.Id == pair.Destination.Id).Select(w => w.Balance).ToListAsync();
        Assert.Equal(1000m, balances.Sum()); Assert.Contains(100m, balances); Assert.Contains(900m, balances);
        Assert.Equal(3, await db.Transfers.CountAsync(t => t.SourceWalletId == pair.Source.Id));
        Assert.Equal(6, await db.Movements.CountAsync(m => m.TransferId != null && (m.WalletId == pair.Source.Id || m.WalletId == pair.Destination.Id)));
    }

    [AzureSqlFact]
    public async Task Opposite_transfers_use_same_lock_order_and_key_is_scoped_to_source()
    {
        await using var pair = await Pair.Create(1000m, 1000m);
        var key = Guid.NewGuid();
        var results = await Task.WhenAll(Execute(pair.Sender.Id, pair.Destination.Id, 200m, key),
            Execute(pair.Receiver.Id, pair.Source.Id, 200m, key));
        Assert.All(results, r => Assert.True(r.Created));
        await using var db = new WalletDbContext(Options());
        var balances = await db.Wallets.Where(w => w.Id == pair.Source.Id || w.Id == pair.Destination.Id).Select(w => w.Balance).ToListAsync();
        Assert.All(balances, balance => Assert.Equal(1000m, balance));
    }

    [AzureSqlFact]
    public async Task Failure_before_second_wallet_update_rolls_back_first_debit_or_credit_and_all_records()
    {
        await using var pair = await Pair.Create();
        var key = Guid.NewGuid(); var failure = new FailBeforeSecondWalletUpdate();
        var error = await Record.ExceptionAsync(() => Execute(pair.Sender.Id, pair.Destination.Id, 300m, key, failure));
        Assert.NotNull(error); Assert.IsType<InjectedFailure>(error.GetBaseException()); Assert.True(failure.Fired);
        await using (var db = new WalletDbContext(Options()))
        {
            Assert.Equal(1000m, (await db.Wallets.SingleAsync(w => w.Id == pair.Source.Id)).Balance);
            Assert.Equal(0m, (await db.Wallets.SingleAsync(w => w.Id == pair.Destination.Id)).Balance);
            Assert.False(await db.Transfers.AnyAsync(t => t.SourceWalletId == pair.Source.Id));
            Assert.False(await db.Movements.AnyAsync(m => m.TransferId != null && (m.WalletId == pair.Source.Id || m.WalletId == pair.Destination.Id)));
        }
        Assert.True((await Execute(pair.Sender.Id, pair.Destination.Id, 300m, key)).Created);
    }

    [AzureSqlFact]
    public async Task Lost_commit_confirmation_is_recovered_without_second_transfer()
    {
        await using var pair = await Pair.Create();
        var key = Guid.NewGuid(); var failure = new FailAfterCommit();
        var error = await Record.ExceptionAsync(() => Execute(pair.Sender.Id, pair.Destination.Id, 300m, key, failure));
        Assert.NotNull(error); Assert.IsType<InjectedFailure>(error.GetBaseException()); Assert.True(failure.Fired);
        var recovered = await Execute(pair.Sender.Id, pair.Destination.Id, 300m, key);
        Assert.False(recovered.Created);
        await using var db = new WalletDbContext(Options());
        Assert.Equal(700m, (await db.Wallets.SingleAsync(w => w.Id == pair.Source.Id)).Balance);
        Assert.Equal(300m, (await db.Wallets.SingleAsync(w => w.Id == pair.Destination.Id)).Balance);
        Assert.Equal(1, await db.Transfers.CountAsync(t => t.SourceWalletId == pair.Source.Id));
    }

    [AzureSqlFact]
    public async Task Self_missing_inactive_and_overflow_destinations_do_not_modify_balances()
    {
        await using var pair = await Pair.Create(1000m, MoneyRules.MaxBalance);
        Assert.Equal("SAME_WALLET", (await Assert.ThrowsAsync<TransferRejectedException>(() =>
            Execute(pair.Sender.Id, pair.Source.Id, 1m, Guid.NewGuid()))).Code);
        Assert.Equal("DESTINATION_UNAVAILABLE", (await Assert.ThrowsAsync<TransferRejectedException>(() =>
            Execute(pair.Sender.Id, Guid.NewGuid(), 1m, Guid.NewGuid()))).Code);
        Assert.Equal("DESTINATION_BALANCE_LIMIT", (await Assert.ThrowsAsync<TransferRejectedException>(() =>
            Execute(pair.Sender.Id, pair.Destination.Id, 1m, Guid.NewGuid()))).Code);
        await using (var db = new WalletDbContext(Options()))
        {
            var receiver = await db.Users.SingleAsync(u => u.Id == pair.Receiver.Id); receiver.Disable(); await db.SaveChangesAsync();
        }
        Assert.Equal("DESTINATION_UNAVAILABLE", (await Assert.ThrowsAsync<TransferRejectedException>(() =>
            Execute(pair.Sender.Id, pair.Destination.Id, 1m, Guid.NewGuid()))).Code);
        await using (var db = new WalletDbContext(Options()))
        {
            Assert.Equal(1000m, (await db.Wallets.SingleAsync(w => w.Id == pair.Source.Id)).Balance);
            Assert.Equal(MoneyRules.MaxBalance, (await db.Wallets.SingleAsync(w => w.Id == pair.Destination.Id)).Balance);
            Assert.False(await db.Transfers.AnyAsync(t => t.SourceWalletId == pair.Source.Id));
        }
    }

    [AzureSqlFact]
    public async Task Concurrent_development_funding_and_transfer_preserve_all_changes()
    {
        await using var pair = await Pair.Create();
        var funding = new DevelopmentFundingService(new SqlDevelopmentFundingStore(Options(), TimeProvider.System));
        var transfer = Execute(pair.Sender.Id, pair.Destination.Id, 300m, Guid.NewGuid());
        var load = funding.FundAsync(new(pair.Source.Id, 50m, Guid.NewGuid()), CancellationToken.None);
        await Task.WhenAll(transfer, load);
        await using var db = new WalletDbContext(Options());
        Assert.Equal(750m, (await db.Wallets.SingleAsync(w => w.Id == pair.Source.Id)).Balance);
        Assert.Equal(300m, (await db.Wallets.SingleAsync(w => w.Id == pair.Destination.Id)).Balance);
    }

    private sealed class InjectedFailure : Exception { }
    private sealed class FailBeforeSecondWalletUpdate : DbCommandInterceptor
    {
        private int _updates; internal bool Fired;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("UPDATE [Wallets]", StringComparison.Ordinal) && ++_updates == 2)
            { Fired = true; throw new InjectedFailure(); }
            return ValueTask.FromResult(result);
        }
    }
    private sealed class FailAfterCommit : DbTransactionInterceptor
    {
        internal bool Fired;
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        { Fired = true; throw new InjectedFailure(); }
    }
}
