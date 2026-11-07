using System.Collections.Concurrent;
using Wallet.Application;
using Wallet.Domain;
using Xunit;

namespace Wallet.Tests;

public sealed class CardServiceTests
{
    private sealed class InMemoryCardRepository : ICardRepository
    {
        private readonly ConcurrentDictionary<Guid, Card> _cards = new();

        public Task<IReadOnlyList<Card>> GetByUserIdAsync(Guid userId, CancellationToken ct)
        {
            var userCards = _cards.Values.Where(c => c.UserId == userId).ToList();
            return Task.FromResult<IReadOnlyList<Card>>(userCards);
        }

        public Task<Card?> FindByIdAsync(Guid id, CancellationToken ct)
        {
            _cards.TryGetValue(id, out var card);
            return Task.FromResult(card);
        }

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

    private sealed class InMemoryCardRequestRepository : ICardRequestRepository
    {
        private readonly ConcurrentDictionary<Guid, CardRequest> _requests = new();

        public Task CreateAsync(CardRequest request, CancellationToken ct)
        {
            _requests[request.Id] = request;
            return Task.CompletedTask;
        }

        public Action<Address>? AddressCreated { get; set; }
        public Task CreateWithAddressAsync(CardRequest request, Address address, CancellationToken ct)
        {
            AddressCreated?.Invoke(address);
            return CreateAsync(request, ct);
        }

        public Task UpdateAsync(CardRequest request, CancellationToken ct)
        {
            _requests[request.Id] = request;
            return Task.CompletedTask;
        }

        public Task<CardRequest?> FindByIdAsync(Guid id, CancellationToken ct)
        {
            _requests.TryGetValue(id, out var req);
            return Task.FromResult(req);
        }

        public Task<IReadOnlyList<CardRequest>> GetByUserIdAsync(Guid userId, CancellationToken ct)
        {
            var userReqs = _requests.Values.Where(r => r.UserId == userId).ToList();
            return Task.FromResult<IReadOnlyList<CardRequest>>(userReqs);
        }
    }

    private sealed class InMemoryAddressRepository : IAddressRepository
    {
        private readonly List<Address> _addresses = [];
        public void Add(Address address) => _addresses.Add(address);
        public Task<Address?> FindByIdAsync(Guid id, Guid userId, CancellationToken ct) =>
            Task.FromResult(_addresses.FirstOrDefault(a => a.Id == id && a.UserId == userId));
        public Task<Address?> FindValidAddressAsync(Guid addressId, Guid userId, CancellationToken ct) =>
            Task.FromResult(_addresses.FirstOrDefault(a => a.Id == addressId && a.UserId == userId && a.ValidationStatus == AddressValidationStatus.Valid));
        public Task<Address?> FindDefaultValidAddressAsync(Guid userId, CancellationToken ct) =>
            Task.FromResult(_addresses.FirstOrDefault(a => a.UserId == userId && a.ValidationStatus == AddressValidationStatus.Valid));
    }

    private static readonly Guid OwnerId = Guid.NewGuid();
    private readonly InMemoryCardRepository _cardRepo = new();
    private readonly InMemoryCardRequestRepository _requestRepo = new();
    private readonly InMemoryAddressRepository _addressRepo = new();
    private readonly TimeProvider _clock = TimeProvider.System;
    private readonly CardService _service;

    public CardServiceTests()
    {
        _requestRepo.AddressCreated = _addressRepo.Add;
        _service = new CardService(_cardRepo, _requestRepo, _addressRepo, _clock);
    }

    [Fact]
    public async Task CreateCardRequestAsync_defaults_to_debit_when_not_specified()
    {
        var address = Address.CreateDraft("Av. Corrientes", "1234", "CABA", "Buenos Aires", "1043", _clock.GetUtcNow(), userId: OwnerId);
        address.MarkValid(-34.6037m, -58.3816m, "place_123", _clock.GetUtcNow());
        _addressRepo.Add(address);

        var userId = OwnerId;
        var request = new CreateCardRequestRequest { AddressId = address.Id };

        var result = await _service.CreateCardRequestAsync(userId, request, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(userId, result.UserId);
        Assert.Equal(address.Id, result.AddressId);
        Assert.Equal("Debit", result.CardType);
        Assert.Equal("Pending", result.Status);
    }

    [Fact]
    public async Task CreateCardRequestAsync_supports_credit_card()
    {
        var address = Address.CreateDraft("Av. Corrientes", "1234", "CABA", "Buenos Aires", "1043", _clock.GetUtcNow(), userId: OwnerId);
        address.MarkValid(-34.6037m, -58.3816m, "place_123", _clock.GetUtcNow());
        _addressRepo.Add(address);

        var userId = OwnerId;
        var request = new CreateCardRequestRequest { AddressId = address.Id, Type = CardType.Credit };

        var result = await _service.CreateCardRequestAsync(userId, request, CancellationToken.None);

        Assert.Equal("Credit", result.CardType);
        Assert.Equal("Pending", result.Status);
    }

    [Fact]
    public async Task CreateCardRequestAsync_throws_when_address_is_invalid()
    {
        var address = Address.CreateDraft("Av. Corrientes", "1234", "CABA", "Buenos Aires", "1043", _clock.GetUtcNow(), userId: OwnerId);
        address.MarkInvalid(_clock.GetUtcNow());
        _addressRepo.Add(address);

        var userId = OwnerId;
        var request = new CreateCardRequestRequest { AddressId = address.Id };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreateCardRequestAsync(userId, request, CancellationToken.None));
    }

