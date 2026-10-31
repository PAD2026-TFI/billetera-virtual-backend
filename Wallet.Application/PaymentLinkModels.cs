using System.ComponentModel.DataAnnotations;
using Wallet.Domain;

namespace Wallet.Application;

public enum CheckoutPaymentMethod
{
    Wallet,
    Card
}

public sealed class CreatePaymentLinkRequest
{
    [Required]
    [Range(0.01, 9999999999999999.99)]
    public decimal Amount { get; init; }

    [Required]
    [StringLength(128, MinimumLength = 1)]
    public string Title { get; init; } = string.Empty;

    [StringLength(512)]
    public string? Description { get; init; }

    public PaymentLinkType Type { get; init; } = PaymentLinkType.SingleUse;

    [Range(1, 43200)] // Hasta 30 días
    public int? TtlMinutes { get; init; }

    public Guid? DestinationWalletId { get; init; }

    [EmailAddress]
    public string? MerchantEmail { get; init; }
}

public sealed record PaymentLinkDto(
    Guid Id,
    string Token,
    Guid DestinationWalletId,
    Guid? MerchantOwnerId,
    string Title,
    string? Description,
    decimal Amount,
    string Currency,
    string Status,
    string Type,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    string CheckoutUrl,
    string QrPayload);

public sealed record CheckoutPublicDetailsDto(
    string Token,
    string Title,
    string? Description,
    decimal Amount,
    string Currency,
    string Status,
    string Type,
    DateTimeOffset? ExpiresAt,
    bool IsPayable);

public sealed class ProcessCheckoutRequest
{
    public string? Token { get; init; }

    [Required]
    public CheckoutPaymentMethod PaymentMethod { get; init; } = CheckoutPaymentMethod.Card;

    public CardPaymentDetails? Card { get; init; }

    public Guid? PayerWalletId { get; init; }
}

public sealed record CheckoutReceiptDto(
    Guid PaymentId,
    string Token,
    decimal Amount,
    string Currency,
    Guid DestinationWalletId,
    string PaymentMethod,
    string Status,
    DateTimeOffset PaidAt,
    string? AuthorizationCode);
