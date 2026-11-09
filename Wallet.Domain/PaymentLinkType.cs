using System.Text.Json.Serialization;

namespace Wallet.Domain;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PaymentLinkType
{
    SingleUse,
    MultiUse
}
