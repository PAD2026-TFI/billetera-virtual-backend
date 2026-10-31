using System.Security.Cryptography;

namespace Wallet.Domain;

public sealed class PaymentLink
{
    public Guid Id { get; private set; }
    public Guid DestinationWalletId { get; private set; }
    public Guid? MerchantOwnerId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public Money Amount { get; private set; }
    public string Currency { get; private set; } = Money.DefaultCurrency;
    public string Token { get; private set; } = string.Empty;
    public PaymentLinkStatus Status { get; private set; }
    public PaymentLinkType Type { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    private PaymentLink() { } // Required by EF Core

    private PaymentLink(
        Guid id,
        Guid destinationWalletId,
        Guid? merchantOwnerId,
        string title,
        string? description,
        Money amount,
        string token,
        PaymentLinkType type,
        DateTimeOffset createdAt,
        DateTimeOffset? expiresAt)
    {
        Id = id;
        DestinationWalletId = destinationWalletId;
        MerchantOwnerId = merchantOwnerId;
        Title = title;
        Description = description;
        Amount = amount;
        Currency = amount.Currency;
        Token = token;
        Status = PaymentLinkStatus.Active;
        Type = type;
        CreatedAt = createdAt.ToUniversalTime();
        ExpiresAt = expiresAt?.ToUniversalTime();
    }

    public static PaymentLink Create(
        Guid destinationWalletId,
        Money amount,
        string title,
        string? description,
        PaymentLinkType type,
        DateTimeOffset now,
        TimeSpan? ttl = null,
        Guid? merchantOwnerId = null,
        string? customToken = null)
    {
        if (destinationWalletId == Guid.Empty)
            throw new ArgumentException("Destination wallet identifier cannot be empty.", nameof(destinationWalletId));

        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Title cannot be null or whitespace.", nameof(title));

        if (amount.Amount <= 0)
            throw new ArgumentException("Payment amount must be strictly greater than zero.", nameof(amount));

        DateTimeOffset? expiresAt = ttl.HasValue && ttl.Value > TimeSpan.Zero
            ? now.Add(ttl.Value).ToUniversalTime()
            : null;

        var token = string.IsNullOrWhiteSpace(customToken)
            ? Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant()
            : customToken.Trim().ToLowerInvariant();

        return new PaymentLink(
            Guid.NewGuid(),
            destinationWalletId,
            merchantOwnerId,
            title.Trim(),
            description?.Trim(),
            amount,
            token,
            type,
            now,
            expiresAt);
    }

    public static PaymentLink Create(
        Guid destinationWalletId,
        decimal amount,
        string title,
        string? description,
        PaymentLinkType type,
        DateTimeOffset now,
        TimeSpan? ttl = null,
        Guid? merchantOwnerId = null,
        string? customToken = null)
    {
        return Create(
            destinationWalletId,
            Money.Positive(amount),
            title,
            description,
            type,
            now,
            ttl,
            merchantOwnerId,
            customToken);
    }

    public void EnsureIsPayable(DateTimeOffset now)
    {
        if (Status == PaymentLinkStatus.Completed)
            throw new InvalidOperationException("This payment link has already been completed.");

        if (Status == PaymentLinkStatus.Cancelled)
            throw new InvalidOperationException("This payment link has been cancelled.");

        if (Status == PaymentLinkStatus.Expired)
            throw new InvalidOperationException("This payment link has expired.");

        if (ExpiresAt.HasValue && now.ToUniversalTime() > ExpiresAt.Value)
        {
            Status = PaymentLinkStatus.Expired;
            throw new InvalidOperationException("This payment link has expired.");
        }
    }

    public void MarkCompleted(DateTimeOffset now)
    {
        EnsureIsPayable(now);

        if (Type == PaymentLinkType.SingleUse)
        {
            Status = PaymentLinkStatus.Completed;
        }
        // In MultiUse, status remains Active so additional payments can be received.
    }

    public void Cancel(DateTimeOffset now)
    {
        if (Status == PaymentLinkStatus.Completed)
            throw new InvalidOperationException("Cannot cancel an already completed payment link.");

        Status = PaymentLinkStatus.Cancelled;
    }

    public void Expire(DateTimeOffset now)
    {
        if (Status == PaymentLinkStatus.Completed)
            throw new InvalidOperationException("Cannot expire an already completed payment link.");

        Status = PaymentLinkStatus.Expired;
    }
}
