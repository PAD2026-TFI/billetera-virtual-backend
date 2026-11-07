using System.ComponentModel.DataAnnotations;
using Wallet.Domain;

namespace Wallet.Application;

public sealed class CreateCardRequestRequest
{
    public Guid? AddressId { get; init; }
    public CardType Type { get; init; } = CardType.Debit;
}

public sealed record CardRequestDto(
    Guid Id,
    Guid UserId,
    Guid AddressId,
    string CardType,
    string Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset UpdatedAt);

public sealed record CardDto(
    Guid Id,
    Guid UserId,
    string CardHolderName,
    string Brand,
    string Type,
    string CardNumberLast4,
    int ExpirationMonth,
    int ExpirationYear,
    bool IsActive,
    DateTimeOffset CreatedAt);

public sealed class CreateCardCommand
{
    [Required]
    public string CardHolderName { get; init; } = string.Empty;

    [Required]
    public string Brand { get; init; } = string.Empty;

    public CardType Type { get; init; } = CardType.Debit;

    [Required]
    [RegularExpression(@"^[0-9]{4}$")]
    public string CardNumberLast4 { get; init; } = string.Empty;

    [Range(1, 12)]
    public int ExpirationMonth { get; init; }

    [Range(1, 9999)]
    public int ExpirationYear { get; init; }

    public string? CardToken { get; init; }
}

public sealed class PayWithSavedCardRequest
{
    [Required]
    public Guid CardId { get; init; }

    [RegularExpression(@"^[0-9]{3}$")]
    public string? Cvv { get; init; }
}
