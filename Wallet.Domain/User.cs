using System.Net.Mail;

namespace Wallet.Domain;

/// <summary>
/// Representa a un usuario individual (cliente o administrador) en la plataforma de billetera virtual.
/// Hereda de WalletOwner para la gestión de su billetera y control de concurrencia.
/// </summary>
public sealed class User : WalletOwner
{
    /// <summary>
    /// Documento Nacional de Identidad del usuario.
    /// </summary>
    public string Dni { get; private set; } = string.Empty;

    /// <summary>
    /// Nombre de pila del usuario.
    /// </summary>
    public string FirstName { get; private set; } = string.Empty;

    /// <summary>
    /// Apellido del usuario.
    /// </summary>
    public string LastName { get; private set; } = string.Empty;

    /// <summary>
    /// Correo electrónico principal del usuario.
    /// </summary>
    public string Email { get; private set; } = string.Empty;

    /// <summary>
    /// Correo electrónico normalizado en mayúsculas para búsquedas y unicidad.
    /// </summary>
    public string NormalizedEmail { get; private set; } = string.Empty;

    /// <summary>
    /// Hash criptográfico de la contraseña del usuario.
    /// </summary>
    public string PasswordHash { get; private set; } = string.Empty;

    /// <summary>
    /// Rol de seguridad asignado (por defecto Client).
    /// </summary>
    public string Role { get; private set; } = "Client";

    /// <summary>
    /// Versión de la sesión activa; al incrementarse se invalidan los tokens JWT emitidos previamente.
    /// </summary>
    public int SessionVersion { get; private set; }

    /// <summary>
    /// Identificador de la billetera virtual vinculada al usuario.
    /// </summary>
    public Guid? WalletId { get; private set; }

    // A seller remains a Client: account type never grants an administrative role.
    public string AccountType { get; private set; } = "personal";
    public int IntegrationVersion { get; private set; }
    public string? IntegrationName { get; private set; }
    public string? IntegrationKeyHash { get; private set; }
    public string? IntegrationKeyPrefix { get; private set; }
    public DateTimeOffset? IntegrationCreatedAt { get; private set; }
    public DateTimeOffset? IntegrationRotatedAt { get; private set; }
    public DateTimeOffset? IntegrationRevokedAt { get; private set; }

    /// <summary>
    /// Fecha y hora (UTC) en que la identidad del usuario fue verificada ante el registro oficial (RENAPER).
    /// Null si el usuario no ha sido verificado.
    /// </summary>
    public DateTimeOffset? IdentityVerifiedAt { get; private set; }

    /// <summary>
    /// Marca la identidad del usuario como verificada ante el registro oficial.
    /// </summary>
    public void MarkIdentityVerified(DateTimeOffset now)
    {
        if (now > DateTimeOffset.UtcNow.AddMinutes(5))
            throw new ArgumentException("La fecha de verificación no puede ser futura.", nameof(now));

        IdentityVerifiedAt = now.ToUniversalTime();
    }

    public void SetAccountType(string accountType)
    {
        if (accountType is not ("personal" or "merchant"))
            throw new ArgumentException("Tipo de cuenta inválido.", nameof(accountType));
        if (AccountType != accountType) IntegrationVersion = checked(IntegrationVersion + 1);
        AccountType = accountType;
        // Revocation is completed by the application with its clock when switching to personal.
    }

