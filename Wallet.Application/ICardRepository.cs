using Wallet.Domain;

namespace Wallet.Application;

public interface ICardRepository
{
    Task<Card?> FindByIdAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Card>> GetByUserIdAsync(Guid userId, CancellationToken ct);
    Task CreateAsync(Card card, CancellationToken ct);
    Task UpdateAsync(Card card, CancellationToken ct);
}
