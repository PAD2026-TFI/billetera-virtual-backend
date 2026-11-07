namespace Wallet.Domain;

public sealed class Card
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string CardHolderName { get; private set; } = string.Empty;
    public string Brand { get; private set; } = string.Empty;
    public CardType Type { get; private set; }
    public string CardNumberLast4 { get; private set; } = string.Empty;
    public int ExpirationMonth { get; private set; }
    public int ExpirationYear { get; private set; }
    public string CardToken { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private Card() { }

    private Card(
        Guid id,
        Guid userId,
        string cardHolderName,
        string brand,
        CardType type,
        string cardNumberLast4,
        int expirationMonth,
        int expirationYear,
        string cardToken,
        DateTimeOffset now)
    {
        Id = id;
        UserId = userId;
        CardHolderName = cardHolderName;
        Brand = brand;
        Type = type;
        CardNumberLast4 = cardNumberLast4;
        ExpirationMonth = expirationMonth;
        ExpirationYear = expirationYear;
        CardToken = cardToken;
        IsActive = true;
        CreatedAt = now.ToUniversalTime();
    }

    public static Card Create(
        Guid userId,
        string cardHolderName,
        string brand,
        CardType type,
        string cardNumberLast4,
        int expirationMonth,
        int expirationYear,
        DateTimeOffset now,
        string? cardToken = null)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("User identifier cannot be empty.", nameof(userId));

        ArgumentException.ThrowIfNullOrWhiteSpace(cardHolderName);
        ArgumentException.ThrowIfNullOrWhiteSpace(brand);

        var trimmedLast4 = cardNumberLast4?.Trim() ?? string.Empty;
        if (trimmedLast4.Length != 4 || !trimmedLast4.All(c => c is >= '0' and <= '9'))
            throw new ArgumentException("Last 4 digits must contain exactly 4 numeric characters.", nameof(cardNumberLast4));

        if (expirationMonth is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(expirationMonth), "Expiration month must be between 1 and 12.");

        if (expirationYear is < 1 or > 9999 || expirationYear < now.UtcDateTime.Year || (expirationYear == now.UtcDateTime.Year && expirationMonth < now.UtcDateTime.Month))
            throw new ArgumentOutOfRangeException(nameof(expirationYear), "Card expiration must be a valid year and must not precede the current UTC month.");

        var token = string.IsNullOrWhiteSpace(cardToken)
            ? $"tok_{Guid.NewGuid():N}"
            : cardToken.Trim();

        return new Card(
            Guid.NewGuid(),
            userId,
            cardHolderName.Trim(),
            brand.Trim(),
            type,
            trimmedLast4,
            expirationMonth,
            expirationYear,
            token,
            now);
    }

    public void Deactivate()
    {
        IsActive = false;
    }

    public void Activate()
    {
        IsActive = true;
    }

    public bool IsExpired(DateTimeOffset now)
    {
        var utc = now.ToUniversalTime();
        return ExpirationYear < utc.Year || (ExpirationYear == utc.Year && ExpirationMonth < utc.Month);
    }
}
