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
public sealed class SqlWalletCreationTests
{
    private static WalletDbContext NewDb() => new(new DbContextOptionsBuilder<WalletDbContext>()
        .UseSqlServer(Environment.GetEnvironmentVariable("WALLET_TEST_CONNECTION")!).Options);

    private static User NewUser()
    {
        var user = User.Create("Wallet", "SQL", $"wallet-{Guid.NewGuid():N}@example.com", DateTimeOffset.UtcNow);
        user.SetPasswordHash(new IdentityPasswordService().Hash(user, IdentityTestHost.Password));
        return user;
    }

    private static async Task Cleanup(Guid userId)
    {
        await using var db = NewDb();
        await db.Wallets.Where(w => w.UserId == userId).ExecuteDeleteAsync();
        await db.Users.Where(u => u.Id == userId).ExecuteDeleteAsync();
    }

    [AzureSqlFact]
    public async Task Concurrent_HTTP_requests_create_once_and_restarted_host_preserves_wallet_and_balance()
    {
        var user = NewUser();
        try
        {
            await using (var db = NewDb())
            {
                db.Users.Add(user);
                await db.SaveChangesAsync();
            }
            WalletResponse first;
            await using (var host = new IdentityTestHost(Environment.GetEnvironmentVariable("WALLET_TEST_CONNECTION")))
            {
                using var client = host.Client();
                client.DefaultRequestHeaders.Authorization = new("Bearer", host.Token(user));
                var replies = await Task.WhenAll(Enumerable.Range(0, 8)
                    .Select(_ => client.PostAsync("/api/wallets", null)));
                try
                {
                    Assert.Single(replies, r => r.StatusCode == HttpStatusCode.Created);
                    Assert.Equal(7, replies.Count(r => r.StatusCode == HttpStatusCode.OK));
                    var wallets = await Task.WhenAll(replies.Select(async r =>
                        (await r.Content.ReadFromJsonAsync<WalletResponse>())!));
                    Assert.Single(wallets.Select(w => w.Id).Distinct());
                    first = wallets[0];
                    Assert.All(wallets, w => { Assert.Equal(0m, w.Balance); Assert.Equal("ARS", w.Currency); });
                }
                finally { foreach (var reply in replies) reply.Dispose(); }
            }
            await using (var db = NewDb())
            {
                Assert.Equal(1, await db.Wallets.CountAsync(w => w.UserId == user.Id));
                // Sólo fixture: simulo saldo futuro sin introducir un endpoint de carga.
                await db.Wallets.Where(w => w.UserId == user.Id).ExecuteUpdateAsync(s => s.SetProperty(w => w.Balance, 125.50m));
            }
            // Otro host y otro DbContext: no reutilizo estado ni tracking del primer request.
            await using var restarted = new IdentityTestHost(Environment.GetEnvironmentVariable("WALLET_TEST_CONNECTION"));
            using var restartedClient = restarted.Client();
            restartedClient.DefaultRequestHeaders.Authorization = new("Bearer", restarted.Token(user));
            using var repeated = await restartedClient.PostAsync("/api/wallets", null);
            Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
            var stored = (await repeated.Content.ReadFromJsonAsync<WalletResponse>())!;
            Assert.Equal(first.Id, stored.Id);
            Assert.Equal(first.CreatedAt, stored.CreatedAt);
            Assert.Equal(125.50m, stored.Balance);
        }
        finally { await Cleanup(user.Id); }
    }

    [AzureSqlFact]
    public async Task SQL_enforces_unique_owner_foreign_key_ARS_nonnegative_balance_and_restricts_user_deletion()
    {
        var user = NewUser();
        try
        {
            await using (var db = NewDb())
            {
                db.Users.Add(user);
                db.Wallets.Add(DomainWallet.Create(user.Id, DateTimeOffset.UtcNow));
                await db.SaveChangesAsync();
            }
            await using (var duplicate = NewDb())
            {
                duplicate.Wallets.Add(DomainWallet.Create(user.Id, DateTimeOffset.UtcNow));
                await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
            }
            await using (var orphan = NewDb())
            {
                orphan.Wallets.Add(DomainWallet.Create(Guid.NewGuid(), DateTimeOffset.UtcNow));
                await Assert.ThrowsAsync<DbUpdateException>(() => orphan.SaveChangesAsync());
            }
            foreach (var (property, invalid) in new[]
            {
                (nameof(DomainWallet.Currency), (object)"USD"),
                (nameof(DomainWallet.Currency), (object)"ars"),
                (nameof(DomainWallet.Balance), (object)(-1m))
            })
            {
                await using var db = NewDb();
                var wallet = await db.Wallets.SingleAsync(w => w.UserId == user.Id);
                typeof(DomainWallet).GetProperty(property)!.SetValue(wallet, invalid);
                await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            }
            await using (var db = NewDb())
            {
                var owner = await db.Users.SingleAsync(u => u.Id == user.Id);
                db.Users.Remove(owner);
                await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            }
        }
        finally { await Cleanup(user.Id); }
    }
}
