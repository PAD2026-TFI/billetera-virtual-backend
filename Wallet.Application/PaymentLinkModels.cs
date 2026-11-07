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

public sealed class LenientNullableGuidConverter : JsonConverter<Guid?>
{
    public override Guid? Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
    {
        if (reader.TokenType == System.Text.Json.JsonTokenType.Null)
            return null;

        if (reader.TokenType == System.Text.Json.JsonTokenType.String)
        {
            var str = reader.GetString();
            if (string.IsNullOrWhiteSpace(str))
                return null;

            return Guid.TryParse(str.Trim(), out var guid) ? guid : null;
        }

        return null;
    }

    public override void Write(System.Text.Json.Utf8JsonWriter writer, Guid? value, System.Text.Json.JsonSerializerOptions options)
    {
        if (value.HasValue)
            writer.WriteStringValue(value.Value);
        else
            writer.WriteNullValue();
    }
}

public sealed class CreatePaymentLinkRequest
{
    /// <summary>
    /// [OBLIGATORIO] Monto a cobrar en la moneda especificada (debe ser mayor a 0).
    /// </summary>
    [Required]
    public decimal Amount { get; init; }

    /// <summary>
    /// [OPCIONAL] Código de moneda ISO de 3 letras. Por defecto: "ARS".
    /// </summary>
    public string? Currency { get; init; } = "ARS";

    /// <summary>
    /// [OBLIGATORIO] Título o concepto del cobro visible para el pagador.
    /// </summary>
    [Required]
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// [OPCIONAL] Descripción detallada de los ítems o el servicio.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// [OPCIONAL] Modalidad de cobro: 0 = SingleUse (un único cobro), 1 = Reusable (múltiples cobros). Por defecto: 0.
    /// </summary>
    public PaymentLinkType Type { get; init; } = PaymentLinkType.SingleUse;

    /// <summary>
    /// [OPCIONAL] Minutos de validez antes de que expire el link. Si se omite, no tiene expiración por TTL.
    /// </summary>
    public int? TtlMinutes { get; init; }

    /// <summary>
    /// [OPCIONAL] GUID de la billetera destino. Se puede omitir si se provee CVU, Alias, email del comercio o si se está autenticado.
    /// </summary>
    public Guid? DestinationWalletId { get; init; }

    /// <summary>
    /// [OPCIONAL] CVU de 22 dígitos de la cuenta que recibirá los fondos.
    /// </summary>
    public string? DestinationCvu { get; init; }

    /// <summary>
    /// [OPCIONAL] Alias bancario/virtual de la cuenta que recibirá los fondos.
    /// </summary>
    public string? DestinationAlias { get; init; }

    /// <summary>
    /// [OPCIONAL] Email del comercio si no se está autenticado (crea o enlaza el comercio invitado automáticamente).
    /// </summary>
    public string? MerchantEmail { get; init; }

    public CreatePaymentLinkRequest() { }

    public CreatePaymentLinkRequest(
        decimal amount,
        string title,
        string? description = null,
        PaymentLinkType type = PaymentLinkType.SingleUse,
        int? ttlMinutes = null,
        Guid? destinationWalletId = null,
        string? merchantEmail = null,
        string? destinationCvu = null,
        string? destinationAlias = null)
    {
        Amount = amount;
        Title = title;
        Description = description;
        Type = type;
        TtlMinutes = ttlMinutes;
        DestinationWalletId = destinationWalletId;
        MerchantEmail = merchantEmail;
        DestinationCvu = destinationCvu;
        DestinationAlias = destinationAlias;
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
    string QrPayload,
    string? QrCodeUrl = null);

public sealed record CreatePaymentIntentRequest(
    [Required] decimal Amount,
    [Required] string Title,
    string? Currency = "ARS",
    string? Description = null,
    PaymentLinkType Type = PaymentLinkType.SingleUse,
    int? TtlMinutes = null);

public sealed record PaymentIntentResponse(
    Guid Id,
    string Token,
    decimal Amount,
    string Currency,
    string Title,
    string? Description,
    string Status,
    string Type,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    string CheckoutUrl,
    string QrCodeUrl);

public sealed record CheckoutPublicDetailsDto(
    string Token,
    string Title,
    string? Description,
    decimal Amount,
    string Currency,
    string Status,
    string Type,
    DateTimeOffset? ExpiresAt,
    bool IsPayable,
    Guid Id);

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
    string? AuthorizationCode,
    bool IdempotencyReplayed = false);

public sealed record MerchantPaymentDto(
    Guid Id,
    Guid PaymentLinkId,
    Guid DestinationWalletId,
    decimal Amount,
    string Currency,
    string PaymentMethod,
    string Status,
    Guid? TransferId,
    string? CardBrand,
    string? CardLast4,
    string? CardHolderName,
    string? AuthorizationCode,
    string? FailureReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);
