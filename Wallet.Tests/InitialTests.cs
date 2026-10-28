using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Wallet.Api.Controllers;
using Wallet.Domain.Entities;
using Wallet.Infrastructure;
using Xunit;
namespace Wallet.Tests;
public sealed class InitialTests
{
    [Fact]
    public void User_has_fixed_role_and_normalized_email()
    {
        var user = User.Create(" Ana ", " Pérez ", "Ana@Example.com", DateTimeOffset.UtcNow);
        Assert.Equal("Client", user.Role);
        Assert.Equal("ANA@EXAMPLE.COM", user.NormalizedEmail);
        Assert.Equal("Ana", user.FirstName);
        Assert.True(user.IsActive);
        Assert.NotEqual(Guid.Empty, user.Id);
    }
    [Fact]
    public void Invalid_profile_does_not_partially_change_user()
    {
        var user = User.Create("Ana", "Pérez", "ana@example.com", DateTimeOffset.UtcNow);
        Assert.Throws<ArgumentException>(() => user.UpdateProfile("Otro", " "));
        Assert.Equal("Ana", user.FirstName);
    }
    [Fact]
    public void Disabling_user_revokes_sessions()
    {
        var user = User.Create("Ana", "Pérez", "ana@example.com", DateTimeOffset.UtcNow);
        user.Disable();
        Assert.False(user.IsActive);
        Assert.Equal(1, user.SessionVersion);
    }
    [Fact]
    public void Password_hash_is_salted_and_rejects_wrong_password()
    {
        var user = User.Create("Ana", "Pérez", "ana@example.com", DateTimeOffset.UtcNow);
        var passwords = new IdentityPasswordService();
        var hash = passwords.Hash(user, "Frase de prueba 2026!");
        Assert.NotEqual(hash, passwords.Hash(user, "Frase de prueba 2026!"));
        user.SetPasswordHash(hash);
        Assert.True(passwords.Verify(user, "Frase de prueba 2026!"));
        Assert.False(passwords.Verify(user, "incorrecta"));
    }
    [Fact]
    public async Task Baseline_is_alive_but_not_ready_and_does_not_fake_registration()
    {
        await using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.UseSetting("Identity:Enabled", "false").UseEnvironment("Development"));
        using var client = app.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        var openApi = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, openApi.StatusCode);
        Assert.Contains("/api/users/me/profile", await openApi.Content.ReadAsStringAsync());
        var live = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal("Healthy", (await live.Content.ReadFromJsonAsync<HealthResponse>())!.Status);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready")).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable,
            (await client.PostAsJsonAsync("/api/users", new { firstName = "Ana" })).StatusCode);
    }
}
