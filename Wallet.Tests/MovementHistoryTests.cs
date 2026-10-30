using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Wallet.Api.Security;
using Wallet.Application;
using Wallet.Domain;
using Xunit;
using DomainWallet = Wallet.Domain.Wallet;

namespace Wallet.Tests;

public sealed class MovementHistoryTests
{
    private static DomainWallet SeedWallet(IdentityTestHost host, Guid userId)
    {
        var wallet = DomainWallet.Create(userId, DateTimeOffset.UtcNow);
        host.Wallets.Items[userId] = wallet;
        return wallet;
    }
    private static void SeedHistory(IdentityTestHost host, DomainWallet wallet, int count)
    {
        var now = new DateTimeOffset(2020, 1, 1, 12, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < count; i++)
        {
            var key = Guid.NewGuid(); var request = new DevelopmentFundingRequest(wallet.Id, 1m, key);
            var funding = DevelopmentFunding.Create(wallet.Id, 1m, key, DevelopmentFundingService.Fingerprint(request), now);
            host.History.Items.Add(Movement.ForDevelopmentFunding(funding, i + 1m));
        }
    }

    [Fact]
    public async Task Pages_with_equal_dates_have_no_duplicates_and_are_scoped_to_current_wallet()
    {
        await using var host = new IdentityTestHost(); var user = host.Seed(); var other = host.Seed("other@example.com");
        var wallet = SeedWallet(host, user.Id); var otherWallet = SeedWallet(host, other.Id);
        SeedHistory(host, wallet, 5); SeedHistory(host, otherWallet, 3);
        using var client = host.Client(); client.DefaultRequestHeaders.Authorization = new("Bearer", host.Token(user));
        var seen = new List<Guid>(); string? cursor = null;
        do
        {
            using var response = await client.GetAsync("/api/wallets/me/movements?limit=2" + (cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor)));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore);
            var page = (await response.Content.ReadFromJsonAsync<MovementPage>())!;
            Assert.InRange(page.Items.Count, 1, 2);
            Assert.All(page.Items, m => { Assert.Equal("ARS", m.Currency); Assert.Equal("DevelopmentFunding", m.Source); Assert.Equal("Credit", m.Type); });
            seen.AddRange(page.Items.Select(m => m.Id)); cursor = page.NextCursor;
        } while (cursor is not null);
        Assert.Equal(host.History.Items.Where(m => m.WalletId == wallet.Id).OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).Select(m => m.Id), seen);
        Assert.Equal(5, seen.Distinct().Count()); Assert.Equal(0, host.Wallets.Creates);
        Assert.Equal(0m, wallet.Balance);
    }

    [Fact]
    public async Task Foreign_or_modified_cursor_is_rejected_and_transfer_source_is_public_reference()
    {
        await using var host = new IdentityTestHost(); var user = host.Seed(); var other = host.Seed("other@example.com");
        var wallet = SeedWallet(host, user.Id); var otherWallet = SeedWallet(host, other.Id); SeedHistory(host, wallet, 3);
        var transfer = Transfer.Complete(wallet.Id, otherWallet.Id, 10m, Guid.NewGuid(), TransferService.Fingerprint(otherWallet.Id, 10m), DateTimeOffset.UtcNow);
        host.History.Items.Add(Movement.ForTransfer(transfer, true, 0m));
        using var client = host.Client(); client.DefaultRequestHeaders.Authorization = new("Bearer", host.Token(user));
        var first = (await client.GetFromJsonAsync<MovementPage>("/api/wallets/me/movements?limit=1"))!;
        Assert.Equal(transfer.Id, first.Items.Single().SourceId); Assert.Equal("Transfer", first.Items.Single().Source);
        var cursor = first.NextCursor!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", host.Token(other));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/wallets/me/movements?cursor=" + Uri.EscapeDataString(cursor))).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", host.Token(user));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/wallets/me/movements?cursor=" + Uri.EscapeDataString("x" + cursor))).StatusCode);
    }

    [Theory]
    [InlineData("limit=0")]
    [InlineData("limit=101")]
    [InlineData("limit=invalid")]
    [InlineData("cursor=not-a-cursor")]
    public async Task Invalid_pagination_returns_400(string query)
    {
        await using var host = new IdentityTestHost(); var user = host.Seed(); SeedWallet(host, user.Id);
        using var client = host.Client(); client.DefaultRequestHeaders.Authorization = new("Bearer", host.Token(user));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/wallets/me/movements?" + query)).StatusCode);
    }

    [Fact]
    public async Task Missing_wallet_empty_history_and_storage_failure_are_distinct()
    {
        await using var host = new IdentityTestHost(); var user = host.Seed();
        using var client = host.Client(); client.DefaultRequestHeaders.Authorization = new("Bearer", host.Token(user));
        using var missing = await client.GetAsync("/api/wallets/me/movements"); Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Empty(host.Wallets.Items); Assert.Equal(0, host.Wallets.Creates);
        SeedWallet(host, user.Id);
        var page = (await client.GetFromJsonAsync<MovementPage>("/api/wallets/me/movements"))!;
        Assert.Empty(page.Items); Assert.Null(page.NextCursor); Assert.Equal(21, host.History.LastCount);
        host.History.Unavailable = true;
        using var failed = await client.GetAsync("/api/wallets/me/movements"); Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        using var json = JsonDocument.Parse(await failed.Content.ReadAsStringAsync());
        Assert.Equal("STORAGE_UNAVAILABLE", json.RootElement.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData(false, HttpStatusCode.Unauthorized)]
    [InlineData(true, HttpStatusCode.Forbidden)]
    public async Task Authorization_is_required(bool wrongRole, HttpStatusCode expected)
    {
        await using var host = new IdentityTestHost(); using var client = host.Client();
        if (wrongRole) client.DefaultRequestHeaders.Authorization = new("Bearer", host.Token(host.Seed(role: "TestObserver")));
        Assert.Equal(expected, (await client.GetAsync("/api/wallets/me/movements")).StatusCode);
        Assert.Equal(0, host.History.LastCount);
    }

    [Fact]
    public void Codec_rejects_tampering_and_wallet_changes()
    {
        var codec = new MovementCursorCodec(new EphemeralDataProtectionProvider()); var wallet = Guid.NewGuid();
        var position = new MovementPosition(DateTimeOffset.UtcNow, Guid.NewGuid()); var cursor = codec.Encode(wallet, position);
        Assert.Equal(position, codec.Decode(wallet, cursor));
        Assert.Null(codec.Decode(Guid.NewGuid(), cursor)); Assert.Null(codec.Decode(wallet, "invalid"));
    }
}
