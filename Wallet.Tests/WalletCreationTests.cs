using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Wallet.Application;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using DomainWallet = Wallet.Domain.Wallet;

namespace Wallet.Tests;

public sealed class WalletCreationTests
{
    [Fact]
    public void Domain_creates_zero_ARS_wallet_for_valid_owner_with_UTC_date()
    {
        var owner = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.FromHours(-3));
        var wallet = DomainWallet.Create(owner, now);
        Assert.NotEqual(Guid.Empty, wallet.Id);
        Assert.Equal(owner, wallet.UserId);
        Assert.Equal(0m, wallet.Balance);
        Assert.Equal("ARS", wallet.Currency);
        Assert.Equal(now.ToUniversalTime(), wallet.CreatedAt);
        Assert.Equal(TimeSpan.Zero, wallet.CreatedAt.Offset);
        Assert.Throws<ArgumentException>(() => DomainWallet.Create(Guid.Empty, now));
    }

    [Fact]
    public async Task Creation_and_repetition_return_same_private_resource_without_resetting_balance()
    {
        await using var host = new IdentityTestHost();
        var owner = host.Seed();
        using var client = host.Client();
        client.DefaultRequestHeaders.Authorization = new("Bearer", host.Token(owner));
        using var created = await client.PostAsync("/api/wallets", null);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.True(created.Headers.CacheControl?.NoStore);
        var wallet = (await created.Content.ReadFromJsonAsync<WalletResponse>())!;
        Assert.Equal(0m, wallet.Balance);
        Assert.Equal("ARS", wallet.Currency);
        Assert.Equal(owner.Id, host.Wallets.Items[owner.Id].UserId);
        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        Assert.Equal(new[] { "balance", "createdAt", "currency", "id" },
            json.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray());
        // Fixture de billetera con fondos futuros: repetir la creación no los modifica.
        typeof(DomainWallet).GetProperty(nameof(DomainWallet.Balance))!.SetValue(host.Wallets.Items[owner.Id], Wallet.Domain.Money.From(125.50m));
        using var repeated = await client.PostAsync("/api/wallets", null);
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        var existing = (await repeated.Content.ReadFromJsonAsync<WalletResponse>())!;
        Assert.Equal(wallet.Id, existing.Id);
        Assert.Equal(wallet.CreatedAt, existing.CreatedAt);
        Assert.Equal(125.50m, existing.Balance);
        Assert.Single(host.Wallets.Items);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData(" ")]
    [InlineData("{\"userId\":\"00000000-0000-0000-0000-000000000001\"}")]
    [InlineData("{\"balance\":1000,\"currency\":\"USD\"}")]
    public async Task Any_body_is_rejected_without_creating_wallet(string body)
    {
        await using var host = new IdentityTestHost();
        var owner = host.Seed();
        using var client = host.Client();
        client.DefaultRequestHeaders.Authorization = new("Bearer", host.Token(owner));
        using var response = await client.PostAsync("/api/wallets", new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("INVALID_INPUT", json.RootElement.GetProperty("code").GetString());
        Assert.Empty(host.Wallets.Items);
    }

    [Theory]
    [InlineData("none", HttpStatusCode.Unauthorized)]
    [InlineData("expired", HttpStatusCode.Unauthorized)]
    [InlineData("signature", HttpStatusCode.Unauthorized)]
    [InlineData("revoked", HttpStatusCode.Unauthorized)]
    [InlineData("disabled", HttpStatusCode.Unauthorized)]
    [InlineData("forbidden", HttpStatusCode.Forbidden)]
    public async Task Invalid_access_does_not_create_wallet(string scenario, HttpStatusCode expected)
    {
        await using var host = new IdentityTestHost();
        var owner = host.Seed(role: scenario == "forbidden" ? "TestObserver" : "Client");
        using var client = host.Client();
        if (scenario != "none") client.DefaultRequestHeaders.Authorization = new("Bearer", host.Token(owner, scenario));
        if (scenario == "revoked") owner.RevokeSessions();
        if (scenario == "disabled") owner.Disable();
        Assert.Equal(expected, (await client.PostAsync("/api/wallets", null)).StatusCode);
        Assert.Empty(host.Wallets.Items);
    }

    [Fact]
    public async Task Different_users_receive_separate_wallets_and_profile_reads_do_not_create_them()
    {
        await using var host = new IdentityTestHost();
        using var client = host.Client();
        var ids = new List<Guid>();
        foreach (var email in new[] { "one@example.com", "two@example.com" })
        {
            var owner = host.Seed(email);
            client.DefaultRequestHeaders.Authorization = new("Bearer", host.Token(owner));
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/users/me")).StatusCode);
            Assert.False(host.Wallets.Items.ContainsKey(owner.Id));
            using var response = await client.PostAsync("/api/wallets", null);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            ids.Add((await response.Content.ReadFromJsonAsync<WalletResponse>())!.Id);
            Assert.Equal(owner.Id, host.Wallets.Items[owner.Id].UserId);
        }
        Assert.Equal(2, ids.Distinct().Count());
    }

    [Fact]
    public async Task Unavailable_storage_returns_503_and_retry_is_safe()
    {
        await using var host = new IdentityTestHost();
        var owner = host.Seed();
        using var client = host.Client();
        client.DefaultRequestHeaders.Authorization = new("Bearer", host.Token(owner));
        host.Wallets.Unavailable = true;
        using var response = await client.PostAsync("/api/wallets", null);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Empty(host.Wallets.Items);
        host.Wallets.Unavailable = false;
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsync("/api/wallets", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/wallets", null)).StatusCode);
    }

    [Fact]
    public async Task Chunked_body_cannot_bypass_empty_body_contract()
    {
        await using var host = new IdentityTestHost();
        var owner = host.Seed();
        using var client = host.Client();
        client.DefaultRequestHeaders.Authorization = new("Bearer", host.Token(owner));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/wallets")
        { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        request.Headers.TransferEncodingChunked = true;
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(request)).StatusCode);
        Assert.Empty(host.Wallets.Items);
    }

    [Fact]
    public async Task Disabled_identity_returns_503_before_creating_wallet()
    {
        await using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.UseSetting("Identity:Enabled", "false").UseEnvironment("Development"));
        using var client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var response = await client.PostAsync("/api/wallets", null);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("IDENTITY_NOT_CONFIGURED", json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task OpenApi_documents_creation_and_repetition_without_request_body()
    {
        await using var host = new IdentityTestHost();
        using var client = host.Client();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var post = document.RootElement.GetProperty("paths").GetProperty("/api/wallets").GetProperty("post");
        Assert.False(post.TryGetProperty("requestBody", out _));
        foreach (var status in new[] { "200", "201", "400", "401", "403", "503" })
            Assert.True(post.GetProperty("responses").TryGetProperty(status, out _));
        Assert.True(post.TryGetProperty("security", out _));
    }
}
