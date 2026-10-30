namespace Wallet.Domain;

public sealed class DevelopmentFunding
{
    public Guid Id { get; private set; }
    public Guid WalletId { get; private set; }
    public Money Amount { get; private set; }
    public Guid IdempotencyKey { get; private set; }
    public string RequestHash { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }

    public string Currency => Amount.Currency;

    private DevelopmentFunding() { }

    private DevelopmentFunding(
        Guid id,
        Guid walletId,
        Money amount,
        Guid idempotencyKey,
        string requestHash,
        DateTimeOffset createdAt)
    {
        if (walletId == Guid.Empty)
            throw new ArgumentException("Wallet identifier cannot be empty.", nameof(walletId));

        if (idempotencyKey == Guid.Empty)
            throw new ArgumentException("Idempotency key cannot be empty.", nameof(idempotencyKey));

        if (amount.Amount <= 0)
            throw new ArgumentException("Funding amount must be strictly greater than zero.", nameof(amount));

        ArgumentException.ThrowIfNullOrWhiteSpace(requestHash);
        var trimmed = requestHash.Trim();
        if (trimmed.Length != 64 || !trimmed.All(Uri.IsHexDigit))
            throw new ArgumentException("Request hash must be a 64-character SHA-256 hexadecimal string.", nameof(requestHash)); 

        Id = id;
        WalletId = walletId;
        Amount = amount;
        IdempotencyKey = idempotencyKey;
        RequestHash = trimmed;
        CreatedAt = createdAt.ToUniversalTime();
    }

    public static DevelopmentFunding Create(
        Guid walletId,
        Money amount,
        Guid idempotencyKey,
        string requestHash,
        DateTimeOffset now)
    {
        return new DevelopmentFunding(
            Guid.NewGuid(),
            walletId,
            amount,
            idempotencyKey,
            requestHash,
            now);
    }

    public static DevelopmentFunding Create(
        Guid walletId,
        decimal amount,
        Guid idempotencyKey,
        string requestHash,
        DateTimeOffset now)
    {
        return Create(
            walletId,
            Money.Positive(amount),
            idempotencyKey,
            requestHash,
            now);
    }

    public bool MatchesRequest(Guid walletId, Money amount, string requestHash)
    {
        return WalletId == walletId
            && Amount == amount
            && string.Equals(RequestHash, requestHash?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public bool MatchesRequest(Guid walletId, decimal amount, string requestHash) =>
        MatchesRequest(walletId, Money.From(amount, Currency), requestHash);
}