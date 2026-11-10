using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Wallet.Application;

namespace Wallet.Infrastructure.OpenRenaper;

/// <summary>
/// Cliente HTTP para la integración con el servicio externo de verificación de identidad OpenRENAPER.
/// Consume el endpoint oficial POST /api/v1/consultas.
/// </summary>
public sealed class OpenRenaperClient(
    HttpClient httpClient,
    ILogger<OpenRenaperClient> logger) : IIdentityVerifier
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<VerifiedIdentity?> FindAsync(IdentityQuery query, CancellationToken ct = default)
    {
        var maskedDni = Mask(query.Number);
        var documentType = query.Type switch
        {
            IdentityDocumentType.Dni => "dni",
            IdentityDocumentType.Cuit => "cuit",
            _ => "dni"
        };

        var requestPayload = new OpenRenaperPersonRequest(documentType, query.Number);

        logger.LogInformation("Consultando validación de identidad ante OpenRENAPER para tipo {DocumentType} y documento {MaskedDni}",
            documentType, maskedDni);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.PostAsJsonAsync("/api/v1/consultas", requestPayload, JsonOptions, ct);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Error de red/comunicación al consultar OpenRENAPER para {MaskedDni}", maskedDni);
            throw new IdentityProviderUnavailableException("Error de comunicación con el servicio de identidad.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Tiempo de espera agotado al consultar OpenRENAPER para {MaskedDni}", maskedDni);
            throw new IdentityProviderUnavailableException("Tiempo de espera agotado al consultar el servicio de identidad.", ex);
        }

        // 404: sin_resultados (no se encontraron coincidencias en el padrón)
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            logger.LogInformation("Documento {MaskedDni} no encontrado en OpenRENAPER (HTTP 404 sin_resultados)", maskedDni);
            return null;
        }

        // 409: consulta_ambigua (varias personas posibles o datos contradictorios)
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            logger.LogWarning("Consulta ambigua para {MaskedDni} en OpenRENAPER (HTTP 409 consulta_ambigua)", maskedDni);
            return null;
        }

        // 429: limite_solicitudes_excedido
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            logger.LogWarning("Límite de solicitudes excedido en OpenRENAPER para {MaskedDni} (HTTP 429)", maskedDni);
            throw new IdentityProviderUnavailableException("Límite de peticiones alcanzado en el servicio de identidad. Reintentá más tarde.");
        }

        // Errores de fuentes externas o servidor (500, 502 respuesta_no_interpretable, 503 fuente_no_disponible, 504 tiempo_agotado)
        if ((int)response.StatusCode >= 500)
        {
            logger.LogError("Error de servidor/fuente en OpenRENAPER: HTTP {StatusCode} para {MaskedDni}",
                response.StatusCode, maskedDni);
            throw new IdentityProviderUnavailableException($"El servicio de identidad respondió con error de servidor ({response.StatusCode}).");
        }

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Respuesta no exitosa de OpenRENAPER: HTTP {StatusCode} para {MaskedDni}",
                response.StatusCode, maskedDni);
            return null;
        }

        OpenRenaperPersonResponse? body;
        try
        {
            body = await response.Content.ReadFromJsonAsync<OpenRenaperPersonResponse>(JsonOptions, ct);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Respuesta JSON no deserializable recibida de OpenRENAPER para {MaskedDni}", maskedDni);
            throw new IdentityProviderUnavailableException("Respuesta inválida recibida del servicio de identidad.", ex);
        }

        if (body is null || string.IsNullOrWhiteSpace(body.Nombre))
        {
            logger.LogInformation("OpenRENAPER devolvió respuesta sin nombre para {MaskedDni}", maskedDni);
            return null;
        }

        var returnedNumber = !string.IsNullOrWhiteSpace(body.Dni) ? body.Dni.Trim() : query.Number;
        return new VerifiedIdentity(returnedNumber, body.Nombre.Trim(), body.Cuit?.Trim());
    }

    private static string Mask(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "****";

        var trimmed = value.Trim();
        if (trimmed.Length <= 4)
            return "****";

        return $"{trimmed[..2]}****{trimmed[^2..]}";
    }
}
