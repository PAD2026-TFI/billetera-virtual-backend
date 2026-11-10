using Microsoft.EntityFrameworkCore;
using Wallet.Application;
using Wallet.Domain;

namespace Wallet.Infrastructure;

public sealed class SqlCardRepository(WalletDbContext db) : ICardRepository, IVirtualCardRepository
{
    public async Task<Card?> FindByIdAsync(Guid id, CancellationToken ct) =>
        await db.Cards.FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<IReadOnlyList<Card>> GetByUserIdAsync(Guid userId, CancellationToken ct) =>
        await db.Cards.Where(c => c.UserId == userId).OrderByDescending(c => c.CreatedAt).ToListAsync(ct);

    public Task<Card?> FindByTokenAsync(string token, CancellationToken ct) =>
        db.Cards.AsNoTracking().SingleOrDefaultAsync(c => c.CardToken == token, ct);

    public async Task<bool> ActivateAsync(Guid cardId, string previousToken, string token, CancellationToken ct) =>
        await db.Cards.Where(c => c.Id == cardId && c.CardToken == previousToken && c.IsActive)
            .ExecuteUpdateAsync(update => update.SetProperty(c => c.CardToken, token), ct) == 1;

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
