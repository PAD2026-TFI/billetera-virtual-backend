using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Wallet.Application;
using Wallet.Domain;
using Xunit;
using DomainWallet = Wallet.Domain.Wallet;

namespace Wallet.Tests;

public sealed class MerchantIntegrationTests
{
    private const string Endpoint = "/api/v1/me/merchant/integration";
    private static HttpClient SellerClient(IdentityTestHost host, out User user, bool merchant = true)
    {
        user = host.Seed($"seller-{Guid.NewGuid():N}@example.com");
        if (merchant) user.SetAccountType("merchant");
        host.Wallets.CreateAsync(DomainWallet.Create(user.Id, DateTimeOffset.UtcNow), default).GetAwaiter().GetResult();
        var client = host.Client();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", host.Token(user));
        return client;
    }
    private static HttpClient KeyClient(IdentityTestHost host, string key)
    {
        var client = host.Client(); client.DefaultRequestHeaders.Add("X-Api-Key", key); return client;
    }
    private static async Task<MerchantIntegrationKeyResponse> Issue(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(Endpoint, new { name = "Tienda online" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        return (await response.Content.ReadFromJsonAsync<MerchantIntegrationKeyResponse>())!;
    }
    private static Task<HttpResponseMessage> CreateLink(HttpClient client) =>
        client.PostAsJsonAsync("/api/v1/integrations/payment-links", new { amount = 1500, title = "Pedido 1042" });

    [Fact]
    public async Task Key_creates_links_for_the_same_owner_and_wallet_and_cannot_debit_or_manage_keys()
    {
        await using var host = new IdentityTestHost();
        using var seller = SellerClient(host, out var user);
        var wallet = host.Wallets.Items[user.Id];
        var createdWallets = host.Wallets.Creates;
        var issued = await Issue(seller);
        Assert.Equal(wallet.Id, issued.Integration.WalletId);
        Assert.NotEqual(issued.ApiKey, user.IntegrationKeyHash);
        Assert.Equal(GatewayService.HashApiKey(issued.ApiKey), user.IntegrationKeyHash);
        var metadata = await seller.GetStringAsync(Endpoint);
        Assert.DoesNotContain(issued.ApiKey, metadata);
        Assert.DoesNotContain(user.IntegrationKeyHash!, metadata);
        Assert.Equal(HttpStatusCode.Conflict, (await seller.PostAsJsonAsync(Endpoint, new { name = "Duplicada" })).StatusCode);
        using var external = KeyClient(host, issued.ApiKey);
        var response = await CreateLink(external);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var link = (await response.Content.ReadFromJsonAsync<PaymentIntentResponse>())!;
        var saved = await host.Links.FindByIdAsync(link.Id, default);
        Assert.Equal(user.Id, saved!.MerchantOwnerId);
        Assert.Equal(wallet.Id, saved.DestinationWalletId);
        Assert.Equal(createdWallets, host.Wallets.Creates);
        var appLinks = await seller.GetFromJsonAsync<PaymentLinkDto[]>("/api/v1/me/merchant/payment-links");
        Assert.Contains(appLinks!, item => item.Id == link.Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await external.PostAsJsonAsync("/api/v1/integrations/transfers", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await external.GetAsync("/api/v1/integrations/wallet")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await external.GetAsync(Endpoint)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await seller.PostAsJsonAsync("/api/v1/integrations/payment-links", new { amount = 1, title = "JWT" })).StatusCode);
    }

    [Fact]
    public async Task Rotation_and_revocation_invalidate_keys_but_keep_existing_links()
    {
        await using var host = new IdentityTestHost();
        using var seller = SellerClient(host, out _);
        var issued = await Issue(seller);
        using var oldKey = KeyClient(host, issued.ApiKey);
        var link = (await (await CreateLink(oldKey)).Content.ReadFromJsonAsync<PaymentIntentResponse>())!;
        var rotation = await seller.PostAsync(Endpoint + "/rotate", null);
        Assert.Equal(HttpStatusCode.OK, rotation.StatusCode);
        var rotated = (await rotation.Content.ReadFromJsonAsync<MerchantIntegrationKeyResponse>())!;
        Assert.NotEqual(issued.ApiKey, rotated.ApiKey);
        Assert.Equal(HttpStatusCode.Unauthorized, (await CreateLink(oldKey)).StatusCode);
        using var newKey = KeyClient(host, rotated.ApiKey);
        Assert.Equal(HttpStatusCode.OK, (await newKey.GetAsync($"/api/v1/integrations/payment-links/{link.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await seller.DeleteAsync(Endpoint)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await CreateLink(newKey)).StatusCode);
        var metadata = (await seller.GetFromJsonAsync<MerchantIntegrationResponse>(Endpoint))!;
        Assert.Equal("Revoked", metadata.Status);
        Assert.NotNull(metadata.RevokedAt);
        Assert.Equal(HttpStatusCode.Conflict, (await seller.PostAsync(Endpoint + "/rotate", null)).StatusCode);
        var reissued = await Issue(seller);
        Assert.NotEqual(rotated.ApiKey, reissued.ApiKey);
    }

    [Fact]
    public async Task Personal_accounts_must_opt_in_and_switching_back_revokes_the_key()
    {
        await using var host = new IdentityTestHost();
        using var seller = SellerClient(host, out var user, merchant: false);
        Assert.Equal(HttpStatusCode.Forbidden, (await seller.PostAsJsonAsync(Endpoint, new { name = "Tienda" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await seller.PutAsJsonAsync("/api/v1/me/account-type", new { accountType = "Admin" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await seller.PutAsJsonAsync("/api/v1/me/account-type", new { accountType = "merchant" })).StatusCode);
        Assert.Equal("merchant", (await seller.GetFromJsonAsync<UserResponse>("/api/v1/me/profile"))!.AccountType);
        var issued = await Issue(seller);
        await seller.PutAsJsonAsync("/api/v1/me/account-type", new { accountType = "personal" });
        Assert.Equal("Client", user.Role);
        using var key = KeyClient(host, issued.ApiKey);
        Assert.Equal(HttpStatusCode.Unauthorized, (await CreateLink(key)).StatusCode);
        await seller.PutAsJsonAsync("/api/v1/me/account-type", new { accountType = "merchant" });
        Assert.Equal(HttpStatusCode.Unauthorized, (await CreateLink(key)).StatusCode);
    }

    [Fact]
    public async Task Key_cannot_read_another_sellers_link_or_received_payment()
    {
        await using var host = new IdentityTestHost();
        using var sellerA = SellerClient(host, out var a);
        using var sellerB = SellerClient(host, out var b);
        var issuedA = await Issue(sellerA); var issuedB = await Issue(sellerB);
        using var keyA = KeyClient(host, issuedA.ApiKey); using var keyB = KeyClient(host, issuedB.ApiKey);
        var linkB = (await (await CreateLink(keyB)).Content.ReadFromJsonAsync<PaymentIntentResponse>())!;
        Assert.Equal(HttpStatusCode.NotFound, (await keyA.GetAsync($"/api/v1/integrations/payment-links/{linkB.Id}")).StatusCode);
        // The read endpoints use the same owner as the seller session, not a new B2B identity.
        var payment = Payment.CreateForCard(linkB.Id, b.Id, host.Wallets.Items[b.Id].Id, Money.Positive(1500), "VISA", "4242", "Test", null, null, null, DateTimeOffset.UtcNow);
        payment.MarkSucceeded(null, "TEST", DateTimeOffset.UtcNow);
        var payments = new MemoryPayments(); await payments.CreateAsync(payment, default);
        using var app = host.WithWebHostBuilder(builder => builder.ConfigureTestServices(s => {
            s.RemoveAll<IPaymentRepository>(); s.AddSingleton<IPaymentRepository>(payments);
        }));
        using var client = app.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Api-Key", issuedA.ApiKey);
        Assert.Empty((await client.GetFromJsonAsync<MerchantPaymentDto[]>("/api/v1/integrations/received-payments"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/integrations/received-payments/{payment.Id}")).StatusCode);
        client.DefaultRequestHeaders.Remove("X-Api-Key"); client.DefaultRequestHeaders.Add("X-Api-Key", issuedB.ApiKey);
        Assert.Single((await client.GetFromJsonAsync<MerchantPaymentDto[]>("/api/v1/integrations/received-payments"))!);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/integrations/received-payments/{payment.Id}")).StatusCode);
    }

    [Fact]
    public async Task New_merchant_registration_persists_account_type()
    {
        await using var host = new IdentityTestHost(); using var client = host.Client();
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new {
            firstName = "Test", lastName = "Seller", dni = "98888888", email = "merchant@example.com", password = IdentityTestHost.Password, accountType = "merchant"
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("merchant", (await response.Content.ReadFromJsonAsync<UserResponse>())!.AccountType);
        Assert.Equal("merchant", (await host.Users.FindByEmailAsync("MERCHANT@EXAMPLE.COM", default))!.AccountType);
    }

    private sealed class MemoryPayments : IPaymentRepository
    {
        private readonly List<Payment> items = [];
        public Task CreateAsync(Payment p, CancellationToken ct) { items.Add(p); return Task.CompletedTask; }
        public Task UpdateAsync(Payment p, CancellationToken ct) => Task.CompletedTask;
        public Task<Payment?> FindByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(items.FirstOrDefault(p => p.Id == id));
        public Task<Payment?> FindByIdempotencyKeyAsync(Guid id, string key, CancellationToken ct) => Task.FromResult(items.FirstOrDefault(p => p.MerchantOwnerId == id && p.IdempotencyKey == key));
        public Task<IReadOnlyList<Payment>> GetByMerchantOwnerIdAsync(Guid id, int limit, CancellationToken ct) => Task.FromResult<IReadOnlyList<Payment>>(items.Where(p => p.MerchantOwnerId == id).Take(limit).ToArray());
        public Task<IReadOnlyList<Payment>> GetByPaymentLinkIdAsync(Guid id, CancellationToken ct) => Task.FromResult<IReadOnlyList<Payment>>(items.Where(p => p.PaymentLinkId == id).ToArray());
    }
}
