namespace Wallet.Domain;

public enum PaymentStatus { Created, Pending, Unknown, Approved, Rejected, Cancelled, Error }

// Una intención de carga. Preferencia de checkout y pago externo son identidades diferentes.
public sealed class Payment
{
    public Guid Id { get; private set; }
    public Guid WalletId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "ARS";
    public string Provider { get; private set; } = "MercadoPago";
    public Guid IdempotencyKey { get; private set; }
    public string RequestHash { get; private set; } = "";
    public PaymentStatus Status { get; private set; }
    public string? PreferenceId { get; private set; }
    public string? CheckoutUrl { get; private set; }
    public string? ProviderPaymentId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? CheckoutAttemptedAt { get; private set; }
    public DateTimeOffset? CreditedAt { get; private set; }
    public DateTimeOffset? ProviderUpdatedAt { get; private set; }
    public DateTimeOffset NextCheckAt { get; private set; }
    public int RecoveryAttempts { get; private set; }
    public bool RequiresReview { get; private set; }
    public string? LastIssue { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    private Payment() { }

    public static Payment Create(Guid walletId, decimal amount, Guid key, string hash, DateTimeOffset now)
    {
        MoneyRules.ValidatePositive(amount);
        if (walletId == Guid.Empty || key == Guid.Empty || hash.Length != 64 || !hash.All(Uri.IsHexDigit))
            throw new ArgumentException("Identidad o huella inválida.");
        now = now.ToUniversalTime();
        return new() { Id = Guid.NewGuid(), WalletId = walletId, Amount = amount, IdempotencyKey = key,
            RequestHash = hash, Status = PaymentStatus.Created, CreatedAt = now, UpdatedAt = now, NextCheckAt = now };
    }

    public bool BeginCheckout(DateTimeOffset now)
    {
        if (Status != PaymentStatus.Created || CheckoutAttemptedAt is not null) return false;
        CheckoutAttemptedAt = now.ToUniversalTime();
        SetStatus(PaymentStatus.Unknown, now); // Persistido antes de la llamada externa; nunca repito una preferencia incierta.
        NextCheckAt = now.AddMinutes(1); return true;
    }

    public void AttachCheckout(string preferenceId, string checkoutUrl, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(preferenceId) || preferenceId.Length > 128 || checkoutUrl.Length > 2048)
            throw new ArgumentException("Checkout inválido.");
        PreferenceId = preferenceId; CheckoutUrl = checkoutUrl;
        if (Status is PaymentStatus.Created or PaymentStatus.Unknown) SetStatus(PaymentStatus.Pending, now);
    }

    public void RecordIssue(string code, DateTimeOffset now, bool definitive = false)
    {
        if (Status == PaymentStatus.Approved) { LastIssue = code; RequiresReview = true; UpdatedAt = now.ToUniversalTime(); return; }
        LastIssue = code;
        if (definitive && ProviderPaymentId is null) SetStatus(PaymentStatus.Error, now);
        else if (Status == PaymentStatus.Created) SetStatus(PaymentStatus.Unknown, now);
        ScheduleRecovery(now);
        if (code is "PAYMENT_MISMATCH" or "PAYMENT_LIVE_MODE" or "PAYMENT_COLLECTOR_MISMATCH"
            or "PAYMENT_CURRENCY_MISMATCH" or "PAYMENT_AMOUNT_MISMATCH" or "MULTIPLE_PROVIDER_PAYMENTS" or "PROVIDER_PAYMENT_CONFLICT"
            or "PROVIDER_TEST_ACCOUNTS_UNVERIFIED" or "CREDIT_REQUIRES_REVIEW" or "PROVIDER_STATUS_REQUIRES_REVIEW" or "PROVIDER_SEARCH_REQUIRES_REVIEW")
            RequiresReview = true;
    }

    public void ScheduleRecovery(DateTimeOffset now)
    {
        RecoveryAttempts++; UpdatedAt = now.ToUniversalTime();
        NextCheckAt = now.AddMinutes(Math.Min(60, Math.Pow(2, Math.Min(RecoveryAttempts, 6))));
        RequiresReview = RequiresReview || (RecoveryAttempts >= 10 && Status is (PaymentStatus.Pending or PaymentStatus.Unknown));
    }

    // Se llama únicamente después de verificar el estado actual directamente con el proveedor.
    public bool ApplyVerified(string providerPaymentId, PaymentStatus status, DateTimeOffset now, DateTimeOffset? providerUpdatedAt = null)
    {
        if (string.IsNullOrWhiteSpace(providerPaymentId) || providerPaymentId.Length > 64
            || status is not (PaymentStatus.Pending or PaymentStatus.Approved or PaymentStatus.Rejected or PaymentStatus.Cancelled))
            throw new ArgumentException("Resultado externo inválido.");
        if (Status == PaymentStatus.Approved)
        {
            if (status == PaymentStatus.Approved && ProviderPaymentId != providerPaymentId)
                throw new InvalidOperationException("La intención ya tiene otro pago aprobado asociado.");
            return false;
        }
        var observedAt = (providerUpdatedAt ?? now).ToUniversalTime();
        if (status != PaymentStatus.Approved && ProviderUpdatedAt > observedAt) return false;
        if (status == PaymentStatus.Pending && ProviderPaymentId == providerPaymentId
            && Status is PaymentStatus.Rejected or PaymentStatus.Cancelled) return false;
        ProviderPaymentId = providerPaymentId; ProviderUpdatedAt = observedAt;
        SetStatus(status, now); LastIssue = null; RequiresReview = false;
        if (status == PaymentStatus.Approved) { CreditedAt = now.ToUniversalTime(); return true; }
        return false;
    }

    private void SetStatus(PaymentStatus status, DateTimeOffset now)
    { Status = status; UpdatedAt = now.ToUniversalTime(); }
}

// Recibo de un estado verificado. Se confirma en la misma transacción que el efecto financiero.
public sealed class PaymentReceipt
{
    public Guid Id { get; private set; }
    public Guid PaymentId { get; private set; }
    public string EventKey { get; private set; } = "";
    public DateTimeOffset ProcessedAt { get; private set; }
    private PaymentReceipt() { }
    public static PaymentReceipt Create(Guid paymentId, string eventKey, DateTimeOffset now) =>
        new() { Id = Guid.NewGuid(), PaymentId = paymentId, EventKey = eventKey, ProcessedAt = now.ToUniversalTime() };
}
