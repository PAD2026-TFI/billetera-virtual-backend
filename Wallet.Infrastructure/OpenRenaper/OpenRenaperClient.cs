using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Wallet.Application;

namespace Wallet.Infrastructure.OpenRenaper;

/// <summary>
/// Cliente HTTP para la integración con el servicio externo de verificación de identidad OpenRENAPER.
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
            response = await httpClient.PostAsJsonAsync("/api/v1/personas/consultar", requestPayload, JsonOptions, ct);
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

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            logger.LogInformation("Documento {MaskedDni} no encontrado en OpenRENAPER (HTTP 404)", maskedDni);
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Respuesta fallida de OpenRENAPER: HTTP {StatusCode} para {MaskedDni}",
                response.StatusCode, maskedDni);

            if ((int)response.StatusCode >= 500)
            {
                throw new IdentityProviderUnavailableException($"El servicio de identidad respondió con error de servidor ({response.StatusCode}).");
            }

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

        if (body is null || (body.Valido.HasValue && !body.Valido.Value))
        {
            logger.LogInformation("OpenRENAPER indicó identidad inválida o vacía para {MaskedDni}", maskedDni);
            return null;
        }

        if (body.Fallecido.HasValue && body.Fallecido.Value)
        {
            logger.LogWarning("El documento {MaskedDni} figura como fallecido en el padrón", maskedDni);
            return null;
        }

        if (string.IsNullOrWhiteSpace(body.Nombre) || string.IsNullOrWhiteSpace(body.Apellido))
        {
            logger.LogWarning("Respuesta de OpenRENAPER sin campos completos de nombre y apellido para {MaskedDni}", maskedDni);
            return null;
        }

        return new VerifiedIdentity(
            query.Number,
            body.Nombre.Trim(),
            body.Apellido.Trim());
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
