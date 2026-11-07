using Microsoft.EntityFrameworkCore;
using Wallet.Application;
using Wallet.Domain;

namespace Wallet.Infrastructure;

public sealed class SqlAddressRepository : IAddressRepository
{
    private readonly WalletDbContext _db;

    public SqlAddressRepository(WalletDbContext db)
    {
        _db = db;
    }

    public Task<Address?> FindByIdAsync(Guid id, CancellationToken ct)
    {
        return _db.Addresses.FirstOrDefaultAsync(a => a.Id == id, ct);
    }

    public Task<Address?> FindValidAddressAsync(Guid addressId, CancellationToken ct)
    {
        return _db.Addresses.FirstOrDefaultAsync(a => a.Id == addressId && a.ValidationStatus == AddressValidationStatus.Valid, ct);
    }

    public Task<Address?> FindDefaultValidAddressAsync(CancellationToken ct)
    {
        return _db.Addresses.FirstOrDefaultAsync(a => a.ValidationStatus == AddressValidationStatus.Valid, ct);
    }
}
