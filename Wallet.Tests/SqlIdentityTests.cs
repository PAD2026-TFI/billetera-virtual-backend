using Microsoft.EntityFrameworkCore;
using Wallet.Application;
using Wallet.Domain;
using Wallet.Infrastructure;
using Xunit;
namespace Wallet.Tests;

// Opt-in: una base SQL Server/Azure SQL exclusiva de pruebas, previamente migrada.
public sealed class AzureSqlFactAttribute : FactAttribute
{
    public AzureSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WALLET_TEST_CONNECTION")))
            Skip = "Falta WALLET_TEST_CONNECTION: requiere SQL Server/Azure SQL de pruebas con migración aplicada.";
    }
}
[Collection("SqlIntegration")]
[Trait("Category", "Sql")]
public sealed class SqlIdentityTests
{
    private static WalletDbContext NewDb() => new(new DbContextOptionsBuilder<WalletDbContext>()
        .UseSqlServer(Environment.GetEnvironmentVariable("WALLET_TEST_CONNECTION")!).Options);
    [AzureSqlFact]
    public async Task Simultaneous_registration_preserves_email_uniqueness()
    {
        string email = $"test-{Guid.NewGuid():N}@example.com";
        async Task<bool> Insert()
        {
            await using var db = NewDb();
            var user = User.Create("Test", "User", email, DateTimeOffset.UtcNow);
            user.SetPasswordHash(new IdentityPasswordService().Hash(user, "Frase de prueba 2026!"));
            try { await new SqlUserRepository(db).AddAsync(user, CancellationToken.None); return true; }
            catch (DuplicateEmailException) { return false; }
        }
        try
        {
            var results = await Task.WhenAll(Insert(), Insert());
            Assert.Single(results, x => x);
            await using var db = NewDb();
            Assert.Equal(1, await db.Users.CountAsync(x => x.NormalizedEmail == User.NormalizeEmail(email)));
        }
        finally
        {
            await using var db = NewDb();
            await db.Users.Where(x => x.NormalizedEmail == User.NormalizeEmail(email)).ExecuteDeleteAsync();
        }
    }
}
