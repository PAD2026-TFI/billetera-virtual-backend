using Wallet.Domain;

namespace Wallet.Application;

public interface IAddressRepository
{
    Task<Address?> FindByIdAsync(Guid id, CancellationToken ct);
    Task<Address?> FindValidAddressAsync(Guid addressId, CancellationToken ct);
    Task<Address?> FindDefaultValidAddressAsync(CancellationToken ct);
}
