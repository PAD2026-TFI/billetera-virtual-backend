namespace Wallet.Domain;

public sealed class Transfer
{
    public static readonly Money MaximumTransferAmount = Money.From(780400m);
    public Guid Id { get; private set; }
    public Guid SourceWalletId { get; private set; }
    public Guid DestinationWalletId { get; private set; }
    public Money Amount { get; private set; }
    public TransferStatus Status { get; private set; }
    public Guid IdempotencyKey { get; private set; }
    public string RequestHash { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }

    public string Currency => Amount.Currency;

    private Transfer() { } 

    private Transfer(
        Guid id,
        Guid sourceWalletId,
        Guid destinationWalletId,
        Money amount,
        TransferStatus status,
        Guid idempotencyKey,
        string requestHash,
        DateTimeOffset createdAt)
    {
        ValidateTransferBoundaries(sourceWalletId, destinationWalletId);
        ValidateAmount(amount);
        ValidateIdempotency(idempotencyKey, requestHash);

        Id = id;
        SourceWalletId = sourceWalletId;
        DestinationWalletId = destinationWalletId;
        Amount = amount;
        Status = status;
        IdempotencyKey = idempotencyKey;
        RequestHash = requestHash.Trim();
        CreatedAt = createdAt.ToUniversalTime();
    }

    public static Transfer Complete(
        Guid sourceWalletId,
        Guid destinationWalletId,
        Money amount,
        Guid idempotencyKey,
        string requestHash,
        DateTimeOffset now)
    {
        return new Transfer(
            Guid.NewGuid(),
            sourceWalletId,
            destinationWalletId,
            amount,
            TransferStatus.Completed,
            idempotencyKey,
            requestHash,
            now);
    }

    public static Transfer Complete(
        Guid sourceWalletId,
        Guid destinationWalletId,
        decimal amount,
        Guid idempotencyKey,
        string requestHash,
        DateTimeOffset now)
    {
        return Complete(
            sourceWalletId,
            destinationWalletId,
            Money.Positive(amount),
            idempotencyKey,
            requestHash,
            now);
    }

    public void ApplyTo(Wallet source, Wallet destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        if (Status != TransferStatus.Completed)
            throw new InvalidOperationException("Only completed transfers can be applied to wallet balances.");

        if (source.Id != SourceWalletId || destination.Id != DestinationWalletId)
            throw new ArgumentException("Provided wallets do not match this transfer's participants.");

        if (source.Currency != Currency || destination.Currency != Currency)
            throw new InvalidOperationException("Wallet currency does not match transfer currency.");

        source.Debit(Amount);
        destination.Credit(Amount);
    }

    public bool MatchesRequest(Guid destinationWalletId, Money amount, string requestHash)
    {
        return Status == TransferStatus.Completed
            && DestinationWalletId == destinationWalletId
            && Amount == amount
            && string.Equals(RequestHash, requestHash?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public bool MatchesRequest(Guid destinationWalletId, decimal amount, string requestHash) =>
        MatchesRequest(destinationWalletId, Money.From(amount, Currency), requestHash);

    public static void ValidateAmount(Money amount)
    {
        if (amount.Amount <= 0)
            throw new ArgumentException("Transfer amount must be strictly greater than zero.", nameof(amount));

        if (amount > MaximumTransferAmount)
            throw new ArgumentException($"Transfer amount exceeds maximum allowed threshold of {MaximumTransferAmount.Amount:0.00} {MaximumTransferAmount.Currency}.", nameof(amount));
    }

    private static void ValidateTransferBoundaries(Guid sourceWalletId, Guid destinationWalletId)
    {
        if (sourceWalletId == Guid.Empty)
            throw new ArgumentException("Source wallet identifier cannot be empty.", nameof(sourceWalletId));

        if (destinationWalletId == Guid.Empty)
            throw new ArgumentException("Destination wallet identifier cannot be empty.", nameof(destinationWalletId));

        if (sourceWalletId == destinationWalletId)
            throw new ArgumentException("Source and destination wallets must be distinct.", nameof(destinationWalletId));
    }

    private static void ValidateIdempotency(Guid idempotencyKey, string requestHash)
    {
        if (idempotencyKey == Guid.Empty)
            throw new ArgumentException("Idempotency key cannot be empty.", nameof(idempotencyKey));

        ArgumentException.ThrowIfNullOrWhiteSpace(requestHash);
        var trimmed = requestHash.Trim();
        if (trimmed.Length != 64 || !trimmed.All(Uri.IsHexDigit))
            throw new ArgumentException("Request hash must be a 64-character SHA-256 hexadecimal string.", nameof(requestHash));
    }
}