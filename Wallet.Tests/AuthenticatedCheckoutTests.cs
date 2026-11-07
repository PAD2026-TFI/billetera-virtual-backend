using System.Collections.Concurrent;
using Wallet.Application;
using Wallet.Domain;
using Xunit;
using DomainWallet = Wallet.Domain.Wallet;

namespace Wallet.Tests;

public sealed class AuthenticatedCheckoutTests
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

    private sealed class InMemoryCardRepository : ICardRepository
    {
        private readonly ConcurrentDictionary<Guid, Card> _cards = new();

        public void Add(Card card) => _cards[card.Id] = card;

        public Task<IReadOnlyList<Card>> GetByUserIdAsync(Guid userId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Card>>(_cards.Values.Where(c => c.UserId == userId).ToList());

        public Task<Card?> FindByIdAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(_cards.TryGetValue(id, out var card) ? card : null);

        public Task CreateAsync(Card card, CancellationToken ct)
        {
            _cards[card.Id] = card;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Card card, CancellationToken ct)
        {
            _cards[card.Id] = card;
            return Task.CompletedTask;
        }
    }

    private sealed class TestCurrentUser : ICurrentUser
    {
        public Guid Id { get; set; } = Guid.NewGuid();
    }

    private sealed class FakeDebitPayments : IDebitCardPaymentStore
    {
        public DebitCardPaymentCommand? LastCommand { get; private set; }
        public Task<CheckoutReceiptDto> ExecuteAsync(DebitCardPaymentCommand command, CancellationToken ct)
        {
            LastCommand = command;
            return Task.FromResult(new CheckoutReceiptDto(Guid.NewGuid(), "token", 2500m, "ARS", Guid.NewGuid(), "Card", "Completed", DateTimeOffset.UtcNow, "WALLET-DEBIT"));
        }
    }
    private readonly FakeDebitPayments _debitPayments = new();
    private readonly InMemoryPaymentLinkRepository _links = new();
    private readonly InMemoryPaymentRepository _payments = new();
    private readonly InMemoryWalletRepository _wallets = new();
    private readonly InMemoryUserRepository _users = new();
    private readonly FakeTransferStore _transfers = new();
    private readonly FakeFundingStore _funding = new();
    private readonly InMemoryCardRepository _cards = new();
    private readonly TestCurrentUser _currentUser = new();
    private readonly TimeProvider _clock = TimeProvider.System;
    private readonly CardSimulatorService _cardSimulator;
    private readonly PaymentLinkService _service;

    public AuthenticatedCheckoutTests()
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
            _clock,
            _cards, debitCardPayments: _debitPayments);
    }

    [Fact]
    public async Task PayWithSavedCardAsync_succeeds_when_card_belongs_to_authenticated_user()
    {
        var merchantOwnerId = Guid.NewGuid();
        var merchantWallet = DomainWallet.Create(merchantOwnerId, _clock.GetUtcNow());
        await _wallets.CreateOrGetAsync(merchantWallet, CancellationToken.None);

        var link = PaymentLink.Create(merchantWallet.Id, 2500m, "Pago de prueba", null, PaymentLinkType.SingleUse, _clock.GetUtcNow(), merchantOwnerId: merchantOwnerId);
        await _links.CreateAsync(link, CancellationToken.None);

        var payerUserId = Guid.NewGuid();
        var card = Card.Create(payerUserId, "Usuario Pagador", "Visa", CardType.Credit, "0366", 12, 2030, _clock.GetUtcNow(), "tok_123456");
        _cards.Add(card);

        var request = new PayWithSavedCardRequest { CardId = card.Id };
        var receipt = await _service.PayWithSavedCardAsync(link.Id, request, payerUserId, "idem-test-1", CancellationToken.None);

        Assert.NotNull(receipt);
        Assert.Equal("Completed", receipt.Status);
        Assert.Equal(2500m, receipt.Amount);
        Assert.Equal("Card", receipt.PaymentMethod);
        Assert.False(receipt.IdempotencyReplayed);

        var replay = await _service.PayWithSavedCardAsync(link.Id, request, payerUserId, "idem-test-1", CancellationToken.None);
        Assert.True(replay.IdempotencyReplayed);
        Assert.Equal(receipt.PaymentId, replay.PaymentId);
    }

    [Fact]
    public async Task PayWithSavedCardAsync_throws_when_card_does_not_belong_to_payer()
    {
        var merchantOwnerId = Guid.NewGuid();
        var merchantWallet = DomainWallet.Create(merchantOwnerId, _clock.GetUtcNow());
        await _wallets.CreateOrGetAsync(merchantWallet, CancellationToken.None);

        var link = PaymentLink.Create(merchantWallet.Id, 2500m, "Pago", null, PaymentLinkType.SingleUse, _clock.GetUtcNow(), merchantOwnerId: merchantOwnerId);
        await _links.CreateAsync(link, CancellationToken.None);

        var ownerId = Guid.NewGuid();
        var maliciousUserId = Guid.NewGuid();
        var card = Card.Create(ownerId, "Dueño Real", "Visa", CardType.Debit, "1234", 12, 2030, _clock.GetUtcNow(), "tok_sec");
        _cards.Add(card);

        var request = new PayWithSavedCardRequest { CardId = card.Id };

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.PayWithSavedCardAsync(link.Id, request, maliciousUserId, null, CancellationToken.None));
    }

    [Fact]
    public async Task PayWithSavedCardAsync_throws_when_payment_link_does_not_exist()
    {
        var payerUserId = Guid.NewGuid();
        var card = Card.Create(payerUserId, "Usuario", "Mastercard", CardType.Debit, "9999", 10, 2029, _clock.GetUtcNow(), "tok_x");
        _cards.Add(card);

        var request = new PayWithSavedCardRequest { CardId = card.Id };

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _service.PayWithSavedCardAsync(Guid.NewGuid(), request, payerUserId, null, CancellationToken.None));
    }

    [Fact]
    public async Task PayWithSavedCardAsync_throws_when_card_is_inactive()
    {
        var merchantOwnerId = Guid.NewGuid();
        var merchantWallet = DomainWallet.Create(merchantOwnerId, _clock.GetUtcNow());
        await _wallets.CreateOrGetAsync(merchantWallet, CancellationToken.None);

        var link = PaymentLink.Create(merchantWallet.Id, 1000m, "Pago", null, PaymentLinkType.SingleUse, _clock.GetUtcNow(), merchantOwnerId: merchantOwnerId);
        await _links.CreateAsync(link, CancellationToken.None);

        var payerUserId = Guid.NewGuid();
        var card = Card.Create(payerUserId, "Usuario", "Mastercard", CardType.Debit, "9999", 10, 2029, _clock.GetUtcNow(), "tok_x");
        card.Deactivate();
        _cards.Add(card);

        var request = new PayWithSavedCardRequest { CardId = card.Id };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.PayWithSavedCardAsync(link.Id, request, payerUserId, null, CancellationToken.None));
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Associated_debit_card_uses_wallet_payment_store_for_both_checkout_entry_points(bool publicCheckout)
    {
        var merchant = Guid.NewGuid();
        var link = PaymentLink.Create(Guid.NewGuid(), 2500m, "Compra", null, PaymentLinkType.SingleUse, _clock.GetUtcNow(), merchantOwnerId: merchant);
        await _links.CreateAsync(link, CancellationToken.None);
        var card = Card.Create(_currentUser.Id, "Titular", "VISA DEBITO", CardType.Debit, "1234", 12, 2030, _clock.GetUtcNow());
        _cards.Add(card);
        var receipt = publicCheckout
            ? await _service.ProcessCheckoutAsync(link.Token, new() { PaymentMethod = CheckoutPaymentMethod.Card, CardId = card.Id }, "purchase-key", CancellationToken.None)
            : await _service.PayWithSavedCardAsync(link.Id, new() { CardId = card.Id }, _currentUser.Id, "purchase-key", CancellationToken.None);
        Assert.Equal("Completed", receipt.Status);
        Assert.Equal(new DebitCardPaymentCommand(link.Id, card.Id, _currentUser.Id, "purchase-key"), _debitPayments.LastCommand);
        Assert.Equal(0, _funding.Calls);
    }

    [Fact]
    public async Task Debit_payment_requires_idempotency_key_before_debiting()
    {
        var link = PaymentLink.Create(Guid.NewGuid(), 2500m, "Compra", null, PaymentLinkType.SingleUse, _clock.GetUtcNow(), merchantOwnerId: Guid.NewGuid());
        await _links.CreateAsync(link, CancellationToken.None);
        var card = Card.Create(_currentUser.Id, "Titular", "Visa", CardType.Debit, "1234", 12, 2030, _clock.GetUtcNow());
        _cards.Add(card);
        await Assert.ThrowsAsync<ArgumentException>(() => _service.PayWithSavedCardAsync(link.Id, new() { CardId = card.Id }, _currentUser.Id, null, CancellationToken.None));
        Assert.Null(_debitPayments.LastCommand);
        Assert.Equal(0, _funding.Calls);
    }}
