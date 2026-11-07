using Wallet.Domain;

namespace Wallet.Application;

public interface IAddressRepository
{
    Task<Address?> FindByIdAsync(Guid id, Guid userId, CancellationToken ct);
    Task<Address?> FindValidAddressAsync(Guid addressId, Guid userId, CancellationToken ct);
    Task<Address?> FindDefaultValidAddressAsync(Guid userId, CancellationToken ct);
}
