using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Wallet.Application;
using Wallet.Domain;

namespace Wallet.Infrastructure;

public sealed class SqlPaymentStore(DbContextOptions<WalletDbContext> options, TimeProvider clock) : IPaymentStore
{
    // Ninguna llamada HTTP se ejecuta dentro de estas transacciones.
    private async Task<T> Run<T>(Func<WalletDbContext, Task<T>> action)
    {
        for (var attempt = 0; ; attempt++)
        {
            await using var db = new WalletDbContext(options);
            try { return await action(db); }
            catch (DbUpdateConcurrencyException) when (attempt < 2) { }
            catch (DbUpdateException e) when (attempt < 2 && e.InnerException is SqlException { Number: 1205 or 2601 or 2627 }) { }
            catch (SqlException e) when (attempt < 2 && e.Number == 1205) { }
            catch (DbUpdateConcurrencyException) { throw new PaymentRejectedException("CONCURRENCY_CONFLICT"); }
            catch (DbUpdateException e) when (e.InnerException is SqlException) { throw new StorageUnavailableException(e); }
            catch (SqlException e) { throw new StorageUnavailableException(e); }
        }
    }
    public Task<PaymentCreation> CreateAsync(Guid userId, decimal amount, Guid key, string hash, CancellationToken ct) => Run(async db =>
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var wallet = await db.Wallets.FromSqlInterpolated(
            $"SELECT * FROM [Wallets] WITH (UPDLOCK, HOLDLOCK) WHERE [UserId] = {userId}")
            .SingleOrDefaultAsync(ct) ?? throw new PaymentRejectedException("WALLET_NOT_FOUND");
        var owner = await db.Users.FromSqlInterpolated(
            $"SELECT * FROM [Users] WITH (HOLDLOCK) WHERE [Id] = {userId}").SingleAsync(ct);
        if (!owner.IsActive) throw new PaymentRejectedException("INVALID_SESSION");
        var previous = await db.Payments.SingleOrDefaultAsync(p => p.WalletId == wallet.Id && p.IdempotencyKey == key, ct);
        if (previous is not null)
        {
            if (previous.RequestHash != hash || previous.Amount != amount || previous.Currency != "ARS")
                throw new PaymentRejectedException("IDEMPOTENCY_CONFLICT");
            await tx.CommitAsync(ct); return new PaymentCreation(previous, false);
        }
        var payment = Payment.Create(wallet.Id, amount, key, hash, clock.GetUtcNow());
        db.Payments.Add(payment); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return new PaymentCreation(payment, true);
    });
    public Task<Payment?> GetOwnedAsync(Guid userId, Guid id, CancellationToken ct) => Run(db =>
        (from p in db.Payments.AsNoTracking() join w in db.Wallets on p.WalletId equals w.Id
         where p.Id == id && w.UserId == userId select p).SingleOrDefaultAsync(ct));
    public Task<Payment?> GetAsync(Guid id, CancellationToken ct) => Run(db => db.Payments.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct));
    private Task<T> Update<T>(Guid id, Func<Payment, T> change, CancellationToken ct) => Run(async db =>
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var payment = await db.Payments.FromSqlInterpolated(
            $"SELECT * FROM [Payments] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {id}")
            .SingleOrDefaultAsync(ct) ?? throw new PaymentRejectedException("PAYMENT_NOT_FOUND");
        var result = change(payment); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return result;
    });
    public Task<bool> BeginCheckoutAsync(Guid id, CancellationToken ct) => Update(id, p => p.BeginCheckout(clock.GetUtcNow()), ct);
    public async Task AttachCheckoutAsync(Guid id, CheckoutResult checkout, CancellationToken ct) =>
        await Update(id, p => { p.AttachCheckout(checkout.PreferenceId, checkout.CheckoutUrl, clock.GetUtcNow()); return true; }, ct);
    public async Task RecordIssueAsync(Guid id, string code, bool definitive, CancellationToken ct) =>
        await Update(id, p => { p.RecordIssue(code, clock.GetUtcNow(), definitive); return true; }, ct);
    public async Task ScheduleAsync(Guid id, CancellationToken ct) =>
        await Update(id, p => { p.ScheduleRecovery(clock.GetUtcNow()); return true; }, ct);
    public Task<IReadOnlyList<Payment>> DueAsync(CancellationToken ct) => Run<IReadOnlyList<Payment>>(async db =>
        await db.Payments.AsNoTracking().Where(p => !p.RequiresReview && p.CheckoutAttemptedAt != null
            && (p.Status == PaymentStatus.Pending || p.Status == PaymentStatus.Unknown
                || p.Status == PaymentStatus.Rejected && p.PreferenceId != null && p.RecoveryAttempts < 10)
            && p.NextCheckAt <= clock.GetUtcNow()).OrderBy(p => p.NextCheckAt).Take(20).ToArrayAsync(ct));

    public async Task ApplyAsync(VerifiedPayment verified, CancellationToken ct) => await Run(async db =>
    {
        // Orden compartido con las demás operaciones monetarias: billetera primero, pago después.
        var candidate = await db.Payments.AsNoTracking().SingleOrDefaultAsync(p => p.Id == verified.Reference, ct)
            ?? throw new PaymentRejectedException("PAYMENT_NOT_FOUND");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var wallet = await db.Wallets.FromSqlInterpolated(
            $"SELECT * FROM [Wallets] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {candidate.WalletId}").SingleAsync(ct);
        var payment = await db.Payments.FromSqlInterpolated(
            $"SELECT * FROM [Payments] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {candidate.Id}").SingleAsync(ct);
        if ((verified.LiveMode && !verified.TestAccountsVerified) || verified.Currency != "ARS" || verified.Amount != payment.Amount)
            throw new PaymentRejectedException("PAYMENT_MISMATCH");
        if (await db.PaymentReceipts.AnyAsync(r => r.EventKey == verified.EventKey, ct)) { await tx.CommitAsync(ct); return true; }
        if (await db.Payments.AnyAsync(p => p.Id != payment.Id && p.Provider == payment.Provider
            && p.ProviderPaymentId == verified.Id, ct)) throw new PaymentRejectedException("PROVIDER_PAYMENT_CONFLICT");
        if (payment.Status == PaymentStatus.Approved && verified.Status == PaymentStatus.Approved && payment.ProviderPaymentId != verified.Id)
            throw new PaymentRejectedException("MULTIPLE_PROVIDER_PAYMENTS");
        if (verified.Status == PaymentStatus.Approved && payment.Status != PaymentStatus.Approved)
        {
            var owner = await db.Users.FromSqlInterpolated(
                $"SELECT * FROM [Users] WITH (HOLDLOCK) WHERE [Id] = {wallet.UserId}").SingleAsync(ct);
            if (!owner.IsActive) throw new PaymentRejectedException("CREDIT_REQUIRES_REVIEW");
            if (wallet.Currency != verified.Currency || verified.Amount > MoneyRules.MaxBalance - wallet.Balance)
                throw new PaymentRejectedException("CREDIT_REQUIRES_REVIEW");
        }
        var now = clock.GetUtcNow();
        if (payment.ApplyVerified(verified.Id, verified.Status, now, verified.ProviderUpdatedAt))
        {
            wallet.Credit(payment.Amount);
            db.Movements.Add(Movement.ForPayment(payment, wallet.Balance, now));
        }
        db.PaymentReceipts.Add(PaymentReceipt.Create(payment.Id, verified.EventKey, now));
        ct.ThrowIfCancellationRequested(); await db.SaveChangesAsync(ct); ct.ThrowIfCancellationRequested();
        await tx.CommitAsync(ct); return true;
    });
}
