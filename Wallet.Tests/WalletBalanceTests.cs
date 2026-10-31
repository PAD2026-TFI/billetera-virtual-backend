using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Wallet.Application;
using Xunit;
using DomainWallet = Wallet.Domain.Wallet;

namespace Wallet.Tests;

public sealed class WalletBalanceTests
{
    [Theory]
    [InlineData("0.00")]
    [InlineData("12345.67")]
    public async Task Reading_returns_current_ARS_balance_and_only_public_fields_without_mutations(string amount)
    {
        await using var host = new IdentityTestHost();
        var user = host.Seed();
        var instant = new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
        var wallet = DomainWallet.Create(user.Id, instant);
        var balance = decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture);
        // Sólo fixture; no agrego operaciones de carga de saldo en esta historia.
        typeof(DomainWallet).GetProperty(nameof(DomainWallet.Balance))!.SetValue(wallet, Wallet.Domain.Money.From(balance));
        host.Wallets.Items[user.Id] = wallet;
        using var client = host.Client();
        client.DefaultRequestHeaders.Authorization = new("Bearer", host.Token(user));
        for (var i = 0; i < 2; i++)
        {
            using var response = await client.GetAsync("/api/wallets/me");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
            Assert.True(response.Headers.CacheControl?.NoStore);
            Assert.Equal(new WalletResponse(wallet.Id, balance, "ARS", instant),
                await response.Content.ReadFromJsonAsync<WalletResponse>());
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(new[] { "balance", "createdAt", "currency", "id" },
                json.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray());
        }
        Assert.Equal(0, host.Wallets.Creates);
        Assert.Equal(balance, wallet.Balance);
        Assert.Equal(instant, wallet.CreatedAt);
        Assert.Single(host.Wallets.Items);
    }

    [Fact]
    public async Task Missing_wallet_returns_404_without_creating_one()
    {
        await using var host = new IdentityTestHost();
        var user = host.Seed();
        using var client = host.Client();
        client.DefaultRequestHeaders.Authorization = new("Bearer", host.Token(user));
        using var response = await client.GetAsync("/api/wallets/me");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("WALLET_NOT_FOUND", body.RootElement.GetProperty("code").GetString());
        Assert.Empty(host.Wallets.Items);
        Assert.Equal(0, host.Wallets.Creates);
    }

    [Fact]
    public async Task Owner_is_taken_from_token_even_if_query_names_another_user()
    {
        await using var host = new IdentityTestHost();
        var first = host.Seed();
        var second = host.Seed("other@example.com");
        var firstWallet = DomainWallet.Create(first.Id, DateTimeOffset.UtcNow);
        var secondWallet = DomainWallet.Create(second.Id, DateTimeOffset.UtcNow);
        host.Wallets.Items[first.Id] = firstWallet;
        host.Wallets.Items[second.Id] = secondWallet;
        using var client = host.Client();
        foreach (var (user, own, other) in new[]
        { (first, firstWallet, secondWallet), (second, secondWallet, firstWallet) })
        {
            client.DefaultRequestHeaders.Authorization = new("Bearer", host.Token(user));
            using var response = await client.GetAsync($"/api/wallets/me?userId={other.UserId}&walletId={other.Id}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(own.Id, (await response.Content.ReadFromJsonAsync<WalletResponse>())!.Id);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/wallets/{other.Id}")).StatusCode);
        }
        // Tener billetera ajena no equivale a tener una propia.
        host.Wallets.Items.TryRemove(second.Id, out _);
        using var missing = await client.GetAsync($"/api/wallets/me?userId={first.Id}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(0, host.Wallets.Creates);
    }

    [Theory]
    [InlineData("none", HttpStatusCode.Unauthorized)]
    [InlineData("signature", HttpStatusCode.Unauthorized)]
    [InlineData("expired", HttpStatusCode.Unauthorized)]
    [InlineData("revoked", HttpStatusCode.Unauthorized)]
    [InlineData("disabled", HttpStatusCode.Unauthorized)]
    [InlineData("forbidden", HttpStatusCode.Forbidden)]
    public async Task Invalid_access_does_not_query_wallet(string scenario, HttpStatusCode expected)
    {
        await using var host = new IdentityTestHost();
        var user = host.Seed(role: scenario == "forbidden" ? "TestObserver" : "Client");
        using var client = host.Client();
        if (scenario != "none") client.DefaultRequestHeaders.Authorization = new("Bearer", host.Token(user, scenario));
        if (scenario == "revoked") user.RevokeSessions();
        if (scenario == "disabled") user.Disable();
        host.Wallets.Unavailable = true;
        Assert.Equal(expected, (await client.GetAsync("/api/wallets/me")).StatusCode);
        Assert.Equal(0, host.Wallets.Reads);
        Assert.Equal(0, host.Wallets.Creates);
    }

    [Fact]
    public async Task Unavailable_repository_returns_503_instead_of_zero_or_missing_wallet()
    {
        await using var host = new IdentityTestHost();
        var user = host.Seed();
        using var client = host.Client();
        client.DefaultRequestHeaders.Authorization = new("Bearer", host.Token(user));
        host.Wallets.Unavailable = true;
        using var response = await client.GetAsync("/api/wallets/me");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("STORAGE_UNAVAILABLE", body.RootElement.GetProperty("code").GetString());
        Assert.False(body.RootElement.TryGetProperty("balance", out _));
        Assert.Equal(0, host.Wallets.Creates);
    }

    [Fact]
    public async Task Disabled_identity_returns_503()
    {
        await using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.UseSetting("Identity:Enabled", "false").UseEnvironment("Development"));
        using var client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var response = await client.GetAsync("/api/wallets/me");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("IDENTITY_NOT_CONFIGURED", json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task OpenApi_declares_Bearer_JSON_and_expected_responses_without_input_parameters()
    {
        await using var host = new IdentityTestHost();
        using var client = host.Client();
        using var json = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var get = json.RootElement.GetProperty("paths").GetProperty("/api/wallets/me").GetProperty("get");
        Assert.False(get.TryGetProperty("requestBody", out _));
        Assert.False(get.TryGetProperty("parameters", out var parameters) && parameters.GetArrayLength() > 0);
        Assert.Contains(get.GetProperty("security").EnumerateArray(), item => item.TryGetProperty("Bearer", out _));
        foreach (var status in new[] { "200", "401", "403", "404", "503" })
            Assert.True(get.GetProperty("responses").TryGetProperty(status, out _));
        Assert.True(get.GetProperty("responses").GetProperty("200").GetProperty("content").TryGetProperty("application/json", out _));
    }
}
