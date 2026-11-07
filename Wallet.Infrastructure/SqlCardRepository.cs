using Microsoft.EntityFrameworkCore;
using Wallet.Application;
using Wallet.Domain;

namespace Wallet.Infrastructure;

public sealed class SqlCardRepository(WalletDbContext db) : ICardRepository
{
    public async Task<Card?> FindByIdAsync(Guid id, CancellationToken ct) =>
        await db.Cards.FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<IReadOnlyList<Card>> GetByUserIdAsync(Guid userId, CancellationToken ct) =>
        await db.Cards.Where(c => c.UserId == userId).OrderByDescending(c => c.CreatedAt).ToListAsync(ct);

    public async Task CreateAsync(Card card, CancellationToken ct)
    {
        db.Cards.Add(card);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Card card, CancellationToken ct)
    {
        db.Cards.Update(card);
        await db.SaveChangesAsync(ct);
    }
}
