namespace Wallet.Application;

public interface ICardService
{
    Task<CardRequestDto> CreateCardRequestAsync(Guid userId, CreateCardRequestRequest request, CancellationToken ct);
    Task<CardRequestDto?> GetCardRequestStatusAsync(Guid id, Guid userId, CancellationToken ct);
    Task<IReadOnlyList<CardDto>> GetUserCardsAsync(Guid userId, CancellationToken ct);
    Task<CardDto> AddCardAsync(Guid userId, CreateCardCommand command, CancellationToken ct);
    Task<CardDto?> GetCardByIdAsync(Guid cardId, Guid userId, CancellationToken ct);
}
