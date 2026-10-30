using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Wallet.Application;
using Wallet.Domain;
using Wallet.Infrastructure;
using Xunit;

namespace Wallet.Tests;

[Collection("SqlIntegration")]
[Trait("Category", "Sql")]
public sealed class SqlSessionRevocationTests
{
    private static WalletDbContext NewDb() => new(new DbContextOptionsBuilder<WalletDbContext>()
        .UseSqlServer(Environment.GetEnvironmentVariable("WALLET_TEST_CONNECTION")!).Options);

    [AzureSqlFact]
    public async Task Committed_revocation_and_disabling_are_observed_by_following_http_requests()
    {
        // Base exclusiva de pruebas, migrada de antemano. No migra ni borra una base compartida.
        var user = User.Create("SQL", "Session", $"test-{Guid.NewGuid():N}@example.com", DateTimeOffset.UtcNow);
        user.SetPasswordHash(new IdentityPasswordService().Hash(user, IdentityTestHost.Password));
        try
        {
            await using (var db = NewDb())
            {
                db.Users.Add(user);
                await db.SaveChangesAsync();
            }
            await using var host = new IdentityTestHost(Environment.GetEnvironmentVariable("WALLET_TEST_CONNECTION"));
            using var client = host.Client();
            var login = new LoginRequest { Email = user.Email, Password = IdentityTestHost.Password };
            using var firstLogin = await client.PostAsJsonAsync("/api/auth/login", login);
            Assert.Equal(HttpStatusCode.OK, firstLogin.StatusCode);
            var token = (await firstLogin.Content.ReadFromJsonAsync<TokenResponse>())!;
            client.DefaultRequestHeaders.Authorization = new("Bearer", token.AccessToken);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/users/me")).StatusCode);
            await using (var writer = NewDb())
            {
                var stored = await writer.Users.SingleAsync(u => u.Id == user.Id);
                stored.RevokeSessions();
                await writer.SaveChangesAsync();
            }
            await using (var reader = NewDb())
                Assert.Equal(1, (await reader.Users.SingleAsync(u => u.Id == user.Id)).SessionVersion);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/users/me")).StatusCode);
            using var secondLogin = await client.PostAsJsonAsync("/api/auth/login", login);
            Assert.Equal(HttpStatusCode.OK, secondLogin.StatusCode);
            var secondToken = (await secondLogin.Content.ReadFromJsonAsync<TokenResponse>())!;
            client.DefaultRequestHeaders.Authorization = new("Bearer", secondToken.AccessToken);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/users/me")).StatusCode);
            await using (var writer = NewDb())
            {
                var stored = await writer.Users.SingleAsync(u => u.Id == user.Id);
                stored.Disable();
                await writer.SaveChangesAsync();
            }
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/users/me")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", login)).StatusCode);
        }
        finally
        {
            await using var cleanup = NewDb();
            await cleanup.Users.Where(u => u.Id == user.Id).ExecuteDeleteAsync();
        }
    }
}
