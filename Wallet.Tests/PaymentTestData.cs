using System.Collections.Concurrent;
using Wallet.Application;
using Wallet.Domain;

namespace Wallet.Tests;
internal static class PaymentTestData
{
    internal static PaymentSettings Settings() => new() { Enabled = true, AccessToken = "test-placeholder-not-a-credential",
        WebhookSecret = "unit-test-secret-not-real", CollectorId = "123456", PublicBaseUrl = "https://wallet.example.com" };
    internal static VerifiedPayment Verified(Payment payment, PaymentStatus status = PaymentStatus.Approved,
        string id = "123456789", string? eventKey = null) => new(id, payment.Id, payment.Amount, "ARS", "123456", false,
        status, eventKey ?? PaymentService.Fingerprint(decimal.Parse(id, System.Globalization.CultureInfo.InvariantCulture)));
}
internal sealed class TestPaymentGateway : IPaymentGateway
{
    internal int Starts;
    internal int Gets;
    internal Func<CheckoutRequest, CheckoutResult>? Start { get; set; }
    internal VerifiedPayment? Result;
    internal IReadOnlyList<VerifiedPayment>? Results { get; set; }
    internal Exception? Failure;
    public Task<CheckoutResult> StartAsync(CheckoutRequest request, CancellationToken ct)
    {
        Interlocked.Increment(ref Starts);
        if (Failure is not null) throw Failure;
        return Task.FromResult(Start?.Invoke(request) ?? new("preference-" + request.Reference,
            "https://sandbox.mercadopago.com.ar/checkout/v1/redirect?pref_id=test"));
    }
    public Task<VerifiedPayment> GetAsync(string id, CancellationToken ct)
    { Gets++; if (Failure is not null) throw Failure; return Task.FromResult(Results?.SingleOrDefault(x => x.Id == id) ?? Result ?? throw new InvalidOperationException()); }
    public Task<IReadOnlyList<VerifiedPayment>> SearchAsync(Guid reference, CancellationToken ct)
    {
        if (Failure is not null) throw Failure;
        return Task.FromResult<IReadOnlyList<VerifiedPayment>>(Results ?? (Result is null ? [] : [Result]));
    }
}
internal sealed class TestPaymentStore : IPaymentStore
{
    internal Guid OwnerId;
    internal Guid WalletId = Guid.NewGuid();
    internal ConcurrentDictionary<Guid, Payment> Items = new();
    internal int Credits;
    internal readonly object Gate = new();
    internal bool FailApply;
    public Task<PaymentCreation> CreateAsync(Guid user, decimal amount, Guid key, string hash, CancellationToken ct)
    {
        lock (Gate)
        {
            var old = Items.Values.SingleOrDefault(p => p.IdempotencyKey == key);
            if (old is not null)
            {
                if (old.RequestHash != hash) throw new PaymentRejectedException("IDEMPOTENCY_CONFLICT");
                return Task.FromResult(new PaymentCreation(old, false));
            }
            var p = Payment.Create(WalletId, amount, key, hash, DateTimeOffset.UtcNow); Items[p.Id] = p;
            return Task.FromResult(new PaymentCreation(p, true));
        }
    }
    public Task<Payment?> GetOwnedAsync(Guid user, Guid id, CancellationToken ct) => Task.FromResult(user == OwnerId ? Items.GetValueOrDefault(id) : null);
    public Task<Payment?> GetAsync(Guid id, CancellationToken ct) => Task.FromResult(Items.GetValueOrDefault(id));
    public Task<bool> BeginCheckoutAsync(Guid id, CancellationToken ct)
    { lock (Gate) return Task.FromResult(Items[id].BeginCheckout(DateTimeOffset.UtcNow)); }
    public Task AttachCheckoutAsync(Guid id, CheckoutResult checkout, CancellationToken ct)
    { Items[id].AttachCheckout(checkout.PreferenceId, checkout.CheckoutUrl, DateTimeOffset.UtcNow); return Task.CompletedTask; }
    public Task RecordIssueAsync(Guid id, string code, bool definitive, CancellationToken ct)
    { Items[id].RecordIssue(code, DateTimeOffset.UtcNow, definitive); return Task.CompletedTask; }
    public Task ApplyAsync(VerifiedPayment result, CancellationToken ct)
    {
        if (FailApply) throw new StorageUnavailableException(new InvalidOperationException());
        lock (Gate) { if (Items[result.Reference].ApplyVerified(result.Id, result.Status, DateTimeOffset.UtcNow, result.ProviderUpdatedAt)) Credits++; }
        return Task.CompletedTask;
    }
    public Task<IReadOnlyList<Payment>> DueAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<Payment>>([]);
    public Task ScheduleAsync(Guid id, CancellationToken ct) { Items[id].ScheduleRecovery(DateTimeOffset.UtcNow); return Task.CompletedTask; }
}
