using System.Net.Mail;

namespace Wallet.Domain;

/// <summary>
/// Representa a un cliente corporativo o comercio B2B que integra la pasarela mediante API Keys y Webhooks.
/// Hereda de WalletOwner para la gestión de su billetera y control de concurrencia optimista.
/// </summary>
public sealed class ExternalClient : WalletOwner
{
    /// <summary>
    /// Razón social o nombre comercial de la empresa.
    /// </summary>
    public string CompanyName { get; private set; } = string.Empty;

    /// <summary>
    /// Clave Única de Identificación Tributaria (CUIT) de 11 dígitos numéricos.
    /// </summary>
    public string Cuit { get; private set; } = string.Empty;

    /// <summary>
    /// Correo electrónico técnico o de contacto del comercio.
    /// </summary>
    public string ContactEmail { get; private set; } = string.Empty;

    /// <summary>
    /// Hash criptográfico SHA-256 de la API Key activa.
    /// </summary>
    public string ApiKeyHash { get; private set; } = string.Empty;

    /// <summary>
    /// Descripción o notas internas del comercio.
    /// </summary>
    public string? Description { get; private set; }

    /// <summary>
    /// URL de destino para las notificaciones asíncronas vía Webhook.
    /// </summary>
    public string? WebhookUrl { get; private set; }

    /// <summary>
    /// Secreto compartido para firmar las notificaciones HMAC de Webhook (mínimo 32 caracteres).
    /// </summary>
    public string? WebhookSecret { get; private set; }

    /// <summary>
    /// Fecha y hora UTC de la última rotación de la API Key.
    /// </summary>
    public DateTimeOffset? LastRotatedAt { get; private set; }

    private ExternalClient() { } // EF Core

    private ExternalClient(Guid id, string companyName, string cuit, string contactEmail, string? description, DateTimeOffset createdAt)
        : base(id, createdAt)
    {
        Cuit = ValidateCuit(cuit);
        UpdateDetails(companyName, contactEmail, description);
    }

    /// <summary>
    /// Método de fábrica para crear un comercio B2B con validación de CUIT de 11 dígitos.
    /// </summary>
    public static ExternalClient Create(
        string companyName,
        string cuit,
        string contactEmail,
        DateTimeOffset now,
        string? description = null)
    {
        return new ExternalClient(Guid.NewGuid(), companyName, cuit, contactEmail, description, now);
    }

    /// <summary>
    /// Compatibilidad hacia atrás: crea un comercio generando un CUIT provisorio si no fue suministrado.
    /// </summary>
    public static ExternalClient Create(
        string companyName,
        string contactEmail,
        DateTimeOffset now,
        string? description = null)
    {
        return new ExternalClient(Guid.NewGuid(), companyName, "00000000000", contactEmail, description, now);
    }

    /// <summary>
    /// Establece el hash SHA-256 de la API Key generada para el comercio.
    /// </summary>
    public void SetApiKeyHash(string apiKeyHash, DateTimeOffset now, bool isTestMode = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKeyHash);
        var trimmed = apiKeyHash.Trim();
        if (trimmed.Length != 64 || !trimmed.All(Uri.IsHexDigit))
            throw new ArgumentException("The ApiKey fingerprint must be a 64-character SHA-256 hexadecimal hash.", nameof(apiKeyHash));

        ApiKeyHash = trimmed;
        LastRotatedAt = now.ToUniversalTime();
    }

    /// <summary>
    /// Actualiza los datos comerciales de la empresa.
    /// </summary>
    public void UpdateDetails(string companyName, string contactEmail, string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(companyName);
        var trimmedName = companyName.Trim();
        if (trimmedName.Length > 150)
            throw new ArgumentException("The corporate name or trade name cannot exceed 150 characters.", nameof(companyName));

        var trimmedEmail = ValidateEmail(contactEmail);

        CompanyName = trimmedName;
        ContactEmail = trimmedEmail;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
    }

    /// <summary>
    /// Configura el endpoint de webhook y la clave secreta compartida para notificaciones asíncronas.
    /// </summary>
    public void ConfigureWebhook(string url, string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);

        var trimmedUrl = url.Trim();
        if (trimmedUrl.Length > 2048)
            throw new ArgumentException("Webhook URL cannot exceed 2048 characters.", nameof(url));

        if (!Uri.TryCreate(trimmedUrl, UriKind.Absolute, out var parsedUri) ||
            (parsedUri.Scheme != Uri.UriSchemeHttp && parsedUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("Webhook URL must be a valid absolute HTTP or HTTPS URL.", nameof(url));
        }

        var trimmedSecret = secret.Trim();
        if (trimmedSecret.Length < 32 || trimmedSecret.Length > 256)
            throw new ArgumentException("Webhook secret must be between 32 and 256 characters.", nameof(secret));

        WebhookUrl = trimmedUrl;
        WebhookSecret = trimmedSecret;
    }

    /// <summary>
    /// Desactiva las notificaciones automáticas por Webhook eliminando la URL y el secreto configurados.
    /// </summary>
    public void DeactivateWebhook()
    {
        WebhookUrl = null;
        WebhookSecret = null;
    }

    public override void Deactivate()
    {
        base.Deactivate();
    }

    private static string ValidateCuit(string cuit)
    {
        if (string.IsNullOrWhiteSpace(cuit))
            return "00000000000";

        var trimmed = cuit.Trim().Replace("-", "");
        if (trimmed.Length != 11 || !trimmed.All(char.IsDigit))
            throw new ArgumentException("CUIT must consist of exactly 11 numeric digits.", nameof(cuit));

        return trimmed;
    }

    private static string ValidateEmail(string email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        var trimmed = email.Trim();
        if (trimmed.Length > 254 || !MailAddress.TryCreate(trimmed, out var parsed) || parsed.Address != trimmed)
            throw new ArgumentException("The technical contact email is invalid.", nameof(email));

        return trimmed;
    }
}
