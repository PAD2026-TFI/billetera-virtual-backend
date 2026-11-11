using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Wallet.Application;
using Wallet.Domain;

namespace Wallet.Infrastructure;

public sealed class SqlDebitCardPaymentStore(DbContextOptions<WalletDbContext> options, TimeProvider clock) : IDebitCardPaymentStore
{
    public async Task<CheckoutReceiptDto> ExecuteAsync(DebitCardPaymentCommand command, CancellationToken ct)
    {
        if (command.UserId == Guid.Empty || command.CardId == Guid.Empty || command.PaymentLinkId == Guid.Empty
            || string.IsNullOrWhiteSpace(command.IdempotencyKey) || command.IdempotencyKey.Length > 128)
            throw new ArgumentException("Valid user, card, payment link and Idempotency-Key are required.");

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await using var db = new WalletDbContext(options);
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                var link = await db.PaymentLinks.FromSqlInterpolated(
                    $"SELECT * FROM [PaymentLinks] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {command.PaymentLinkId}")
                    .SingleOrDefaultAsync(ct) ?? throw new KeyNotFoundException("Payment link not found.");
                var merchant = link.MerchantOwnerId ?? throw new InvalidOperationException("Payment link has no merchant.");
                var hash = Fingerprint(command, link.Amount.Amount);
                // Reserve the indexed merchant/key range before checking funds. Concurrent uses of
                // the same key on different links must resolve as replay/conflict, not as a new debit.
                var previous = await db.Payments.FromSqlInterpolated(
                    $"SELECT * FROM [Payments] WITH (UPDLOCK, HOLDLOCK) WHERE [MerchantOwnerId] = {merchant} AND [IdempotencyKey] = {command.IdempotencyKey}")
                    .AsNoTracking().SingleOrDefaultAsync(ct);
                if (previous is not null)
                {
                    if (previous.RequestHash != hash || previous.PayerUserId != command.UserId)
                        throw new TransferRejectedException("IDEMPOTENCY_CONFLICT");
                    return Receipt(previous, link.Token, true);
                }

                link.EnsureIsPayable(clock.GetUtcNow());
                if (link.Type == PaymentLinkType.SingleUse && await db.Payments.AnyAsync(
                    p => p.PaymentLinkId == link.Id && (p.Status == PaymentStatus.Processing || p.Status == PaymentStatus.Succeeded), ct))
                    throw new InvalidOperationException("This payment link has already been completed.");

                var card = await db.Cards.FromSqlInterpolated(
                    $"SELECT * FROM [Cards] WITH (HOLDLOCK) WHERE [Id] = {command.CardId}").SingleOrDefaultAsync(ct);
                if (card is null || card.UserId != command.UserId ||
                    (command.VirtualToken is not null && card.CardToken != command.VirtualToken))
                    throw new UnauthorizedAccessException("Card does not belong to the authenticated user.");
                if (card.Type != CardType.Debit || !card.IsActive || card.IsExpired(clock.GetUtcNow()))
                    throw new InvalidOperationException("An active, unexpired debit card is required.");
                Transfer.ValidateAmount(link.Amount);
                var sourceId = await db.Wallets.AsNoTracking().Where(w => w.OwnerId == command.UserId)
                    .Select(w => w.Id).SingleOrDefaultAsync(ct);
                if (sourceId == Guid.Empty)
                {
                    sourceId = await db.Users.AsNoTracking().Where(u => u.Id == command.UserId && u.WalletId != null)
                        .Select(u => u.WalletId!.Value).SingleOrDefaultAsync(ct);
                }
                if (sourceId == Guid.Empty) throw new TransferRejectedException("SOURCE_WALLET_NOT_FOUND");
                if (sourceId == link.DestinationWalletId) throw new TransferRejectedException("SAME_WALLET");

                var now = clock.GetUtcNow();
                var payment = Payment.CreateForDebitCard(link.Id, merchant, link.DestinationWalletId, link.Amount,
                    card, sourceId, command.IdempotencyKey, hash, now);
                var transfer = await SqlTransferStore.ExecuteInTransactionAsync(db,
                    new(command.UserId, link.DestinationWalletId, link.Amount, payment.Id),
                    TransferService.Fingerprint(link.DestinationWalletId, link.Amount.Amount), clock, ct);
                payment.MarkSucceeded(transfer.Transfer.Id, "WALLET-DEBIT", now);
                link.MarkCompleted(now);
                db.Payments.Add(payment);
                await db.SaveChangesAsync(ct);
                ct.ThrowIfCancellationRequested();
                await transaction.CommitAsync(ct);
                return Receipt(payment, link.Token, false);
            }
            catch (DbUpdateConcurrencyException) when (attempt < 2) { }
            catch (DbUpdateException e) when (attempt < 2 && e.InnerException is SqlException { Number: 2601 or 2627 or 1205 }) { }
            catch (SqlException e) when (attempt < 2 && e.Number == 1205) { }
            catch (DbUpdateConcurrencyException) { throw new TransferRejectedException("CONCURRENCY_CONFLICT"); }
            catch (DbUpdateException e) when (e.InnerException is SqlException { Number: 2601 or 2627 or 1205 })
            { throw new TransferRejectedException("CONCURRENCY_CONFLICT"); }
            catch (SqlException e) when (e.Number == 1205) { throw new TransferRejectedException("CONCURRENCY_CONFLICT"); }
            catch (DbUpdateException e) when (e.InnerException is SqlException) { throw new StorageUnavailableException(e); }
            catch (SqlException e) { throw new StorageUnavailableException(e); }
        }
    }

    private static string Fingerprint(DebitCardPaymentCommand command, decimal amount) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"debit|{command.PaymentLinkId:N}|{command.CardId:N}|{command.UserId:N}|{amount.ToString("0.00", CultureInfo.InvariantCulture)}|ARS")));

    private static CheckoutReceiptDto Receipt(Payment payment, string token, bool replayed) =>
        new(payment.Id, token, payment.Amount.Amount, payment.Currency, payment.DestinationWalletId,
            payment.Method.ToString(), payment.Status == PaymentStatus.Succeeded ? "Completed" : payment.Status.ToString(),
            payment.CompletedAt ?? payment.CreatedAt, payment.AuthorizationCode, replayed);
}
