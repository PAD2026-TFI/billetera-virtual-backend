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

    public Task<Address?> FindByIdAsync(Guid id, Guid userId, CancellationToken ct)
    {
        return _db.Addresses.FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId, ct);
    }

    public Task<Address?> FindValidAddressAsync(Guid addressId, Guid userId, CancellationToken ct)
    {
        return _db.Addresses.FirstOrDefaultAsync(a => a.Id == addressId && a.UserId == userId && a.ValidationStatus == AddressValidationStatus.Valid, ct);
    }

    public Task<Address?> FindDefaultValidAddressAsync(Guid userId, CancellationToken ct)
    {
        return _db.Addresses.Where(a => a.UserId == userId && a.ValidationStatus == AddressValidationStatus.Valid).OrderByDescending(a => a.CreatedAt).ThenBy(a => a.Id).FirstOrDefaultAsync(ct);
    }
}
