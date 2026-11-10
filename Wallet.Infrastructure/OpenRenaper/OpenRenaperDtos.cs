using System.Text.Json.Serialization;

namespace Wallet.Infrastructure.OpenRenaper;

/// <summary>
/// Modelo de petición hacia el servicio OpenRENAPER.
/// </summary>
internal sealed record OpenRenaperPersonRequest(
    [property: JsonPropertyName("tipo")] string Tipo,
    [property: JsonPropertyName("numero")] string Numero);

/// <summary>
/// Modelo de respuesta provisorio retornado por OpenRENAPER (sujeto a confirmación de esquema oficial).
/// </summary>
internal sealed record OpenRenaperPersonResponse(
    [property: JsonPropertyName("nombre")] string? Nombre,
    [property: JsonPropertyName("apellido")] string? Apellido,
    [property: JsonPropertyName("numero")] string? Numero,
    [property: JsonPropertyName("tipo")] string? Tipo,
    [property: JsonPropertyName("valido")] bool? Valido,
    [property: JsonPropertyName("fallecido")] bool? Fallecido,
    [property: JsonPropertyName("mensaje")] string? Mensaje);
