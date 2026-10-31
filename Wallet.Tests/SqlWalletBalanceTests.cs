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
public sealed class SqlWalletBalanceTests
{
    private static WalletDbContext NewDb() => new(new DbContextOptionsBuilder<WalletDbContext>()
        .UseSqlServer(Environment.GetEnvironmentVariable("WALLET_TEST_CONNECTION")!).Options);

    [AzureSqlFact]
    public async Task Reading_from_new_hosts_returns_persisted_balance_without_changing_SQL_row_version()
    {
        var user = User.Create("Read", "Wallet", $"wallet-read-{Guid.NewGuid():N}@example.com", DateTimeOffset.UtcNow);
        user.SetPasswordHash(new IdentityPasswordService().Hash(user, IdentityTestHost.Password));
        var wallet = DomainWallet.Create(user.Id, DateTimeOffset.UtcNow);
        // Sólo fixture: esta historia no implementa cargas de saldo.
        typeof(DomainWallet).GetProperty(nameof(DomainWallet.Balance))!.SetValue(wallet, Wallet.Domain.Money.From(12345.67m));
        try
        {
            await using (var db = NewDb())
            {
                db.Users.Add(user);
                db.Wallets.Add(wallet);
                await db.SaveChangesAsync();
            }
            byte[] version;
            DateTimeOffset createdAt;
            await using (var db = NewDb())
            {
                var stored = await db.Wallets.AsNoTracking().SingleAsync(w => w.Id == wallet.Id);
                version = stored.RowVersion;
                createdAt = stored.CreatedAt;
            }
            for (var i = 0; i < 2; i++)
            {
                await using var host = new IdentityTestHost(Environment.GetEnvironmentVariable("WALLET_TEST_CONNECTION"));
                using var client = host.Client();
                client.DefaultRequestHeaders.Authorization = new("Bearer", host.Token(user));
                using var response = await client.GetAsync("/api/wallets/me");
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal(new WalletResponse(wallet.Id, 12345.67m, "ARS", createdAt),
                    await response.Content.ReadFromJsonAsync<WalletResponse>());
            }
            await using (var db = NewDb())
            {
                var stored = await db.Wallets.AsNoTracking().SingleAsync(w => w.UserId == user.Id);
                Assert.Equal(version, stored.RowVersion);
                Assert.Equal(12345.67m, stored.Balance);
                Assert.Equal(1, await db.Wallets.CountAsync(w => w.UserId == user.Id));
            }
        }
        finally
        {
            await using var db = NewDb();
            await db.Wallets.Where(w => w.UserId == user.Id).ExecuteDeleteAsync();
            await db.Users.Where(u => u.Id == user.Id).ExecuteDeleteAsync();
        }
    }
}
