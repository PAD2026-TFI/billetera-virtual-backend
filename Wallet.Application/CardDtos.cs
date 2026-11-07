using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations;
using Wallet.Domain;

namespace Wallet.Application;

public sealed class CreateCardRequestRequest
{
    public Guid? AddressId { get; init; }
    public DeliveryAddressRequest? DeliveryAddress { get; init; }
    [JsonConverter(typeof(JsonStringEnumConverter<CardType>))]
    public CardType Type { get; init; } = CardType.Debit;
}

public sealed record CardRequestDto(
    Guid Id,
    Guid UserId,
    Guid AddressId,
    string CardType,
    string Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset UpdatedAt,
    DeliveryAddressDto? DeliveryAddress = null);

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

    [JsonConverter(typeof(JsonStringEnumConverter<CardType>))]
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

public sealed class DeliveryAddressRequest
{
    [Required, StringLength(150)] public string Street { get; init; } = string.Empty;
    [Required, StringLength(20)] public string Number { get; init; } = string.Empty;
    [Required, StringLength(100)] public string City { get; init; } = string.Empty;
    [Required, StringLength(100)] public string State { get; init; } = string.Empty;
    [Required, StringLength(20)] public string PostalCode { get; init; } = string.Empty;
    [Required, StringLength(100)] public string Country { get; init; } = "Argentina";
}

public sealed record DeliveryAddressDto(
    Guid Id, string Street, string Number, string City, string State,
    string PostalCode, string Country, string ValidationStatus);
