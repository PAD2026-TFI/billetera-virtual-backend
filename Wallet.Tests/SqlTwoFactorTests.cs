using Microsoft.EntityFrameworkCore;
using Wallet.Application;
using Wallet.Domain;
using Wallet.Infrastructure;
using Xunit;

namespace Wallet.Tests;

[Collection("SqlIntegration")]
[Trait("Category", "Sql")]
public sealed class SqlTwoFactorTests
{
    [AzureSqlFact]
    public async Task Only_one_concurrent_use_of_a_totp_step_is_persisted()
    {
        var options = new DbContextOptionsBuilder<WalletDbContext>()
            .UseSqlServer(Environment.GetEnvironmentVariable("WALLET_TEST_CONNECTION")!).Options;
        var now = DateTimeOffset.UtcNow;
        var user = User.Create("Ana", "Prueba", $"totp-{Guid.NewGuid():N}@example.com", now);
        user.SetPasswordHash("test-only-password-hash");
        user.BeginTwoFactorSetup("v1.fixture");
        user.EnableTwoFactor(100, now);
        await using (var setup = new WalletDbContext(options))
        {
            setup.Users.Add(user);
            await setup.SaveChangesAsync();
        }

        try
        {
            await using var first = new WalletDbContext(options);
            await using var second = new WalletDbContext(options);
            var firstRepository = new SqlUserRepository(first);
            var secondRepository = new SqlUserRepository(second);
            var a = (await firstRepository.FindByIdAsync(user.Id, default))!;
            var b = (await secondRepository.FindByIdAsync(user.Id, default))!;
            Assert.True(a.TryUseTwoFactorStep(101));
            Assert.True(b.TryUseTwoFactorStep(101));

            await firstRepository.SaveAsync(default);
            await Assert.ThrowsAsync<ConcurrentUpdateException>(() => secondRepository.SaveAsync(default));

            await using var read = new WalletDbContext(options);
            var stored = await read.Users.SingleAsync(u => u.Id == user.Id);
            Assert.Equal(101, stored.TwoFactorLastUsedStep);
            Assert.False(stored.TryUseTwoFactorStep(101));
        }
        finally
        {
            await using var cleanup = new WalletDbContext(options);
            cleanup.Users.Remove(await cleanup.Users.SingleAsync(u => u.Id == user.Id));
            await cleanup.SaveChangesAsync();
        }
    }
}