    [Fact]
    public async Task GetCardRequestStatusAsync_returns_tracking_information()
    {
        var address = Address.CreateDraft("Av. Corrientes", "1234", "CABA", "Buenos Aires", "1043", _clock.GetUtcNow(), userId: OwnerId);
        address.MarkValid(-34.6037m, -58.3816m, "place_123", _clock.GetUtcNow());
        _addressRepo.Add(address);

        var userId = OwnerId;
        var created = await _service.CreateCardRequestAsync(userId, new CreateCardRequestRequest { AddressId = address.Id }, CancellationToken.None);

        var status = await _service.GetCardRequestStatusAsync(created.Id, userId, CancellationToken.None);

        Assert.NotNull(status);
        Assert.Equal(created.Id, status.Id);
        Assert.Equal("Pending", status.Status);
    }

    [Fact]
    public async Task GetCardRequestStatusAsync_returns_null_when_user_does_not_own_request()
    {
        var address = Address.CreateDraft("Av. Corrientes", "1234", "CABA", "Buenos Aires", "1043", _clock.GetUtcNow(), userId: OwnerId);
        address.MarkValid(-34.6037m, -58.3816m, "place_123", _clock.GetUtcNow());
        _addressRepo.Add(address);

        var userId = OwnerId;
        var created = await _service.CreateCardRequestAsync(userId, new CreateCardRequestRequest { AddressId = address.Id }, CancellationToken.None);

        var otherUserStatus = await _service.GetCardRequestStatusAsync(created.Id, Guid.NewGuid(), CancellationToken.None);

        Assert.Null(otherUserStatus);
    }

    [Fact]
    public async Task AddCardAsync_and_GetUserCardsAsync_manage_saved_cards()
    {
        var userId = OwnerId;
        var command = new CreateCardCommand
        {
            CardHolderName = "Juan Perez",
            Brand = "Visa",
            Type = CardType.Debit,
            CardNumberLast4 = "4242",
            ExpirationMonth = 12,
            ExpirationYear = 2029
        };

        var added = await _service.AddCardAsync(userId, command, CancellationToken.None);
        Assert.NotNull(added);
        Assert.Equal("Juan Perez", added.CardHolderName);
        Assert.Equal("4242", added.CardNumberLast4);

        var cards = await _service.GetUserCardsAsync(userId, CancellationToken.None);
        Assert.Single(cards);
        Assert.Equal(added.Id, cards[0].Id);
    }

    [Fact]
    public async Task AddCardAsync_throws_for_expired_card()
    {
        var userId = OwnerId;
        var command = new CreateCardCommand
        {
            CardHolderName = "Juan Perez",
            Brand = "Visa",
            Type = CardType.Debit,
            CardNumberLast4 = "4242",
            ExpirationMonth = 1,
            ExpirationYear = 2020
        };

        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            _service.AddCardAsync(userId, command, CancellationToken.None));
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Request_cannot_use_another_users_address(bool explicitAddress)
    {
        var address = Address.CreateDraft("Calle", "1", "Ciudad", "Provincia", "1000", _clock.GetUtcNow(), userId: Guid.NewGuid());
        address.MarkValid(0, 0, null, _clock.GetUtcNow());
        _addressRepo.Add(address);
        var request = new CreateCardRequestRequest { AddressId = explicitAddress ? address.Id : null };
        if (explicitAddress)
            await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.CreateCardRequestAsync(OwnerId, request, CancellationToken.None));
        else
            await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateCardRequestAsync(OwnerId, request, CancellationToken.None));
    }

    [Fact]
    public async Task Default_address_is_selected_only_from_requesting_user()
    {
        var address = Address.CreateDraft("Calle", "1", "Ciudad", "Provincia", "1000", _clock.GetUtcNow(), userId: OwnerId);
        address.MarkValid(0, 0, null, _clock.GetUtcNow());
        _addressRepo.Add(address);
        var result = await _service.CreateCardRequestAsync(OwnerId, new(), CancellationToken.None);
        Assert.Equal(address.Id, result.AddressId);
    }
    [Fact]
    public async Task Inline_address_is_saved_with_owner_and_returned_as_awaiting_validation()
    {
        var result = await _service.CreateCardRequestAsync(OwnerId, new CreateCardRequestRequest
        {
            DeliveryAddress = new() { Street = " Corrientes ", Number = "1234", City = "CABA", State = "Buenos Aires", PostalCode = "1043" }
        }, CancellationToken.None);
        Assert.Equal("AwaitingAddressValidation", result.Status);
        Assert.Equal("Pending", result.DeliveryAddress!.ValidationStatus);
        Assert.Equal("Corrientes", result.DeliveryAddress.Street);
        Assert.Equal(result.AddressId, result.DeliveryAddress.Id);
        var stored = await _addressRepo.FindByIdAsync(result.AddressId, OwnerId, CancellationToken.None);
        Assert.NotNull(stored);
        var status = await _service.GetCardRequestStatusAsync(result.Id, OwnerId, CancellationToken.None);
        Assert.Equal(result.DeliveryAddress, status!.DeliveryAddress);
    }

    [Fact]
    public async Task Address_id_and_inline_address_are_mutually_exclusive() =>
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateCardRequestAsync(OwnerId, new()
        {
            AddressId = Guid.NewGuid(), DeliveryAddress = new()
        }, CancellationToken.None));
}
