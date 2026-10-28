using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Wallet.Domain;

namespace Wallet.Application;

public sealed class PaymentSettings
{
    public bool Enabled { get; set; }
    public decimal MaxTopUpAmount { get; set; } = 100000m;
    public string AccessToken { get; set; } = "";
    public string WebhookSecret { get; set; } = "";
    public string CollectorId { get; set; } = "";
    public string PublicBaseUrl { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 15;
    public void Validate()
    {
        if (!Enabled) return;
        MoneyRules.ValidatePositive(MaxTopUpAmount);
        if (string.IsNullOrWhiteSpace(AccessToken) || WebhookSecret.Length < 16
            || !CollectorId.All(char.IsAsciiDigit) || CollectorId.Length is < 1 or > 32
            || !Uri.TryCreate(PublicBaseUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https"
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)
            || uri.AbsolutePath != "/" || TimeoutSeconds is < 1 or > 60)
            throw new InvalidOperationException("Configuración de pagos sandbox incompleta o inválida.");
    }
}
public sealed class PaymentRequest { [Required] public decimal? Amount { get; init; } }
public sealed record CheckoutRequest(Guid Reference, decimal Amount, string Currency);
public sealed record CheckoutResult(string PreferenceId, string CheckoutUrl);
public sealed record VerifiedPayment(string Id, Guid Reference, decimal Amount, string Currency,
    string CollectorId, bool LiveMode, PaymentStatus Status, string EventKey, DateTimeOffset? ProviderUpdatedAt = null, bool TestAccountsVerified = false);
public interface IPaymentGateway
{
    Task<CheckoutResult> StartAsync(CheckoutRequest request, CancellationToken ct);
    Task<VerifiedPayment> GetAsync(string paymentId, CancellationToken ct);
    Task<IReadOnlyList<VerifiedPayment>> SearchAsync(Guid reference, CancellationToken ct);
}
public sealed class GatewayException(string code, bool definitive = false) : Exception(code)
{ public string Code { get; } = code; public bool Definitive { get; } = definitive; }
public sealed class PaymentRejectedException(string code) : Exception(code) { public string Code { get; } = code; }
public sealed record PaymentResponse(Guid Id, Guid WalletId, decimal Amount, string Currency, string Provider,
    string Status, string? CheckoutUrl, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    DateTimeOffset? CreditedAt, bool RequiresReview, string? IssueCode = null)
{
    public static PaymentResponse From(Payment p) => new(p.Id, p.WalletId, p.Amount, p.Currency, p.Provider,
        p.Status.ToString(), p.CheckoutUrl, p.CreatedAt, p.UpdatedAt, p.CreditedAt, p.RequiresReview, p.LastIssue);
}
public sealed record PaymentCreation(Payment Payment, bool Created);
public sealed record PaymentExecution(PaymentResponse Payment, bool Created);
public interface IPaymentStore
{
    Task<PaymentCreation> CreateAsync(Guid userId, decimal amount, Guid key, string hash, CancellationToken ct);
    Task<Payment?> GetOwnedAsync(Guid userId, Guid id, CancellationToken ct);
    Task<Payment?> GetAsync(Guid id, CancellationToken ct);
    Task<bool> BeginCheckoutAsync(Guid id, CancellationToken ct);
    Task AttachCheckoutAsync(Guid id, CheckoutResult checkout, CancellationToken ct);
    Task RecordIssueAsync(Guid id, string code, bool definitive, CancellationToken ct);
    Task ApplyAsync(VerifiedPayment payment, CancellationToken ct);
    Task<IReadOnlyList<Payment>> DueAsync(CancellationToken ct);
    Task ScheduleAsync(Guid id, CancellationToken ct);
}
public sealed class PaymentService(IPaymentStore store, IPaymentGateway gateway, PaymentSettings settings,
    ICurrentUser currentUser)
{
    public async Task<PaymentExecution> StartAsync(PaymentRequest request, string? key, CancellationToken ct)
    {
        EnsureEnabled();
        if (request.Amount is not decimal amount || !Guid.TryParse(key, out var parsed) || parsed == Guid.Empty)
            throw new PaymentRejectedException("INVALID_INPUT");
        try { MoneyRules.ValidatePositive(amount); }
        catch (ArgumentException) { throw new PaymentRejectedException("INVALID_AMOUNT"); }
        if (amount > settings.MaxTopUpAmount) throw new PaymentRejectedException("INVALID_AMOUNT");
        var creation = await store.CreateAsync(currentUser.Id, amount, parsed, Fingerprint(amount), ct);
        var id = creation.Payment.Id;
        if (await store.BeginCheckoutAsync(id, ct))
        {
            try
            {
                var checkout = await gateway.StartAsync(new(id, amount, "ARS"), ct);
                await store.AttachCheckoutAsync(id, checkout, ct);
            }
            catch (GatewayException e) { await store.RecordIssueAsync(id, e.Code, e.Definitive, ct); }
            // Cancelación/caída: la reserva Unknown ya quedó confirmada antes de la llamada externa.
        }
        var actual = await store.GetOwnedAsync(currentUser.Id, id, ct) ?? throw new PaymentRejectedException("PAYMENT_NOT_FOUND");
        return new(PaymentResponse.From(actual), creation.Created);
    }
    public async Task<PaymentResponse> GetAsync(Guid id, CancellationToken ct)
    {
        EnsureEnabled();
        var payment = await store.GetOwnedAsync(currentUser.Id, id, ct) ?? throw new PaymentRejectedException("PAYMENT_NOT_FOUND");
        return PaymentResponse.From(payment);
    }
    private void EnsureEnabled() { if (!settings.Enabled) throw new PaymentRejectedException("PAYMENTS_DISABLED"); }
    public static string Fingerprint(decimal amount) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        $"TopUp|{amount.ToString("0.00", CultureInfo.InvariantCulture)}|ARS")));
}
public sealed class PaymentProcessor(IPaymentStore store, IPaymentGateway gateway, PaymentSettings settings)
{
    public async Task ProcessAsync(string externalId, CancellationToken ct)
    {
        if (!settings.Enabled) throw new PaymentRejectedException("PAYMENTS_DISABLED");
        await VerifyAndApplyAsync(await gateway.GetAsync(externalId, ct), ct);
    }
    public async Task ReconcileAsync(Payment payment, CancellationToken ct)
    {
        try
        {
            var results = await gateway.SearchAsync(payment.Id, ct);
            // Un checkout puede originar varios intentos rechazados. Sólo una aprobación puede acreditarse.
            var approved = results.Where(x => x.Status == PaymentStatus.Approved).ToArray();
            if (approved.Length > 1) throw new PaymentRejectedException("MULTIPLE_PROVIDER_PAYMENTS");
            if (results.Count > 0)
            {
                var selected = approved.SingleOrDefault() ?? results.FirstOrDefault(x => x.Status == PaymentStatus.Pending) ?? results[0];
                var verified = await gateway.GetAsync(selected.Id, ct);
                if (verified.Reference != payment.Id) throw new PaymentRejectedException("PAYMENT_MISMATCH");
                await VerifyAndApplyAsync(verified, ct); // La búsqueda localiza; GET verifica el estado actual.
            }
            else if (payment.ProviderPaymentId is not null) await ProcessAsync(payment.ProviderPaymentId, ct);
            await store.ScheduleAsync(payment.Id, ct);
        }
        catch (GatewayException e) { await store.RecordIssueAsync(payment.Id, e.Code, false, ct); }
        catch (PaymentRejectedException e) { await store.RecordIssueAsync(payment.Id, e.Code, false, ct); }
    }
    private async Task VerifyAndApplyAsync(VerifiedPayment external, CancellationToken ct)
    {
        var local = await store.GetAsync(external.Reference, ct) ?? throw new PaymentRejectedException("PAYMENT_NOT_FOUND");
        var mismatch = external.LiveMode && !external.TestAccountsVerified ? "PAYMENT_LIVE_MODE"
            : external.CollectorId != settings.CollectorId ? "PAYMENT_COLLECTOR_MISMATCH"
            : external.Currency != "ARS" || local.Currency != external.Currency ? "PAYMENT_CURRENCY_MISMATCH"
            : external.Amount != local.Amount ? "PAYMENT_AMOUNT_MISMATCH" : null;
        if (mismatch is not null)
        {
            await store.RecordIssueAsync(local.Id, mismatch, false, ct);
            throw new PaymentRejectedException(mismatch);
        }
        try { await store.ApplyAsync(external, ct); }
        catch (PaymentRejectedException e)
        {
            await store.RecordIssueAsync(local.Id, e.Code, false, ct);
            throw;
        }
    }
}
