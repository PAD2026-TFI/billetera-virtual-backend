using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Wallet.Domain;

namespace Wallet.Application;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CheckoutPaymentMethod
{
    Wallet,
    Card
}

public sealed class CreatePaymentLinkRequest
{
    [Required]
    public decimal Amount { get; init; }

    public string? Currency { get; init; } = "ARS";

    [Required]
    public string Title { get; init; } = string.Empty;

    public string? Description { get; init; }

    public PaymentLinkType Type { get; init; } = PaymentLinkType.SingleUse;

    public int? TtlMinutes { get; init; }

    public Guid? DestinationWalletId { get; init; }

    public string? MerchantEmail { get; init; }

    public CreatePaymentLinkRequest() { }

    public CreatePaymentLinkRequest(
        decimal amount,
        string title,
        string? description = null,
        PaymentLinkType type = PaymentLinkType.SingleUse,
        int? ttlMinutes = null,
        Guid? destinationWalletId = null,
        string? merchantEmail = null)
    {
        Amount = amount;
        Title = title;
        Description = description;
        Type = type;
        TtlMinutes = ttlMinutes;
        DestinationWalletId = destinationWalletId;
        MerchantEmail = merchantEmail;
    }
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
    public CheckoutPaymentMethod PaymentMethod { get; init; } = CheckoutPaymentMethod.Card;

    public string? Token { get; init; }

    public CardPaymentDetails? Card { get; init; }

    public Guid? PayerWalletId { get; init; }

    public ProcessCheckoutRequest() { }

    public ProcessCheckoutRequest(
        CheckoutPaymentMethod paymentMethod,
        string? token = null,
        CardPaymentDetails? card = null,
        Guid? payerWalletId = null)
    {
        PaymentMethod = paymentMethod;
        Token = token;
        Card = card;
        PayerWalletId = payerWalletId;
    }
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
