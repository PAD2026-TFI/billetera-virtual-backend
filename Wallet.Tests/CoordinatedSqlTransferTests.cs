using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Wallet.Application;
using Wallet.Infrastructure;
using Xunit;

namespace Wallet.Tests;

[Collection("SqlIntegration")]
[Trait("Category", "Sql")]
public sealed class CoordinatedSqlTransferTests
{
    private static DbContextOptions<WalletDbContext> Options(params IInterceptor[] interceptors) =>
        new DbContextOptionsBuilder<WalletDbContext>()
            .UseSqlServer(Environment.GetEnvironmentVariable("WALLET_TEST_CONNECTION")!).AddInterceptors(interceptors).Options;

    [AzureSqlFact]
    public async Task Synchronized_duplicate_requests_create_exactly_one_transfer()
    {
        await using var pair = await SqlTransferTests.Pair.Create();
        var gate = new StartGate(8); var options = Options(gate); var key = Guid.NewGuid();
        var command = new TransferCommand(pair.Sender.Id, pair.Destination.Id, 300m, key);
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            new SqlTransferStore(options, TimeProvider.System).ExecuteAsync(command,
                TransferService.Fingerprint(pair.Destination.Id, 300m), CancellationToken.None)));
        Assert.Equal(8, gate.Arrivals); Assert.Single(results, r => r.Created);
        Assert.Single(results.Select(r => r.Transfer.Id).Distinct());
        await using var db = new WalletDbContext(Options());
        Assert.Equal(700m, (await db.Wallets.SingleAsync(w => w.Id == pair.Source.Id)).Balance);
        Assert.Equal(300m, (await db.Wallets.SingleAsync(w => w.Id == pair.Destination.Id)).Balance);
        Assert.Equal(1, await db.Transfers.CountAsync(t => t.SourceWalletId == pair.Source.Id));
        Assert.Equal(2, await db.Movements.CountAsync(m => m.TransferId == results[0].Transfer.Id));
    }

    [AzureSqlFact]
    public async Task Synchronized_debits_cannot_spend_same_funds_twice()
    {
        await using var pair = await SqlTransferTests.Pair.Create();
        var gate = new StartGate(4); var options = Options(gate);
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            try
            {
                await new SqlTransferStore(options, TimeProvider.System).ExecuteAsync(
                    new(pair.Sender.Id, pair.Destination.Id, 600m, Guid.NewGuid()),
                    TransferService.Fingerprint(pair.Destination.Id, 600m), CancellationToken.None);
                return "OK";
            }
            catch (TransferRejectedException e) { return e.Code; }
        }));
        Assert.Equal(4, gate.Arrivals); Assert.Single(outcomes, r => r == "OK");
        Assert.Equal(3, outcomes.Count(r => r == "INSUFFICIENT_FUNDS"));
        await using var db = new WalletDbContext(Options());
        Assert.Equal(400m, (await db.Wallets.SingleAsync(w => w.Id == pair.Source.Id)).Balance);
        Assert.Equal(600m, (await db.Wallets.SingleAsync(w => w.Id == pair.Destination.Id)).Balance);
        Assert.Equal(1, await db.Transfers.CountAsync(t => t.SourceWalletId == pair.Source.Id));
    }

    [AzureSqlFact]
    public async Task Inclusive_amount_boundaries_are_persisted_with_two_movements()
    {
        foreach (var amount in new[] { 0.01m, 780400m })
        {
            await using var pair = await SqlTransferTests.Pair.Create(amount);
            var result = await new SqlTransferStore(Options(), TimeProvider.System).ExecuteAsync(
                new(pair.Sender.Id, pair.Destination.Id, amount, Guid.NewGuid()),
                TransferService.Fingerprint(pair.Destination.Id, amount), CancellationToken.None);
            Assert.True(result.Created);
            await using var db = new WalletDbContext(Options());
            Assert.Equal(0m, (await db.Wallets.SingleAsync(w => w.Id == pair.Source.Id)).Balance);
            Assert.Equal(amount, (await db.Wallets.SingleAsync(w => w.Id == pair.Destination.Id)).Balance);
            var movements = await db.Movements.Where(m => m.TransferId == result.Transfer.Id).ToListAsync();
            Assert.Equal(2, movements.Count); Assert.All(movements, m => Assert.Equal(amount, m.Amount));
        }
    }

    private sealed class StartGate(int participants) : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _seen;
        public int Arrivals => Math.Min(Volatile.Read(ref _seen), participants);
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            // Antes de ejecutar el SELECT inicial, todavía no hay locks de billeteras.
            if (command.CommandText.Contains("FROM [Wallets]", StringComparison.Ordinal)
                && command.CommandText.Contains("[UserId]", StringComparison.Ordinal)
                && !command.CommandText.Contains("UPDLOCK", StringComparison.Ordinal))
            {
                var arrival = Interlocked.Increment(ref _seen);
                if (arrival == participants) _ready.TrySetResult();
                if (arrival <= participants) await _ready.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            }
            return result;
        }
    }
}
