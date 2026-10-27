using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Wallet.Application;
using Wallet.Domain;
using Xunit;
using DomainWallet = Wallet.Domain.Wallet;

namespace Wallet.Tests;

public sealed class TransferTests
{
    [Theory]
    [InlineData("0.01")]
    [InlineData("780400")]
    public void Domain_transfer_and_two_movements_preserve_amount_currency_and_references(string amount)
    {
        var value = decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture);
        var source = DomainWallet.Create(Guid.NewGuid(), DateTimeOffset.UtcNow);
        var destination = DomainWallet.Create(Guid.NewGuid(), DateTimeOffset.UtcNow);
        source.Credit(value);
        var t = Transfer.Complete(source.Id, destination.Id, value, Guid.NewGuid(),
            TransferService.Fingerprint(destination.Id, value), DateTimeOffset.UtcNow);
        source.Debit(value); destination.Credit(value);
        var debit = Movement.ForTransfer(t, true, source.Balance);
        var credit = Movement.ForTransfer(t, false, destination.Balance);
        Assert.Equal(value, source.Balance + destination.Balance);
        Assert.Equal(0m, source.Balance); Assert.Equal(value, destination.Balance);
        Assert.Equal(TransferStatus.Completed, t.Status); Assert.Equal("ARS", t.Currency);
        Assert.Equal(source.Id, debit.WalletId); Assert.Equal(destination.Id, credit.WalletId);
        Assert.Equal(MovementType.Debit, debit.Type); Assert.Equal(MovementType.Credit, credit.Type);
        Assert.All(new[] { debit, credit }, m =>
        {
            Assert.Equal(t.Id, m.TransferId); Assert.Null(m.DevelopmentFundingId);
            Assert.Equal(value, m.Amount); Assert.Equal(MovementSource.Transfer, m.Source);
        });
    }

    [Fact]
    public void Debit_cannot_make_balance_negative_and_fingerprint_is_canonical()
    {
        var wallet = DomainWallet.Create(Guid.NewGuid(), DateTimeOffset.UtcNow); wallet.Credit(10m);
        Assert.Throws<ArgumentException>(() => wallet.Debit(10.01m));
        Assert.Equal(10m, wallet.Balance);
        var destination = Guid.NewGuid();
        Assert.Equal(TransferService.Fingerprint(destination, 1m), TransferService.Fingerprint(destination, 1.00m));
        Assert.NotEqual(TransferService.Fingerprint(destination, 1m), TransferService.Fingerprint(destination, 2m));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("0.001")]
    [InlineData("780400.01")]
    public async Task Invalid_amount_is_rejected_before_persistence(string amount)
    {
        await using var host = new IdentityTestHost(); var user = host.Seed();
        using var client = host.Client(); client.DefaultRequestHeaders.Authorization = new("Bearer", host.Token(user));
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        using var response = await client.PostAsJsonAsync("/api/transfers", new
        { destinationWalletId = Guid.NewGuid(), amount = decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture) });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("INVALID_AMOUNT", body.RootElement.GetProperty("code").GetString());
        Assert.Equal(0, host.Transfers.Calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Missing_or_invalid_key_does_not_reach_store(string? key)
    {
        await using var host = new IdentityTestHost(); var user = host.Seed();
        using var client = host.Client(); client.DefaultRequestHeaders.Authorization = new("Bearer", host.Token(user));
        if (key is not null) client.DefaultRequestHeaders.Add("Idempotency-Key", key);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/transfers",
            new { destinationWalletId = Guid.NewGuid(), amount = 1m })).StatusCode);
        Assert.Equal(0, host.Transfers.Calls);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"amount\":1}")]
    [InlineData("{\"destinationWalletId\":\"00000000-0000-0000-0000-000000000000\",\"amount\":1}")]
    [InlineData("{\"destinationWalletId\":\"not-a-guid\",\"amount\":1}")]
    [InlineData("{\"destinationWalletId\":\"d03d179d-951d-4c05-9c2e-b15c2916c378\",\"amount\":1,\"sourceWalletId\":\"other\"}")]
    [InlineData("{\"destinationWalletId\":\"d03d179d-951d-4c05-9c2e-b15c2916c378\",\"amount\":1,\"currency\":\"USD\"}")]
    public async Task Missing_malformed_and_unmapped_fields_are_rejected(string json)
    {
        await using var host = new IdentityTestHost(); var user = host.Seed();
        using var client = host.Client(); client.DefaultRequestHeaders.Authorization = new("Bearer", host.Token(user));
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/transfers", new StringContent(json, Encoding.UTF8, "application/json"))).StatusCode);
        Assert.Equal(0, host.Transfers.Calls);
    }

    [Theory]
    [InlineData(true, HttpStatusCode.Created)]
    [InlineData(false, HttpStatusCode.OK)]
    public async Task Endpoint_uses_current_user_and_maps_new_or_replayed_store_outcome(bool created, HttpStatusCode status)
    {
        await using var host = new IdentityTestHost(); var user = host.Seed();
        var destination = Guid.NewGuid(); var source = Guid.NewGuid(); var key = Guid.NewGuid();
        var transfer = new TransferResponse(Guid.NewGuid(), source, destination, 300m, "ARS", "Completed", DateTimeOffset.UtcNow);
        host.Transfers.Handler = (command, hash) =>
        {
            Assert.Equal(user.Id, command.SourceUserId); Assert.Equal(destination, command.DestinationWalletId);
            Assert.Equal(key, command.IdempotencyKey); Assert.Equal(300m, command.Amount);
            Assert.Equal(TransferService.Fingerprint(destination, 300m), hash);
            return new(transfer, created);
        };
        using var client = host.Client(); client.DefaultRequestHeaders.Authorization = new("Bearer", host.Token(user));
        client.DefaultRequestHeaders.Add("Idempotency-Key", key.ToString());
        using var response = await client.PostAsJsonAsync("/api/transfers", new { destinationWalletId = destination, amount = 300m });
        Assert.Equal(status, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(created ? "false" : "true", response.Headers.GetValues("Idempotency-Replayed").Single());
        Assert.Equal(transfer, await response.Content.ReadFromJsonAsync<TransferResponse>());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(new[] { "amount", "createdAt", "currency", "destinationWalletId", "id", "sourceWalletId", "status" },
            json.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray());
    }

    [Theory]
    [InlineData("none", HttpStatusCode.Unauthorized)]
    [InlineData("expired", HttpStatusCode.Unauthorized)]
    [InlineData("revoked", HttpStatusCode.Unauthorized)]
    [InlineData("forbidden", HttpStatusCode.Forbidden)]
    public async Task Unauthorized_requests_never_reach_store(string scenario, HttpStatusCode status)
    {
        await using var host = new IdentityTestHost(); var user = host.Seed(role: scenario == "forbidden" ? "TestObserver" : "Client");
        using var client = host.Client();
        if (scenario != "none") client.DefaultRequestHeaders.Authorization = new("Bearer", host.Token(user, scenario));
        if (scenario == "revoked") user.RevokeSessions();
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        Assert.Equal(status, (await client.PostAsJsonAsync("/api/transfers", new { destinationWalletId = Guid.NewGuid(), amount = 1m })).StatusCode);
        Assert.Equal(0, host.Transfers.Calls);
    }

    [Theory]
    [InlineData("SOURCE_WALLET_NOT_FOUND", 404)]
    [InlineData("DESTINATION_UNAVAILABLE", 422)]
    [InlineData("SAME_WALLET", 400)]
    [InlineData("INSUFFICIENT_FUNDS", 409)]
    [InlineData("DESTINATION_BALANCE_LIMIT", 409)]
    [InlineData("IDEMPOTENCY_CONFLICT", 409)]
    [InlineData("CONCURRENCY_CONFLICT", 409)]
    public async Task Business_rejections_have_contract_status_and_code(string code, int status)
    {
        await using var host = new IdentityTestHost(); var user = host.Seed();
        host.Transfers.Handler = (_, _) => throw new TransferRejectedException(code);
        using var client = host.Client(); client.DefaultRequestHeaders.Authorization = new("Bearer", host.Token(user));
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        using var response = await client.PostAsJsonAsync("/api/transfers", new { destinationWalletId = Guid.NewGuid(), amount = 1m });
        Assert.Equal(status, (int)response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Disabled_identity_returns_controlled_503()
    {
        await using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.UseSetting("Identity:Enabled", "false").UseEnvironment("Development"));
        using var client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var response = await client.PostAsJsonAsync("/api/transfers", new { destinationWalletId = Guid.NewGuid(), amount = 1m });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("IDENTITY_NOT_CONFIGURED", body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Storage_failure_is_503_and_OpenApi_documents_header_and_security()
    {
        await using var host = new IdentityTestHost(); var user = host.Seed();
        host.Transfers.Handler = (_, _) => throw new StorageUnavailableException(new InvalidOperationException());
        using var client = host.Client(); client.DefaultRequestHeaders.Authorization = new("Bearer", host.Token(user));
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        using var response = await client.PostAsJsonAsync("/api/transfers", new { destinationWalletId = Guid.NewGuid(), amount = 1m });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("STORAGE_UNAVAILABLE", body.RootElement.GetProperty("code").GetString());
        using var openapi = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var operation = openapi.RootElement.GetProperty("paths").GetProperty("/api/transfers").GetProperty("post");
        Assert.True(operation.TryGetProperty("security", out _));
        Assert.Contains(operation.GetProperty("parameters").EnumerateArray(), p =>
            p.GetProperty("name").GetString() == "Idempotency-Key" && p.GetProperty("required").GetBoolean());
        foreach (var status in new[] { "200", "201", "400", "401", "403", "404", "409", "422", "503" })
            Assert.True(operation.GetProperty("responses").TryGetProperty(status, out _));
    }
}
