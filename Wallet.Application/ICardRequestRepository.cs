using Wallet.Domain;

namespace Wallet.Application;

public interface ICardRequestRepository
{
    Task<CardRequest?> FindByIdAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<CardRequest>> GetByUserIdAsync(Guid userId, CancellationToken ct);
    Task CreateAsync(CardRequest request, CancellationToken ct);
    Task CreateWithAddressAsync(CardRequest request, Address address, CancellationToken ct);
    Task UpdateAsync(CardRequest request, CancellationToken ct);
}
