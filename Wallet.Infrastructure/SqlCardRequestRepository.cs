using Microsoft.EntityFrameworkCore;
using Wallet.Application;
using Wallet.Domain;

namespace Wallet.Infrastructure;

public sealed class SqlCardRequestRepository(WalletDbContext db) : ICardRequestRepository
{
    public async Task<CardRequest?> FindByIdAsync(Guid id, CancellationToken ct) =>
        await db.CardRequests.FirstOrDefaultAsync(cr => cr.Id == id, ct);

    public async Task<IReadOnlyList<CardRequest>> GetByUserIdAsync(Guid userId, CancellationToken ct) =>
        await db.CardRequests.Where(cr => cr.UserId == userId).OrderByDescending(cr => cr.RequestedAt).ToListAsync(ct);

    public async Task CreateAsync(CardRequest request, CancellationToken ct)
    {
        db.CardRequests.Add(request);
        await db.SaveChangesAsync(ct);
    }

    public async Task CreateWithAddressAsync(CardRequest request, Address address, CancellationToken ct)
    {
        if (address.Id != request.AddressId || address.UserId != request.UserId)
            throw new ArgumentException("The address must match the request and its owner.");
        db.Addresses.Add(address);
        db.CardRequests.Add(request);
        // EF commits both inserts in the same transaction.
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(CardRequest request, CancellationToken ct)
    {
        db.CardRequests.Update(request);
        await db.SaveChangesAsync(ct);
    }
}
