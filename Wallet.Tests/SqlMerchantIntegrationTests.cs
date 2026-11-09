using Microsoft.EntityFrameworkCore;
using Wallet.Application;
using Wallet.Domain;
using Wallet.Infrastructure;
using Xunit;

namespace Wallet.Tests;

[Collection("SqlIntegration")]
[Trait("Category", "Sql")]
public sealed class SqlMerchantIntegrationTests
{
    private static WalletDbContext NewDb() => new(new DbContextOptionsBuilder<WalletDbContext>()
        .UseSqlServer(Environment.GetEnvironmentVariable("WALLET_TEST_CONNECTION")!).Options);

    [AzureSqlFact]
    public async Task Keys_persist_as_hashes_and_competing_issuance_cannot_both_succeed()
    {
        var user = User.Create("Test", "Seller", $"integration-{Guid.NewGuid():N}@example.com", DateTimeOffset.UtcNow);
        user.SetAccountType("merchant"); user.SetPasswordHash("test-only-password-hash");
        await using var setup = NewDb(); setup.Users.Add(user); await setup.SaveChangesAsync();
        try
        {
            await using var first = NewDb(); await using var second = NewDb();
            var a = (await first.Users.FindAsync(user.Id))!; var b = (await second.Users.FindAsync(user.Id))!;
            var firstHash = GatewayService.HashApiKey("test-first-key");
            a.SetIntegrationKey("Tienda", firstHash, "padi_first", DateTimeOffset.UtcNow);
            b.SetIntegrationKey("Tienda", GatewayService.HashApiKey("test-second-key"), "padi_second", DateTimeOffset.UtcNow);
            await first.SaveChangesAsync();
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
            await using var read = NewDb();
            var repository = new SqlUserRepository(read);
            Assert.Equal(user.Id, (await repository.FindByIntegrationKeyHashAsync(firstHash, default))!.Id);
            Assert.Null(await repository.FindByIntegrationKeyHashAsync(GatewayService.HashApiKey("test-second-key"), default));
        }
        finally
        {
            await using var cleanup = NewDb();
            var persisted = (await cleanup.Users.FindAsync(user.Id))!; cleanup.Users.Remove(persisted); await cleanup.SaveChangesAsync();
        }
    }
}
