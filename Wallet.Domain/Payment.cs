namespace Wallet.Domain;

public sealed class Payment
{
    public Guid Id { get; private set; }
    public Guid PaymentLinkId { get; private set; }
    public Guid MerchantOwnerId { get; private set; }
    public Guid DestinationWalletId { get; private set; }
    public Guid? PayerUserId { get; private set; }
    public Guid? PayerWalletId { get; private set; }
    public Money Amount { get; private set; }
    public string Currency { get; private set; } = Money.DefaultCurrency;
    public PaymentMethod Method { get; private set; }
    public PaymentStatus Status { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public string? RequestHash { get; private set; }
    public Guid? TransferId { get; private set; }
    public string? CardBrand { get; private set; }
    public string? CardLast4 { get; private set; }
    public string? CardHolderName { get; private set; }
    public string? AuthorizationCode { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    private Payment() { } // EF Core

    private Payment(
        Guid id,
        Guid paymentLinkId,
        Guid merchantOwnerId,
        Guid destinationWalletId,
        Guid? payerUserId,
        Guid? payerWalletId,
        Money amount,
        PaymentMethod method,
        PaymentStatus status,
        string? idempotencyKey,
        string? requestHash,
        string? cardBrand,
        string? cardLast4,
        string? cardHolderName,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty) throw new ArgumentException("Payment identifier cannot be empty.", nameof(id));
        if (paymentLinkId == Guid.Empty) throw new ArgumentException("Payment link identifier cannot be empty.", nameof(paymentLinkId));
        if (merchantOwnerId == Guid.Empty) throw new ArgumentException("Merchant owner identifier cannot be empty.", nameof(merchantOwnerId));
        if (destinationWalletId == Guid.Empty) throw new ArgumentException("Destination wallet identifier cannot be empty.", nameof(destinationWalletId));
        if (amount.Amount <= 0) throw new ArgumentException("Payment amount must be strictly greater than zero.", nameof(amount));

        Id = id;
        PaymentLinkId = paymentLinkId;
        MerchantOwnerId = merchantOwnerId;
        DestinationWalletId = destinationWalletId;
        PayerUserId = payerUserId;
        PayerWalletId = payerWalletId;
        Amount = amount;
        Currency = amount.Currency;
        Method = method;
        Status = status;
        IdempotencyKey = idempotencyKey?.Trim();
        RequestHash = requestHash?.Trim();
        CardBrand = cardBrand?.Trim();
        CardLast4 = cardLast4?.Trim();
        CardHolderName = cardHolderName?.Trim();
        CreatedAt = createdAt.ToUniversalTime();
    }

    public static Payment CreateForWallet(
        Guid paymentLinkId,
        Guid merchantOwnerId,
        Guid destinationWalletId,
        Guid payerUserId,
        Guid payerWalletId,
        Money amount,
        string? idempotencyKey,
        string? requestHash,
        DateTimeOffset now)
    {
        if (payerUserId == Guid.Empty) throw new ArgumentException("Payer user identifier cannot be empty.", nameof(payerUserId));
        if (payerWalletId == Guid.Empty) throw new ArgumentException("Payer wallet identifier cannot be empty.", nameof(payerWalletId));
        if (destinationWalletId == payerWalletId) throw new ArgumentException("Source and destination wallet cannot be the same.", nameof(payerWalletId));

        return new Payment(
            Guid.NewGuid(),
            paymentLinkId,
            merchantOwnerId,
            destinationWalletId,
            payerUserId,
            payerWalletId,
            amount,
            PaymentMethod.Wallet,
            PaymentStatus.Processing,
            idempotencyKey,
            requestHash,
            null,
            null,
            null,
            now);
    }

    public static Payment CreateForCard(
        Guid paymentLinkId,
        Guid merchantOwnerId,
        Guid destinationWalletId,
        Money amount,
        string cardBrand,
        string cardLast4,
        string? cardHolderName,
        Guid? payerUserId,
        string? idempotencyKey,
        string? requestHash,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(cardBrand)) throw new ArgumentException("Card brand cannot be empty.", nameof(cardBrand));
        if (string.IsNullOrWhiteSpace(cardLast4) || cardLast4.Length != 4 || !cardLast4.All(char.IsDigit))
            throw new ArgumentException("Card last 4 must be exactly 4 digits.", nameof(cardLast4));

        return new Payment(
            Guid.NewGuid(),
            paymentLinkId,
            merchantOwnerId,
            destinationWalletId,
            payerUserId,
            null,
            amount,
            PaymentMethod.Card,
            PaymentStatus.Processing,
            idempotencyKey,
            requestHash,
            cardBrand,
            cardLast4,
            cardHolderName,
            now);
    }

    public void MarkSucceeded(Guid? transferId, string? authorizationCode, DateTimeOffset now)
    {
        if (Status != PaymentStatus.Processing && Status != PaymentStatus.Pending)
            throw new InvalidOperationException($"Cannot succeed payment from status '{Status}'.");

        Status = PaymentStatus.Succeeded;
        TransferId = transferId;
        AuthorizationCode = authorizationCode?.Trim();
        CompletedAt = now.ToUniversalTime();
    }

    public void MarkFailed(string reason, DateTimeOffset now)
    {
        if (Status != PaymentStatus.Processing && Status != PaymentStatus.Pending)
            throw new InvalidOperationException($"Cannot fail payment from status '{Status}'.");

        Status = PaymentStatus.Failed;
        FailureReason = reason?.Trim();
        CompletedAt = now.ToUniversalTime();
    }

    public void Cancel(DateTimeOffset now)
    {
        if (Status == PaymentStatus.Succeeded)
            throw new InvalidOperationException("Cannot cancel an already succeeded payment.");

        Status = PaymentStatus.Cancelled;
        CompletedAt = now.ToUniversalTime();
    }
}
