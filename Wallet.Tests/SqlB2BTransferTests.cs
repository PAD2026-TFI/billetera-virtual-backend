using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Wallet.Application;
using Wallet.Domain;
using Wallet.Infrastructure;
using Xunit;
using DomainWallet = Wallet.Domain.Wallet;

namespace Wallet.Tests;

[Collection("SqlIntegration")]
[Trait("Category", "Sql")]
public sealed class SqlB2BTransferTests
{
    private const string Endpoint = "/api/v1/integrations/transfers";

    [AzureSqlFact]
    public async Task Seller_key_transfers_from_the_same_wallet_used_for_collections()
    {
        var connection = Environment.GetEnvironmentVariable("WALLET_TEST_CONNECTION")!;
        var options = new DbContextOptionsBuilder<WalletDbContext>().UseSqlServer(connection).Options;
        await using var host = new IdentityTestHost(connection);
        using var sellerSession = host.Client();
        using var integration = host.Client();
        var suffix = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        var seller = User.Create("Vendedor", "Prueba", $"seller-{suffix}@example.com", now);
        seller.SetAccountType("merchant");
        var recipient = User.Create("Cliente", "Prueba", $"recipient-{suffix}@example.com", now);
        var origin = DomainWallet.Create(seller.Id, now);
        var destination = DomainWallet.Create(recipient.Id, now);
        seller.LinkWallet(origin.Id);
        origin.Credit(25m);

        await using var db = new WalletDbContext(options);
        db.Users.AddRange(seller, recipient);
        db.Wallets.AddRange(origin, destination);
        await db.SaveChangesAsync();

        try
        {
            const string integrationEndpoint = "/api/v1/me/merchant/integration";
            sellerSession.DefaultRequestHeaders.Authorization = new("Bearer", host.Token(seller));
            using var issue = await sellerSession.PostAsJsonAsync(integrationEndpoint, new { name = "Comercio QA" });
            Assert.Equal(HttpStatusCode.OK, issue.StatusCode);
            var key = (await issue.Content.ReadFromJsonAsync<MerchantIntegrationKeyResponse>())!;
            Assert.Equal(origin.Id, key.Integration.WalletId);
            integration.DefaultRequestHeaders.Add("X-Api-Key", key.ApiKey);

            integration.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
            using var byAlias = await integration.PostAsJsonAsync(Endpoint, new { destination.Alias, amount = 10m });
            Assert.Equal(HttpStatusCode.Created, byAlias.StatusCode);
            var aliasTransfer = (await byAlias.Content.ReadFromJsonAsync<TransferResponse>())!;
            Assert.Equal(origin.Id, aliasTransfer.SourceWalletId);
            Assert.Equal(destination.Id, aliasTransfer.DestinationWalletId);

            integration.DefaultRequestHeaders.Remove("Idempotency-Key");
            integration.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
            using var byCvu = await integration.PostAsJsonAsync(Endpoint, new { destination.Cvu, amount = 15m });
            Assert.Equal(HttpStatusCode.Created, byCvu.StatusCode);
            var cvuTransfer = (await byCvu.Content.ReadFromJsonAsync<TransferResponse>())!;
            Assert.Equal(origin.Id, cvuTransfer.SourceWalletId);
            Assert.Equal(destination.Id, cvuTransfer.DestinationWalletId);
            Assert.Equal(0m, (await db.Wallets.AsNoTracking().SingleAsync(w => w.Id == origin.Id)).Balance);
            Assert.Equal(25m, (await db.Wallets.AsNoTracking().SingleAsync(w => w.Id == destination.Id)).Balance);

            using var revoke = await sellerSession.DeleteAsync(integrationEndpoint);
            Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);
            using var revoked = await integration.PostAsJsonAsync(Endpoint, new { destination.Cvu, amount = 15m });
            Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
        }
        finally
        {
            await using var cleanup = new WalletDbContext(options);
            var ids = new[] { origin.Id, destination.Id };
            await cleanup.Movements.Where(m => ids.Contains(m.WalletId)).ExecuteDeleteAsync();
            await cleanup.Transfers.Where(t => t.SourceWalletId == origin.Id).ExecuteDeleteAsync();
            await cleanup.Wallets.Where(w => ids.Contains(w.Id)).ExecuteDeleteAsync();
            cleanup.Users.RemoveRange(await cleanup.Users.Where(u => u.Id == seller.Id || u.Id == recipient.Id).ToListAsync());
            await cleanup.SaveChangesAsync();
        }
    }

    [AzureSqlFact]
    public async Task External_client_can_transfer_by_alias_or_cvu_and_retry_by_wallet_id()
    {
        var connection = Environment.GetEnvironmentVariable("WALLET_TEST_CONNECTION")!;
        var options = new DbContextOptionsBuilder<WalletDbContext>().UseSqlServer(connection).Options;
        await using var host = new IdentityTestHost(connection);
        using var client = host.Client();
        var suffix = Guid.NewGuid().ToString("N");
        using var registration = await client.PostAsJsonAsync("/api/v1/integrations/register",
            new RegisterExternalClientRequest("Comercio SQL", $"b2b-{suffix}@example.com", null));
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        var merchant = (await registration.Content.ReadFromJsonAsync<RegisterExternalClientResponse>())!;
        var user = User.Create("Cliente", "Prueba", $"cliente-{suffix}@example.com", DateTimeOffset.UtcNow);
        var destination = DomainWallet.Create(user.Id, DateTimeOffset.UtcNow);

        await using var db = new WalletDbContext(options);
        db.Users.Add(user);
        db.Wallets.Add(destination);
        await db.SaveChangesAsync();

        try
        {
            var funding = new DevelopmentFundingService(new SqlDevelopmentFundingStore(options, TimeProvider.System));
            await funding.FundAsync(new(merchant.WalletId, 1000m, Guid.NewGuid()), default);
            client.DefaultRequestHeaders.Add("X-Api-Key", merchant.ApiKey);
            client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());

            using var ambiguous = await client.PostAsJsonAsync(Endpoint, new { destination.Alias, destination.Cvu, amount = 100m });
            Assert.Equal(HttpStatusCode.BadRequest, ambiguous.StatusCode);
            using var invalidCvu = await client.PostAsJsonAsync(Endpoint, new { cvu = "123", amount = 100m });
            Assert.Equal(HttpStatusCode.BadRequest, invalidCvu.StatusCode);
            using var missing = await client.PostAsJsonAsync(Endpoint, new { alias = $"missing.{suffix[..8]}", amount = 100m });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, missing.StatusCode);

            using var byAlias = await client.PostAsJsonAsync(Endpoint, new { alias = destination.Alias.ToUpperInvariant(), amount = 100m });
            Assert.Equal(HttpStatusCode.Created, byAlias.StatusCode);
            var transfer = (await byAlias.Content.ReadFromJsonAsync<TransferResponse>())!;
            Assert.Equal(destination.Id, transfer.DestinationWalletId);

            using var replay = await client.PostAsJsonAsync(Endpoint, new { destinationWalletId = destination.Id, amount = 100m });
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.Equal("true", replay.Headers.GetValues("Idempotency-Replayed").Single());
            Assert.Equal(transfer.Id, (await replay.Content.ReadFromJsonAsync<TransferResponse>())!.Id);

            client.DefaultRequestHeaders.Remove("Idempotency-Key");
            client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
            using var byCvu = await client.PostAsJsonAsync(Endpoint, new { destination.Cvu, amount = 150m });
            Assert.Equal(HttpStatusCode.Created, byCvu.StatusCode);
            Assert.Equal(destination.Id, (await byCvu.Content.ReadFromJsonAsync<TransferResponse>())!.DestinationWalletId);

            Assert.Equal(750m, (await db.Wallets.AsNoTracking().SingleAsync(w => w.Id == merchant.WalletId)).Balance);
            Assert.Equal(250m, (await db.Wallets.AsNoTracking().SingleAsync(w => w.Id == destination.Id)).Balance);
            Assert.Equal(2, await db.Transfers.CountAsync(t => t.SourceWalletId == merchant.WalletId));
        }
        finally
        {
            await using var cleanup = new WalletDbContext(options);
            var ids = new[] { merchant.WalletId, destination.Id };
            await cleanup.Movements.Where(m => ids.Contains(m.WalletId)).ExecuteDeleteAsync();
            await cleanup.Transfers.Where(t => t.SourceWalletId == merchant.WalletId).ExecuteDeleteAsync();
            await cleanup.DevelopmentFundings.Where(f => f.WalletId == merchant.WalletId).ExecuteDeleteAsync();
            await cleanup.Wallets.Where(w => ids.Contains(w.Id)).ExecuteDeleteAsync();
            cleanup.Users.Remove(await cleanup.Users.SingleAsync(u => u.Id == user.Id));
            cleanup.ExternalClients.Remove(await cleanup.ExternalClients.SingleAsync(c => c.Id == merchant.Id));
            await cleanup.SaveChangesAsync();
        }
    }
}
