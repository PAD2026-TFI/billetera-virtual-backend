using Wallet.Domain;

namespace Wallet.Application;

public sealed class CardService(
    ICardRepository cardRepository,
    ICardRequestRepository cardRequestRepository,
    IAddressRepository addressRepository,
    TimeProvider clock) : ICardService
{
    public async Task<CardRequestDto> CreateCardRequestAsync(Guid userId, CreateCardRequestRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (userId == Guid.Empty)
            throw new ArgumentException("User identifier cannot be empty.", nameof(userId));

        var now = clock.GetUtcNow();

        Address? address;
        if (request.AddressId.HasValue && request.AddressId.Value != Guid.Empty)
        {
            address = await addressRepository.FindByIdAsync(request.AddressId.Value, ct)
                ?? throw new KeyNotFoundException($"Delivery address '{request.AddressId.Value}' was not found.");
        }
        else
        {
            address = await addressRepository.FindDefaultValidAddressAsync(ct)
                ?? throw new InvalidOperationException("Cannot request a card with an unvalidated address.");
        }

        var cardRequest = CardRequest.Create(userId, address, now, request.Type);
        await cardRequestRepository.CreateAsync(cardRequest, ct);

        return MapRequest(cardRequest);
    }

    public async Task<CardRequestDto?> GetCardRequestStatusAsync(Guid id, Guid userId, CancellationToken ct)
    {
        var cardRequest = await cardRequestRepository.FindByIdAsync(id, ct);
        if (cardRequest is null || cardRequest.UserId != userId)
            return null;

        return MapRequest(cardRequest);
    }

    public async Task<IReadOnlyList<CardDto>> GetUserCardsAsync(Guid userId, CancellationToken ct)
    {
        var cards = await cardRepository.GetByUserIdAsync(userId, ct);
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

    private static CardRequestDto MapRequest(CardRequest r) =>
        new(r.Id, r.UserId, r.AddressId, r.Type.ToString(), r.Status.ToString(), r.RequestedAt, r.UpdatedAt);

    private static CardDto MapCard(Card c) =>
        new(c.Id, c.UserId, c.CardHolderName, c.Brand, c.Type.ToString(), c.CardNumberLast4, c.ExpirationMonth, c.ExpirationYear, c.IsActive, c.CreatedAt);
}
