using Wallet.Application;
using Wallet.Domain;
using Xunit;
using DomainWallet = Wallet.Domain.Wallet;

namespace Wallet.Tests;

public sealed class PaymentLinkServiceTests
{
    private readonly InMemoryPaymentLinkRepository _links = new();
    private readonly InMemoryPaymentRepository _payments = new();
    private readonly InMemoryWalletRepository _wallets = new();
    private readonly InMemoryUserRepository _users = new();
    private readonly FakeTransferStore _transfers = new();
    private readonly FakeFundingStore _funding = new();
    private readonly TestCurrentUser _currentUser = new();
    private readonly TimeProvider _clock = TimeProvider.System;
    private readonly CardSimulatorService _cardSimulator;
    private readonly PaymentLinkService _service;

    public PaymentLinkServiceTests()
    {
        _cardSimulator = new CardSimulatorService(_clock);
        _service = new PaymentLinkService(
            _links,
            _payments,
            _wallets,
            _users,
            _transfers,
            _funding,
            _cardSimulator,
            _currentUser,
            _clock);
    }

    [Fact]
    public async Task CreateLinkAsync_authenticated_user_creates_link_with_users_wallet()
    {
        _currentUser.Id = Guid.NewGuid();

        var request = new CreatePaymentLinkRequest(
            1250.75m,
            "Servicio de Consultoría",
            "Hora de asesoría técnica",
            PaymentLinkType.SingleUse,
            60);

        var result = await _service.CreateLinkAsync(request, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(1250.75m, result.Amount);
        Assert.Equal("ARS", result.Currency);
        Assert.Equal("Servicio de Consultoría", result.Title);
        Assert.Equal(_currentUser.Id, result.MerchantOwnerId);
        Assert.False(string.IsNullOrWhiteSpace(result.Token));
        Assert.Contains(result.Token, result.CheckoutUrl);

        var saved = await _links.FindByTokenAsync(result.Token, CancellationToken.None);
        Assert.NotNull(saved);
        Assert.Equal(saved.Id, result.Id);
    }

    [Fact]
    public async Task CreateLinkAsync_guest_with_email_creates_guest_user_and_wallet()
    {
        _currentUser.Id = Guid.Empty;

        var request = new CreatePaymentLinkRequest(
            500m,
            "Venta informal",
            null,
            PaymentLinkType.SingleUse,
            null,
            null,
            "vendedor@externo.com");

        var result = await _service.CreateLinkAsync(request, CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(result.MerchantOwnerId);

        var user = await _users.FindByEmailAsync(User.NormalizeEmail("vendedor@externo.com"), CancellationToken.None);
        Assert.NotNull(user);
        Assert.Equal(result.MerchantOwnerId, user.Id);
    }

    [Fact]
    public async Task ProcessCheckoutAsync_with_valid_card_authorizes_and_creates_payment_record()
    {
        var walletId = Guid.NewGuid();
        var merchantOwnerId = Guid.NewGuid();
        var link = PaymentLink.Create(
            walletId,
            1500m,
            "Producto de prueba",
            "Descripción",
            PaymentLinkType.SingleUse,
            DateTimeOffset.UtcNow,
            merchantOwnerId: merchantOwnerId);

        await _links.CreateAsync(link, CancellationToken.None);

        var checkoutRequest = new ProcessCheckoutRequest(
            CheckoutPaymentMethod.Card,
            link.Token,
            new CardPaymentDetails("4532015112830366", "Juan Pérez", 12, 2028, "123"));

        var receipt = await _service.ProcessCheckoutAsync(link.Token, checkoutRequest, "idemp-001", CancellationToken.None);

        Assert.NotNull(receipt);
        Assert.Equal(1500m, receipt.Amount);
        Assert.Equal("Completed", receipt.Status);
        Assert.False(receipt.IdempotencyReplayed);

        var updated = await _links.FindByTokenAsync(link.Token, CancellationToken.None);
        Assert.Equal(PaymentLinkStatus.Completed, updated!.Status);
        Assert.Equal(1, _funding.Calls);

        var storedPayment = await _payments.FindByIdAsync(receipt.PaymentId, CancellationToken.None);
        Assert.NotNull(storedPayment);
        Assert.Equal(PaymentStatus.Succeeded, storedPayment.Status);
        Assert.Equal("Visa", storedPayment.CardBrand);
        Assert.Equal("0366", storedPayment.CardLast4);
    }

    [Fact]
    public async Task ProcessCheckoutAsync_idempotent_replay_returns_cached_receipt()
    {
        var walletId = Guid.NewGuid();
        var merchantOwnerId = Guid.NewGuid();
        var link = PaymentLink.Create(
            walletId,
            1000m,
            "Servicio Recurrente",
            null,
            PaymentLinkType.MultiUse,
            DateTimeOffset.UtcNow,
            merchantOwnerId: merchantOwnerId);

        await _links.CreateAsync(link, CancellationToken.None);

        var checkoutRequest = new ProcessCheckoutRequest(
            CheckoutPaymentMethod.Card,
            link.Token,
            new CardPaymentDetails("4532015112830366", "Juan Pérez", 12, 2028, "123"));

        var receipt1 = await _service.ProcessCheckoutAsync(link.Token, checkoutRequest, "idemp-dup", CancellationToken.None);
        var receipt2 = await _service.ProcessCheckoutAsync(link.Token, checkoutRequest, "idemp-dup", CancellationToken.None);

        Assert.Equal(receipt1.PaymentId, receipt2.PaymentId);
        Assert.False(receipt1.IdempotencyReplayed);
        Assert.True(receipt2.IdempotencyReplayed);
        Assert.Equal(1, _funding.Calls);
    }

    [Fact]
    public async Task ProcessCheckoutAsync_idempotent_key_with_altered_params_throws_conflict()
    {
        var walletId = Guid.NewGuid();
        var merchantOwnerId = Guid.NewGuid();
        var link = PaymentLink.Create(
            walletId,
            1000m,
            "Servicio A",
            null,
            PaymentLinkType.MultiUse,
            DateTimeOffset.UtcNow,
            merchantOwnerId: merchantOwnerId);

        await _links.CreateAsync(link, CancellationToken.None);

        var checkoutRequest1 = new ProcessCheckoutRequest(
            CheckoutPaymentMethod.Card,
            link.Token,
            new CardPaymentDetails("4532015112830366", "Juan Pérez", 12, 2028, "123"));

        await _service.ProcessCheckoutAsync(link.Token, checkoutRequest1, "idemp-conflict", CancellationToken.None);

        // Intento con diferente tarjeta pero misma clave de idempotencia
        var checkoutRequest2 = new ProcessCheckoutRequest(
            CheckoutPaymentMethod.Card,
            link.Token,
            new CardPaymentDetails("5555555555554444", "Otro Titular", 11, 2029, "456"));

        var ex = await Assert.ThrowsAsync<TransferRejectedException>(() =>
            _service.ProcessCheckoutAsync(link.Token, checkoutRequest2, "idemp-conflict", CancellationToken.None));

        Assert.Equal("IDEMPOTENCY_CONFLICT", ex.Code);
    }

    [Fact]
    public async Task GetPaymentByIdAsync_returns_payment_only_to_owner()
    {
        var merchantA = Guid.NewGuid();
        var merchantB = Guid.NewGuid();
        var destWallet = Guid.NewGuid();
        var linkId = Guid.NewGuid();

        var payment = Payment.CreateForCard(
            linkId,
            merchantA,
            destWallet,
            Money.Positive(500m),
            "Visa",
            "0366",
            "Juan",
            null,
            "idemp-1",
            "hash-1",
            DateTimeOffset.UtcNow);

        payment.MarkSucceeded(null, "AUTH-123", DateTimeOffset.UtcNow);
        await _payments.CreateAsync(payment, CancellationToken.None);

        // Comercio dueño debe ver su cobro
        var resultA = await _service.GetPaymentByIdAsync(payment.Id, merchantA, CancellationToken.None);
        Assert.NotNull(resultA);
        Assert.Equal(500m, resultA.Amount);

        // Comercio ajeno debe recibir null
        var resultB = await _service.GetPaymentByIdAsync(payment.Id, merchantB, CancellationToken.None);
        Assert.Null(resultB);
    }

    private sealed class InMemoryPaymentLinkRepository : IPaymentLinkRepository
    {
        private readonly Dictionary<string, PaymentLink> _byToken = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<Guid, PaymentLink> _byId = [];

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
        private readonly Dictionary<Guid, Payment> _byId = [];

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
        private readonly Dictionary<Guid, DomainWallet> _walletsByOwner = [];

        public Task<DomainWallet?> GetByUserIdAsync(Guid userId, CancellationToken ct) =>
            Task.FromResult(_walletsByOwner.TryGetValue(userId, out var w) ? w : null);

        public Task<WalletCreation> CreateOrGetAsync(DomainWallet candidate, CancellationToken ct)
        {
            if (_walletsByOwner.TryGetValue(candidate.OwnerId, out var existing))
                return Task.FromResult(new WalletCreation(existing, false));

            _walletsByOwner[candidate.OwnerId] = candidate;
            return Task.FromResult(new WalletCreation(candidate, true));
        }
    }

    private sealed class InMemoryUserRepository : IUserRepository
    {
        private readonly Dictionary<string, User> _byEmail = new(StringComparer.OrdinalIgnoreCase);

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

    private sealed class FakeTransferStore : ITransferStore
    {
        public Task<TransferExecution> ExecuteAsync(TransferCommand command, string hash, CancellationToken ct)
        {
            var transfer = Transfer.Complete(Guid.NewGuid(), command.DestinationWalletId, command.Amount, command.IdempotencyKey, hash, DateTimeOffset.UtcNow);
            return Task.FromResult(new TransferExecution(TransferResponse.From(transfer), true));
        }
    }

    private sealed class FakeFundingStore : IDevelopmentFundingStore
    {
        public int Calls { get; private set; }

        public Task<DevelopmentFundingResult> FundAsync(DevelopmentFundingRequest request, string hash, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new DevelopmentFundingResult(
                Guid.NewGuid(), request.WalletId, request.Amount, "ARS", DateTimeOffset.UtcNow, request.Amount, true));
        }
    }

    private sealed class TestCurrentUser : ICurrentUser
    {
        public Guid Id { get; set; } = Guid.NewGuid();
    }
}
