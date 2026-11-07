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

    public async Task UpdateAsync(CardRequest request, CancellationToken ct)
    {
        db.CardRequests.Update(request);
        await db.SaveChangesAsync(ct);
    }
}
