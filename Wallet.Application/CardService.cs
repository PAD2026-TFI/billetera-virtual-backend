using Wallet.Domain;

namespace Wallet.Application;

public sealed class CardService(
    ICardRepository cardRepository,
    ICardRequestRepository cardRequestRepository,
    IAddressRepository addressRepository,
    TimeProvider clock,
    IUserRepository? userRepository = null,
    IAddressGeocoder? geocoder = null) : ICardService
{
    public async Task<CardRequestDto> CreateCardRequestAsync(Guid userId, CreateCardRequestRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (userId == Guid.Empty)
            throw new ArgumentException("User identifier cannot be empty.", nameof(userId));

        var now = clock.GetUtcNow();

        if (request.AddressId == Guid.Empty)
            throw new ArgumentException("AddressId cannot be empty.");
        if (request.AddressId.HasValue && request.DeliveryAddress is not null)
            throw new ArgumentException("Provide either AddressId or DeliveryAddress, not both.");
        if (!Enum.IsDefined(request.Type)) throw new ArgumentOutOfRangeException(nameof(request.Type));

        Address address;
        var isNewAddress = request.DeliveryAddress is not null;
        if (request.DeliveryAddress is { } input)
        {
            address = Address.CreateDraft(input.Street, input.Number, input.City, input.State,
                input.PostalCode, now, input.Country, userId);
            if (geocoder is not null)
            {
                var result = await geocoder.ValidateAsync(new(address.Street, address.Number, address.City,
                    address.State, address.PostalCode, address.Country), ct);
                if (!result.IsValid || result.Latitude is null || result.Longitude is null)
                    throw new ArgumentException("The delivery address could not be validated.");
                address.MarkValid(result.Latitude.Value, result.Longitude.Value, result.ExternalPlaceId, now);
            }
        }
        else if (request.AddressId.HasValue)
        {
            address = await addressRepository.FindByIdAsync(request.AddressId.Value, userId, ct)
                ?? throw new KeyNotFoundException("Delivery address not found.");
        }
        else
        {
            address = await addressRepository.FindDefaultValidAddressAsync(userId, ct)
                ?? throw new ArgumentException("Provide DeliveryAddress or the AddressId of your saved address.");
        }

        var cardRequest = address.ValidationStatus == AddressValidationStatus.Pending
            ? CardRequest.CreateAwaitingAddressValidation(userId, address, now, request.Type)
            : CardRequest.Create(userId, address, now, request.Type);
        if (isNewAddress)
            await cardRequestRepository.CreateWithAddressAsync(cardRequest, address, ct);
        else
            await cardRequestRepository.CreateAsync(cardRequest, ct);

        return MapRequest(cardRequest, address);
    }

    public async Task<CardRequestDto?> GetCardRequestStatusAsync(Guid id, Guid userId, CancellationToken ct)
    {
        var cardRequest = await cardRequestRepository.FindByIdAsync(id, ct);
        if (cardRequest is null || cardRequest.UserId != userId)
            return null;

        var address = await addressRepository.FindByIdAsync(cardRequest.AddressId, userId, ct);
        return MapRequest(cardRequest, address);
    }

    public async Task<IReadOnlyList<CardDto>> GetUserCardsAsync(Guid userId, CancellationToken ct)
    {
        var cards = await cardRepository.GetByUserIdAsync(userId, ct);
        if (cards.Count == 0)
        {
            var user = userRepository is not null ? await userRepository.FindByIdAsync(userId, ct) : null;
            var holder = user is not null
                ? $"{user.FirstName} {user.LastName}".Trim().ToUpperInvariant()
                : "CLIENTE TITULAR";
            if (string.IsNullOrWhiteSpace(holder)) holder = "CLIENTE TITULAR";

            var now = clock.GetUtcNow();
            var last4 = $"{Random.Shared.Next(1000, 9999)}";
            var defaultCard = Card.Create(
                userId,
                holder,
                "VISA DÉBITO",
                CardType.Debit,
                last4,
                12,
                now.Year + 5,
                now);

            await cardRepository.CreateAsync(defaultCard, ct);
            return [MapCard(defaultCard)];
        }
        return cards.Select(MapCard).ToList();
    }

    public async Task<CardDto> AddCardAsync(Guid userId, CreateCardCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (userId == Guid.Empty)
            throw new ArgumentException("User identifier cannot be empty.", nameof(userId));

        var now = clock.GetUtcNow();

        var card = Card.Create(
            userId,
            command.CardHolderName,
            command.Brand,
            command.Type,
            command.CardNumberLast4,
            command.ExpirationMonth,
            command.ExpirationYear,
            now,
            command.CardToken);

        await cardRepository.CreateAsync(card, ct);

        return MapCard(card);
    }

    public async Task<CardDto?> GetCardByIdAsync(Guid cardId, Guid userId, CancellationToken ct)
    {
        var card = await cardRepository.FindByIdAsync(cardId, ct);
        if (card is null || card.UserId != userId)
            return null;

        return MapCard(card);
    }

    public async Task<IReadOnlyList<CardRequestDto>> GetUserCardRequestsAsync(Guid userId, CancellationToken ct)
    {
        var requests = await cardRequestRepository.GetByUserIdAsync(userId, ct);
        var result = new List<CardRequestDto>(requests.Count);
        foreach (var req in requests.OrderByDescending(r => r.RequestedAt))
        {
            var address = await addressRepository.FindByIdAsync(req.AddressId, userId, ct);
            result.Add(MapRequest(req, address));
        }
        return result;
    }

    private static CardRequestDto MapRequest(CardRequest r, Address? address) =>
        new(r.Id, r.UserId, r.AddressId, r.Type.ToString(), r.Status.ToString(), r.RequestedAt, r.UpdatedAt,
            address is null ? null : new(address.Id, address.Street, address.Number, address.City, address.State, address.PostalCode, address.Country, address.ValidationStatus.ToString()));

    private static CardDto MapCard(Card c) =>
        new(c.Id, c.UserId, c.CardHolderName, c.Brand, c.Type.ToString(), c.CardNumberLast4, c.ExpirationMonth, c.ExpirationYear, c.IsActive, c.CreatedAt);
}
