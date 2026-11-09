using Microsoft.EntityFrameworkCore;
using Wallet.Application;

namespace Wallet.Infrastructure;

public sealed class SqlTransferCounterpartyResolver(WalletDbContext db) : ITransferCounterpartyResolver
{
    public async Task<IReadOnlyDictionary<Guid, CounterpartyInfo>> ResolveCounterpartiesAsync(
        Guid currentWalletId,
        IEnumerable<Guid> transferIds,
        IEnumerable<Guid> fundingIds,
        CancellationToken ct)
    {
        try
        {
            var result = new Dictionary<Guid, CounterpartyInfo>();
            var tIds = transferIds.Distinct().ToList();
            var fIds = fundingIds.Distinct().ToList();

            if (tIds.Count > 0)
            {
                var transfers = await db.Transfers.AsNoTracking()
                    .Where(t => tIds.Contains(t.Id))
                    .Select(t => new { t.Id, t.SourceWalletId, t.DestinationWalletId })
                    .ToListAsync(ct);

                var otherWalletIds = transfers
                    .Select(t => t.SourceWalletId == currentWalletId ? t.DestinationWalletId : t.SourceWalletId)
                    .Distinct()
                    .ToList();

                var wallets = otherWalletIds.Count > 0
                    ? await db.Wallets.AsNoTracking()
                        .Where(w => otherWalletIds.Contains(w.Id))
                        .Select(w => new { w.Id, w.OwnerId, w.Alias })
                        .ToListAsync(ct)
                    : [];

                var ownerIds = wallets.Select(w => w.OwnerId).Distinct().ToList();

                var users = ownerIds.Count > 0
                    ? await db.Users.AsNoTracking()
                        .Where(u => ownerIds.Contains(u.Id))
                        .Select(u => new { u.Id, FirstName = u.FirstName, LastName = u.LastName })
                        .ToListAsync(ct)
                    : [];

                var clients = ownerIds.Count > 0
                    ? await db.ExternalClients.AsNoTracking()
                        .Where(c => ownerIds.Contains(c.Id))
                        .Select(c => new { c.Id, Name = c.CompanyName })
                        .ToListAsync(ct)
                    : [];

                var userMap = users.ToDictionary(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim());
                var clientMap = clients.ToDictionary(c => c.Id, c => c.Name);

                var walletInfoMap = wallets.ToDictionary(
                    w => w.Id,
                    w =>
                    {
                        var name = userMap.TryGetValue(w.OwnerId, out var un) && !string.IsNullOrWhiteSpace(un)
                            ? un
                            : (clientMap.TryGetValue(w.OwnerId, out var cn) && !string.IsNullOrWhiteSpace(cn) ? cn : w.Alias);
                        return new { Name = name, Alias = w.Alias };
                    });

                var payments = await db.Payments.AsNoTracking()
                    .Where(p => p.TransferId.HasValue && tIds.Contains(p.TransferId.Value))
                    .Select(p => new { p.TransferId, p.PaymentLinkId, p.CardHolderName, p.PayerUserId })
                    .ToListAsync(ct);

                var linkIds = payments.Select(p => p.PaymentLinkId).Distinct().ToList();
                var links = linkIds.Count > 0
                    ? await db.PaymentLinks.AsNoTracking()
                        .Where(l => linkIds.Contains(l.Id))
                        .Select(l => new { l.Id, l.Title })
                        .ToDictionaryAsync(l => l.Id, l => l.Title, ct)
                    : new Dictionary<Guid, string>();

                var paymentMap = payments
                    .Where(p => p.TransferId.HasValue)
                    .ToDictionary(p => p.TransferId!.Value, p => p);

                foreach (var t in transfers)
                {
                    var otherWalletId = t.SourceWalletId == currentWalletId ? t.DestinationWalletId : t.SourceWalletId;
                    walletInfoMap.TryGetValue(otherWalletId, out var baseInfo);
                    var counterpartyName = baseInfo?.Name ?? "Cuenta padi";
                    var counterpartyAlias = baseInfo?.Alias ?? "padi";
                    string? concept = null;
                    var isPayment = false;

                    if (paymentMap.TryGetValue(t.Id, out var payment))
                    {
                        isPayment = true;
                        if (links.TryGetValue(payment.PaymentLinkId, out var title))
                        {
                            concept = title;
                        }

                        if (t.DestinationWalletId == currentWalletId && !string.IsNullOrWhiteSpace(payment.CardHolderName))
                        {
                            counterpartyName = payment.CardHolderName.Trim();
                        }
                    }

                    result[t.Id] = new CounterpartyInfo(counterpartyName, counterpartyAlias, concept, isPayment);
                }
            }

            if (fIds.Count > 0)
            {
                var fundings = await db.DevelopmentFundings.AsNoTracking()
                    .Where(f => fIds.Contains(f.Id))
                    .Select(f => new { f.Id, f.IdempotencyKey })
                    .ToListAsync(ct);

                var paymentGuids = fundings.Select(f => f.IdempotencyKey).Distinct().ToList();
                if (paymentGuids.Count > 0)
                {
                    var payments = await db.Payments.AsNoTracking()
                        .Where(p => paymentGuids.Contains(p.Id))
                        .Select(p => new { p.Id, p.PaymentLinkId, p.CardHolderName, p.PayerUserId })
                        .ToListAsync(ct);

                    var linkIds = payments.Select(p => p.PaymentLinkId).Distinct().ToList();
                    var links = linkIds.Count > 0
                        ? await db.PaymentLinks.AsNoTracking()
                            .Where(l => linkIds.Contains(l.Id))
                            .Select(l => new { l.Id, l.Title })
                            .ToDictionaryAsync(l => l.Id, l => l.Title, ct)
                        : new Dictionary<Guid, string>();

                    var paymentById = payments.ToDictionary(p => p.Id, p => p);

                    foreach (var f in fundings)
                    {
                        if (paymentById.TryGetValue(f.IdempotencyKey, out var payment))
                        {
                            links.TryGetValue(payment.PaymentLinkId, out var title);
                            var payerName = !string.IsNullOrWhiteSpace(payment.CardHolderName)
                                ? payment.CardHolderName.Trim()
                                : "Cliente pasarela";
                            result[f.Id] = new CounterpartyInfo(payerName, "padi.checkout", title, IsPayment: true);
                        }
                    }
                }
            }

            return result;
        }
        catch
        {
            return new Dictionary<Guid, CounterpartyInfo>();
        }
    }
}