using System.Net;
using System.Net.Http.Json;
using Wallet.Application;
using Wallet.Domain;
using Xunit;

namespace Wallet.Tests;

public sealed class AdminAccessRestrictionTests
{
    [Theory]
    [InlineData("/api/v1/admin/register")]
    [InlineData("/api/admin/register")]
    public async Task Admin_creation_denies_anonymous_client_and_api_key_before_writing(string route)
    {
        await using var host = new IdentityTestHost();
        var email = $"must-not-exist-{Guid.NewGuid():N}@example.com";
        var request = new RegisterAdminRequest { FirstName = "Denied", LastName = "Admin", Email = email, Password = IdentityTestHost.Password };
        using var anonymous = host.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync(route, request)).StatusCode);
        var user = host.Seed("client@example.com");
        using var client = host.Client(); client.DefaultRequestHeaders.Authorization = new("Bearer", host.Token(user));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(route, request)).StatusCode);
        user.SetAccountType("merchant");
        user.SetIntegrationKey("Fixture", GatewayService.HashApiKey("padi_fixture"), "padi_fixture", DateTimeOffset.UtcNow);
        using var key = host.Client(); key.DefaultRequestHeaders.Add("X-Api-Key", "padi_fixture");
        Assert.Equal(HttpStatusCode.Unauthorized, (await key.PostAsJsonAsync(route, request)).StatusCode);
        Assert.Null(await host.Users.FindByEmailAsync(User.NormalizeEmail(email), default));
    }

    [Theory]
    [InlineData("/api/v1/admin/register")]
    [InlineData("/api/admin/register")]
    public async Task Existing_admin_can_create_admin_but_inactive_or_expired_sessions_cannot(string route)
    {
        await using var host = new IdentityTestHost(); var admin = host.Seed("existing-admin@example.com", "Admin");
        using var client = host.Client();
        var request = new RegisterAdminRequest { FirstName = "New", LastName = "Admin", Email = "new-admin@example.com", Password = IdentityTestHost.Password };
        client.DefaultRequestHeaders.Authorization = new("Bearer", host.Token(admin, "expired"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(route, request)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", host.Token(admin));
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(route, request)).StatusCode);
        Assert.Equal("Admin", (await host.Users.FindByEmailAsync("NEW-ADMIN@EXAMPLE.COM", default))!.Role);
        admin.Deactivate();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(route, request)).StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/admin/wallet-funding/by-cvu")]
    [InlineData("/api/v1/admin/wallet-funding/by-alias")]
    [InlineData("/api/v1/admin/wallet-funding/by-wallet-id")]
    [InlineData("/api/admin/funds")]
    [InlineData("/api/admin/funds/cvu")]
    [InlineData("/api/admin/funds/alias")]
    [InlineData("/api/admin/funds/wallet-id")]
    public async Task Funding_denies_anonymous_and_client_sessions_before_model_validation(string route)
    {
        await using var host = new IdentityTestHost(); using var client = host.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(route, new { })).StatusCode);
        var user = host.Seed(); client.DefaultRequestHeaders.Authorization = new("Bearer", host.Token(user));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(route, new { })).StatusCode);
    }

    [Fact]
    public async Task Public_user_registration_cannot_request_admin_role()
    {
        await using var host = new IdentityTestHost(); using var client = host.Client();
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new { firstName = "Denied", lastName = "Admin", email = "escalation@example.com", password = IdentityTestHost.Password, role = "Admin" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(await host.Users.FindByEmailAsync("ESCALATION@EXAMPLE.COM", default));
    }
}