    public void SetIntegrationKey(string name, string hash, string prefix, DateTimeOffset now)
    {
        if (!IsActive || Role != "Client" || AccountType != "merchant")
            throw new InvalidOperationException("La cuenta debe ser un vendedor activo.");
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Trim().Length > 100 || hash.Length != 64 || !hash.All(Uri.IsHexDigit)
            || prefix.Length is < 1 or > 20)
            throw new ArgumentException("Datos de integración inválidos.");
        IntegrationVersion = checked(IntegrationVersion + 1);
        IntegrationName = name.Trim();
        IntegrationKeyHash = hash.ToLowerInvariant();
        IntegrationKeyPrefix = prefix;
        IntegrationCreatedAt ??= now.ToUniversalTime();
        IntegrationRotatedAt = now.ToUniversalTime();
        IntegrationRevokedAt = null;
    }

    public void RevokeIntegrationKey(DateTimeOffset now)
    {
        if (IntegrationKeyHash is null) return;
        IntegrationVersion = checked(IntegrationVersion + 1);
        IntegrationKeyHash = null;
        IntegrationRevokedAt = now.ToUniversalTime();
    }

    /// <summary>
    /// Secreto TOTP cifrado. Mientras TwoFactorEnabledAt es null, es una configuración pendiente de confirmar.
    /// </summary>
    public string? TwoFactorSecret { get; private set; }

    public DateTimeOffset? TwoFactorEnabledAt { get; private set; }

    /// <summary>
    /// Último paso TOTP aceptado; impide reutilizar el mismo código.
    /// </summary>
    public long? TwoFactorLastUsedStep { get; private set; }

    public bool IsTwoFactorEnabled => TwoFactorEnabledAt is not null && TwoFactorSecret is not null;

    public void BeginTwoFactorSetup(string protectedSecret)
    {
        if (IsTwoFactorEnabled)
            throw new InvalidOperationException("La verificación en dos pasos ya está activa.");
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedSecret);
        if (protectedSecret.Length > 512)
            throw new ArgumentException("Secreto inválido.", nameof(protectedSecret));
        TwoFactorSecret = protectedSecret;
        TwoFactorLastUsedStep = null;
    }

    public void EnableTwoFactor(long step, DateTimeOffset now)
    {
        if (TwoFactorSecret is null || IsTwoFactorEnabled)
            throw new InvalidOperationException("No hay una configuración pendiente de confirmar.");
        TwoFactorEnabledAt = now.ToUniversalTime();
        TwoFactorLastUsedStep = step;
    }

    public bool TryUseTwoFactorStep(long step)
    {
        if (!IsTwoFactorEnabled || TwoFactorLastUsedStep is { } last && step <= last)
            return false;
        TwoFactorLastUsedStep = step;
        return true;
    }

    public void DisableTwoFactor()
    {
        TwoFactorSecret = null;
        TwoFactorEnabledAt = null;
        TwoFactorLastUsedStep = null;
    }

    private User() { } // Materialización EF Core.

    private User(Guid id, string dni, string firstName, string lastName, string email, DateTimeOffset createdAt)
        : base(id, createdAt)
    {
        Dni = ValidateDni(dni);
        var trimmedEmail = ValidateAndSanitizeEmail(email);
        Email = trimmedEmail;
        NormalizedEmail = NormalizeEmail(trimmedEmail);
        UpdateProfile(firstName, lastName);
    }

    /// <summary>
    /// Fábrica de dominio para registrar un nuevo cliente con DNI, nombres y correo.
    /// </summary>
    public static User RegisterClient(string dni, string firstName, string lastName, string email, DateTimeOffset now)
    {
        return new User(Guid.NewGuid(), dni, firstName, lastName, email, now);
    }

    /// <summary>
    /// Fábrica de dominio para registrar un usuario administrador.
    /// </summary>
    public static User RegisterAdmin(string dni, string firstName, string lastName, string email, DateTimeOffset now)
    {
        var admin = new User(Guid.NewGuid(), dni, firstName, lastName, email, now);
        admin.Role = "Admin";
        return admin;
    }

    /// <summary>
    /// Compatibilidad hacia atrás: registra un usuario asignando un DNI predeterminado si no se especifica.
    /// </summary>
    public static User Create(string firstName, string lastName, string email, DateTimeOffset now, string? dni = null)
    {
        var effectiveDni = string.IsNullOrWhiteSpace(dni) ? "00000000" : dni;
        return new User(Guid.NewGuid(), effectiveDni, firstName, lastName, email, now);
    }

    /// <summary>
    /// Vincula la billetera creada al usuario.
    /// </summary>
    public void LinkWallet(Guid walletId)
    {
        if (walletId == Guid.Empty)
            throw new ArgumentException("Wallet identifier cannot be empty.", nameof(walletId));

        WalletId = walletId;
    }

    /// <summary>
    /// Normaliza una dirección de correo a mayúsculas sin espacios.
    /// </summary>
    public static string NormalizeEmail(string email) => email.Trim().ToUpperInvariant();

    /// <summary>
    /// Actualiza el nombre y apellido en el perfil del usuario.
    /// </summary>
    public void UpdateProfile(string firstName, string lastName)
    {
        var validFirst = ValidateName(firstName, nameof(firstName));
        var validLast = ValidateName(lastName, nameof(lastName));
        FirstName = validFirst;
        LastName = validLast;
    }

    /// <summary>
    /// Establece el hash criptográfico de la contraseña del usuario.
    /// </summary>
    public void SetPasswordHash(string hash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);
        PasswordHash = hash;
    }

    /// <summary>
    /// Revoca todas las sesiones activas del usuario incrementando la versión de sesión.
    /// </summary>
    public void RevokeSessions() => SessionVersion = checked(SessionVersion + 1);

    /// <summary>
    /// Desactiva al usuario y revoca sus sesiones concurrentes.
    /// </summary>
    public override void Deactivate()
    {
        base.Deactivate();
        RevokeSessions();
    }

    public override void Disable() => Deactivate();

    private static string ValidateDni(string dni)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dni, nameof(dni));

        var trimmed = dni.Trim();
        if (trimmed.Length is < 7 or > 8 || !trimmed.All(char.IsAsciiDigit))
            throw new ArgumentException("El DNI debe contener 7 u 8 dígitos numéricos.", nameof(dni));

        return trimmed;
    }

    private static string ValidateName(string value, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);
        var trimmed = value.Trim();
        if (trimmed.Length > 100)
            throw new ArgumentException("First Name / Last Name can't exceed 100 characters.", paramName);

        return trimmed;
    }

    private static string ValidateAndSanitizeEmail(string email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        var trimmed = email.Trim();
        if (trimmed.Length > 254 || !MailAddress.TryCreate(trimmed, out var parsed) || parsed.Address != trimmed)
            throw new ArgumentException("Invalid email format.", nameof(email));

        return trimmed;
    }
}
