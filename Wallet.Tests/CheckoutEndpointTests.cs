using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
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

public sealed class CheckoutEndpointTests
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

    private sealed class InMemoryUserRepository : IUserRepository
    {
        private readonly ConcurrentDictionary<string, User> _byEmail = new(StringComparer.OrdinalIgnoreCase);

        public Task<User?> FindByEmailAsync(string normalizedEmail, CancellationToken ct) =>
            Task.FromResult(_byEmail.TryGetValue(normalizedEmail, out var u) ? u : null);

        public Task<User?> FindByIdAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(_byEmail.Values.FirstOrDefault(u => u.Id == id));

        public Task AddAsync(User user, CancellationToken ct)
        {
            _byEmail[user.NormalizedEmail] = user;
            return Task.CompletedTask;
        }

        public Task SaveAsync(CancellationToken ct) => Task.CompletedTask;

        public Task<bool> IsReadyAsync(CancellationToken ct) => Task.FromResult(true);
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
    public async Task EndToEnd_checkout_link_creation_qr_and_card_payment()
    {
        var linksRepo = new InMemoryPaymentLinkRepository();
        var paymentsRepo = new InMemoryPaymentRepository();
        var walletRepo = new InMemoryWalletRepository();
        var userRepo = new InMemoryUserRepository();
        var fundingStore = new FakeFundingStore();

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

                services.RemoveAll<IUserRepository>();
                services.AddSingleton<IUserRepository>(userRepo);

                services.RemoveAll<IDevelopmentFundingStore>();
                services.AddSingleton<IDevelopmentFundingStore>(fundingStore);
            });
        });

        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });

        // 1. Crear un link de pago como comercio invitado
        var createRequest = new CreatePaymentLinkRequest
        {
            Amount = 2500m,
            Title = "Servicio de Consultoría",
            Description = "1 hora de asesoría técnica",
            MerchantEmail = "consultor@empresa.com"
        };

        var postRes = await client.PostAsJsonAsync("/api/v1/checkout/links", createRequest);
        Assert.Equal(HttpStatusCode.Created, postRes.StatusCode);

        var created = await postRes.Content.ReadFromJsonAsync<PaymentLinkDto>();
        Assert.NotNull(created);
        Assert.NotEmpty(created.Token);
        Assert.Equal(2500m, created.Amount);
        Assert.Equal("Active", created.Status);

        // 2. Consultar detalles públicos mediante el token
        var getRes = await client.GetAsync($"/api/v1/checkout/links/{created.Token}");
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);

        var details = await getRes.Content.ReadFromJsonAsync<CheckoutPublicDetailsDto>();
        Assert.NotNull(details);
        Assert.Equal("Servicio de Consultoría", details.Title);
        Assert.True(details.IsPayable);

        // 3. Consultar código QR en formato SVG
        var qrRes = await client.GetAsync($"/api/v1/checkout/links/{created.Token}/qr");
        Assert.Equal(HttpStatusCode.OK, qrRes.StatusCode);
        Assert.Equal("image/svg+xml", qrRes.Content.Headers.ContentType?.MediaType);
        var svgContent = await qrRes.Content.ReadAsStringAsync();
        Assert.Contains("<svg", svgContent);

        // 4. Pagar con tarjeta simulada con fondos insuficientes (CVV 999)
        var failedPayRequest = new ProcessCheckoutRequest
        {
            Token = created.Token,
            PaymentMethod = CheckoutPaymentMethod.Card,
            Card = new CardPaymentDetails("4532015112830366", "Carlos Sin Fondos", 12, 2029, "999")
        };

        var failedPayRes = await client.PostAsJsonAsync("/api/v1/checkout/pay", failedPayRequest);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, failedPayRes.StatusCode);

        // 5. Pagar con tarjeta simulada exitosa
        var successPayRequest = new ProcessCheckoutRequest
        {
            Token = created.Token,
            PaymentMethod = CheckoutPaymentMethod.Card,
            Card = new CardPaymentDetails("4532015112830366", "Ana Cliente", 12, 2029, "123")
        };

        var payRes = await client.PostAsJsonAsync("/api/v1/checkout/pay", successPayRequest);
        Assert.Equal(HttpStatusCode.OK, payRes.StatusCode);

        var receipt = await payRes.Content.ReadFromJsonAsync<CheckoutReceiptDto>();
        Assert.NotNull(receipt);
        Assert.Equal("Completed", receipt.Status);
        Assert.Equal(2500m, receipt.Amount);
        Assert.NotNull(receipt.AuthorizationCode);

        // 6. Intentar pagar nuevamente el mismo link (SingleUse) debe dar conflicto
        var doublePayRes = await client.PostAsJsonAsync("/api/v1/checkout/pay", successPayRequest);
        Assert.Equal(HttpStatusCode.Conflict, doublePayRes.StatusCode);
    }
}
