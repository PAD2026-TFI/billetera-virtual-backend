using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Wallet.Application;
using Wallet.Domain;
using Xunit;
using DomainWallet = Wallet.Domain.Wallet;

namespace Wallet.Tests;

public sealed class CheckoutB2BAndSecurityTests
{
    private sealed class InMemoryPaymentLinkRepository : IPaymentLinkRepository
    {
        private readonly ConcurrentDictionary<string, PaymentLink> _byToken = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<Guid, PaymentLink> _byId = [];

        public Task<PaymentLink?> FindByTokenAsync(string token, CancellationToken ct) =>
            Task.FromResult(_byToken.TryGetValue(token, out var val) ? val : null);

        public Task<PaymentLink?> FindByIdAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(_byId.TryGetValue(id, out var val) ? val : null);

        public Task CreateAsync(PaymentLink link, CancellationToken ct)
        {
            _byToken[link.Token] = link;
            _byId[link.Id] = link;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(PaymentLink link, CancellationToken ct)
        {
            _byToken[link.Token] = link;
            _byId[link.Id] = link;
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryPaymentRepository : IPaymentRepository
    {
        private readonly ConcurrentDictionary<Guid, Payment> _byId = [];

        public Task CreateAsync(Payment payment, CancellationToken ct)
        {
            _byId[payment.Id] = payment;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Payment payment, CancellationToken ct)
        {
            _byId[payment.Id] = payment;
            return Task.CompletedTask;
        }

        public Task<Payment?> FindByIdAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(_byId.TryGetValue(id, out var val) ? val : null);

        public Task<Payment?> FindByIdempotencyKeyAsync(Guid merchantOwnerId, string idempotencyKey, CancellationToken ct)
        {
            var p = _byId.Values.FirstOrDefault(x => x.MerchantOwnerId == merchantOwnerId && x.IdempotencyKey == idempotencyKey);
            return Task.FromResult(p);
        }

        public Task<IReadOnlyList<Payment>> GetByMerchantOwnerIdAsync(Guid merchantOwnerId, int limit, CancellationToken ct)
        {
            var list = _byId.Values.Where(x => x.MerchantOwnerId == merchantOwnerId).Take(limit).ToList();
            return Task.FromResult<IReadOnlyList<Payment>>(list);
        }

        public Task<IReadOnlyList<Payment>> GetByPaymentLinkIdAsync(Guid paymentLinkId, CancellationToken ct)
        {
            var list = _byId.Values.Where(x => x.PaymentLinkId == paymentLinkId).ToList();
            return Task.FromResult<IReadOnlyList<Payment>>(list);
        }
    }

    private sealed class InMemoryWalletRepository : IWalletRepository
    {
        private readonly ConcurrentDictionary<Guid, DomainWallet> _walletsByOwner = [];

        public Task<DomainWallet?> GetByUserIdAsync(Guid userId, CancellationToken ct) =>
            Task.FromResult(_walletsByOwner.TryGetValue(userId, out var w) ? w : null);

        public Task<WalletCreation> CreateOrGetAsync(DomainWallet candidate, CancellationToken ct)
        {
            var existing = _walletsByOwner.GetOrAdd(candidate.OwnerId, candidate);
            return Task.FromResult(new WalletCreation(existing, ReferenceEquals(existing, candidate)));
        }
    }

    private sealed class InMemoryExternalClientRepository : IExternalClientRepository
    {
        private readonly ConcurrentDictionary<string, ExternalClient> _byApiKeyHash = new();
        private readonly ConcurrentDictionary<Guid, ExternalClient> _byId = new();

        public Task<ExternalClient?> FindByApiKeyHashAsync(string hash, CancellationToken ct)
        {
            _byApiKeyHash.TryGetValue(hash, out var client);
            return Task.FromResult(client);
        }

        public Task<ExternalClient?> FindByIdAsync(Guid id, CancellationToken ct)
        {
            _byId.TryGetValue(id, out var client);
            return Task.FromResult(client);
        }

        public Task CreateAsync(ExternalClient client, CancellationToken ct)
        {
            _byApiKeyHash[client.ApiKeyHash] = client;
            _byId[client.Id] = client;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeFundingStore : IDevelopmentFundingStore
    {
        public Task<DevelopmentFundingResult> FundAsync(DevelopmentFundingRequest request, string hash, CancellationToken ct)
        {
            return Task.FromResult(new DevelopmentFundingResult(
                Guid.NewGuid(), request.WalletId, request.Amount, "ARS", DateTimeOffset.UtcNow, request.Amount, true));
        }
    }

    [Fact]
    public async Task Merchant_can_query_own_payment_and_other_merchant_gets_404()
    {
        var linksRepo = new InMemoryPaymentLinkRepository();
        var paymentsRepo = new InMemoryPaymentRepository();
        var walletRepo = new InMemoryWalletRepository();
        var clientsRepo = new InMemoryExternalClientRepository();
        var fundingStore = new FakeFundingStore();

        var now = DateTimeOffset.UtcNow;
        var merchantA = ExternalClient.Create("Comercio A", "a@empresa.com", now);
        var rawKeyA = "sec_key_comercio_a_123456789";
        merchantA.SetApiKeyHash(GatewayService.HashApiKey(rawKeyA), now);
        await clientsRepo.CreateAsync(merchantA, CancellationToken.None);

        var merchantB = ExternalClient.Create("Comercio B", "b@empresa.com", now);
        var rawKeyB = "sec_key_comercio_b_987654321";
        merchantB.SetApiKeyHash(GatewayService.HashApiKey(rawKeyB), now);
        await clientsRepo.CreateAsync(merchantB, CancellationToken.None);

        var walletA = DomainWallet.Create(merchantA.Id, DateTimeOffset.UtcNow);
        await walletRepo.CreateOrGetAsync(walletA, CancellationToken.None);

        var link = PaymentLink.Create(walletA.Id, 3000m, "Factura A-001", null, PaymentLinkType.SingleUse, DateTimeOffset.UtcNow, merchantOwnerId: merchantA.Id);
        await linksRepo.CreateAsync(link, CancellationToken.None);

        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Identity:Enabled", "true");
            builder.UseSetting("ConnectionStrings:DefaultConnection", "Server=unused;Database=unused;Integrated Security=true");
            builder.UseSetting("Jwt:Key", Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IPaymentLinkRepository>();
                services.AddSingleton<IPaymentLinkRepository>(linksRepo);

                services.RemoveAll<IPaymentRepository>();
                services.AddSingleton<IPaymentRepository>(paymentsRepo);

                services.RemoveAll<IWalletRepository>();
                services.AddSingleton<IWalletRepository>(walletRepo);

                services.RemoveAll<IExternalClientRepository>();
                services.AddSingleton<IExternalClientRepository>(clientsRepo);

                services.RemoveAll<IDevelopmentFundingStore>();
                services.AddSingleton<IDevelopmentFundingStore>(fundingStore);
            });
        });

        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });

