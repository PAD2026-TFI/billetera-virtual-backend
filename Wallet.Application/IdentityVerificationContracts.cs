namespace Wallet.Application;

/// <summary>
/// Tipo de documento para consulta de identidad en registros oficiales.
/// </summary>
public enum IdentityDocumentType
{
    Dni,
    Cuit
}

/// <summary>
/// Parámetros de consulta de identidad.
/// </summary>
public sealed record IdentityQuery(IdentityDocumentType Type, string Number);

/// <summary>
/// Datos de la identidad verificada devuelta por el proveedor oficial (RENAPER).
/// </summary>
public sealed record VerifiedIdentity(string Number, string FullName, string? Cuit = null);

/// <summary>
/// Contrato del servicio de verificación de identidad externa (RENAPER).
/// </summary>
public interface IIdentityVerifier
{
    /// <summary>
    /// Consulta la información de identidad en el registro oficial.
    /// Retorna null si el documento no existe en el padrón.
    /// Lanza IdentityProviderUnavailableException ante timeouts, errores de conexión o respuestas 5xx.
    /// </summary>
    Task<VerifiedIdentity?> FindAsync(IdentityQuery query, CancellationToken ct = default);
}

/// <summary>
/// Implementación por defecto para entornos donde no se ha configurado un cliente HTTP externo.
/// </summary>
public sealed class PassThroughIdentityVerifier : IIdentityVerifier
{
    public Task<VerifiedIdentity?> FindAsync(IdentityQuery query, CancellationToken ct = default)
        => Task.FromResult<VerifiedIdentity?>(null);
}

/// <summary>
/// Excepción lanzada cuando el proveedor externo de identidad no está disponible o falla su comunicación.
/// </summary>
public sealed class IdentityProviderUnavailableException : Exception
{
    public IdentityProviderUnavailableException(string message = "El proveedor de identidad no se encuentra disponible.", Exception? inner = null)
        : base(message, inner) { }
}

/// <summary>
/// Opciones de configuración para la verificación de identidad.
/// Por defecto en desarrollo es false para no depender de servicios externos en tests.
/// En producción se configura a true en appsettings.json.
/// </summary>
public sealed class IdentityVerificationOptions
{
    public const string SectionName = "IdentityVerification";

    public bool Enabled { get; init; } = true;
}
