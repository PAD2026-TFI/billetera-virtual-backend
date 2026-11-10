using System.Text.Json.Serialization;

namespace Wallet.Infrastructure.OpenRenaper;

/// <summary>
/// Modelo de petición hacia el servicio OpenRENAPER (/api/v1/consultas).
/// </summary>
internal sealed record OpenRenaperPersonRequest(
    [property: JsonPropertyName("tipo")] string Tipo,
    [property: JsonPropertyName("numero")] string Numero);

/// <summary>
/// Modelo de respuesta exitosa retornado por OpenRENAPER (/api/v1/consultas).
/// Retorna exactamente seis campos acordados (todos opcionales/anulables según la fuente elegida).
/// </summary>
internal sealed record OpenRenaperPersonResponse(
    [property: JsonPropertyName("nombre")] string? Nombre,
    [property: JsonPropertyName("dni")] string? Dni,
    [property: JsonPropertyName("cuit")] string? Cuit,
    [property: JsonPropertyName("edad")] int? Edad,
    [property: JsonPropertyName("sexo")] string? Sexo,
    [property: JsonPropertyName("provincia_ubicacion")] string? ProvinciaUbicacion);

/// <summary>
/// Modelo de error estándar retornado por OpenRENAPER (ErrorRespuesta / ErrorRespuestaSimple).
/// </summary>
internal sealed record OpenRenaperErrorResponse(
    [property: JsonPropertyName("codigo")] string? Codigo,
    [property: JsonPropertyName("mensaje")] string? Mensaje,
    [property: JsonPropertyName("fuente")] string? Fuente);