        // 1. Pagar el link mediante tarjeta simulada
        var payRequest = new ProcessCheckoutRequest
        {
            Token = link.Token,
            PaymentMethod = CheckoutPaymentMethod.Card,
            Card = new CardPaymentDetails("4532015112830366", "Cliente Pagador", 12, 2029, "123")
        };

        var payRes = await client.PostAsJsonAsync("/api/v1/checkout/pay", payRequest);
        Assert.Equal(HttpStatusCode.OK, payRes.StatusCode);
        var receipt = await payRes.Content.ReadFromJsonAsync<CheckoutReceiptDto>();
        Assert.NotNull(receipt);

        // 2. Comercio A consulta su cobro con su X-Api-Key
        using var reqA = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/gateway/checkout-payments/{receipt.PaymentId}");
        reqA.Headers.Add("X-Api-Key", rawKeyA);

        var resA = await client.SendAsync(reqA);
        Assert.Equal(HttpStatusCode.OK, resA.StatusCode);
        var paymentA = await resA.Content.ReadFromJsonAsync<MerchantPaymentDto>();
        Assert.NotNull(paymentA);
        Assert.Equal(3000m, paymentA.Amount);
        Assert.Equal("Succeeded", paymentA.Status);
        Assert.Equal("Visa", paymentA.CardBrand);
        Assert.Equal("0366", paymentA.CardLast4);

        // 3. Comercio B intenta consultar el cobro de Comercio A -> Debe recibir 404 (aislamiento B2B)
        using var reqB = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/gateway/checkout-payments/{receipt.PaymentId}");
        reqB.Headers.Add("X-Api-Key", rawKeyB);

        var resB = await client.SendAsync(reqB);
        Assert.Equal(HttpStatusCode.NotFound, resB.StatusCode);
    }
}
