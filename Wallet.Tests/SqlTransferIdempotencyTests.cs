using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Wallet.Application;
using Wallet.Infrastructure;
using Xunit;

namespace Wallet.Tests;

[Trait("Category", "Sql")]
public sealed class SqlTransferIdempotencyTests
{
    [AzureSqlFact]
    public async Task Restart_and_equivalent_amount_recover_original_operation_even_after_receiver_is_disabled()
    {
        await using var pair = await SqlTransferTests.Pair.Create();
        var key = Guid.NewGuid(); TransferResponse original;
        await using (var first = new IdentityTestHost(Environment.GetEnvironmentVariable("WALLET_TEST_CONNECTION")))
        {
            using var client = first.Client(); client.DefaultRequestHeaders.Authorization = new("Bearer", first.Token(pair.Sender));
            client.DefaultRequestHeaders.Add("Idempotency-Key", key.ToString("D"));
            using var created = await client.PostAsJsonAsync("/api/transfers", new { destinationWalletId = pair.Destination.Id, amount = 300m });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            Assert.Equal("false", created.Headers.GetValues("Idempotency-Replayed").Single());
            original = (await created.Content.ReadFromJsonAsync<TransferResponse>())!;
        }
        var options = new DbContextOptionsBuilder<WalletDbContext>().UseSqlServer(Environment.GetEnvironmentVariable("WALLET_TEST_CONNECTION")!).Options;
        await using (var db = new WalletDbContext(options))
        {
            var receiver = await db.Users.SingleAsync(u => u.Id == pair.Receiver.Id); receiver.Disable(); await db.SaveChangesAsync();
        }
        await using var restarted = new IdentityTestHost(Environment.GetEnvironmentVariable("WALLET_TEST_CONNECTION"));
        using var retry = restarted.Client(); retry.DefaultRequestHeaders.Authorization = new("Bearer", restarted.Token(pair.Sender));
        retry.DefaultRequestHeaders.Add("Idempotency-Key", key.ToString("N"));
        using var repeated = await retry.PostAsJsonAsync("/api/transfers", new { destinationWalletId = pair.Destination.Id, amount = 300.00m });
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal("true", repeated.Headers.GetValues("Idempotency-Replayed").Single());
        Assert.Equal(original, await repeated.Content.ReadFromJsonAsync<TransferResponse>());
        using var conflict = await retry.PostAsJsonAsync("/api/transfers", new { destinationWalletId = pair.Source.Id, amount = 300m });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        await using var verify = new WalletDbContext(options);
        Assert.Equal(700m, (await verify.Wallets.SingleAsync(w => w.Id == pair.Source.Id)).Balance);
        Assert.Equal(300m, (await verify.Wallets.SingleAsync(w => w.Id == pair.Destination.Id)).Balance);
        Assert.Equal(1, await verify.Transfers.CountAsync(t => t.SourceWalletId == pair.Source.Id));
        Assert.Equal(2, await verify.Movements.CountAsync(m => m.TransferId == original.Id));
    }

    [AzureSqlFact]
    public async Task Rejected_request_does_not_consume_key_and_can_succeed_after_funding()
    {
        await using var pair = await SqlTransferTests.Pair.Create(0m);
        var options = new DbContextOptionsBuilder<WalletDbContext>().UseSqlServer(Environment.GetEnvironmentVariable("WALLET_TEST_CONNECTION")!).Options;
        var command = new TransferCommand(pair.Sender.Id, pair.Destination.Id, 10m, Guid.NewGuid());
        var hash = TransferService.Fingerprint(pair.Destination.Id, 10m);
        var store = new SqlTransferStore(options, TimeProvider.System);
        Assert.Equal("INSUFFICIENT_FUNDS", (await Assert.ThrowsAsync<TransferRejectedException>(() => store.ExecuteAsync(command, hash, CancellationToken.None))).Code);
        await new DevelopmentFundingService(new SqlDevelopmentFundingStore(options, TimeProvider.System))
            .FundAsync(new(pair.Source.Id, 10m, Guid.NewGuid()), CancellationToken.None);
        Assert.True((await store.ExecuteAsync(command, hash, CancellationToken.None)).Created);
        Assert.False((await store.ExecuteAsync(command, hash, CancellationToken.None)).Created);
    }
}
