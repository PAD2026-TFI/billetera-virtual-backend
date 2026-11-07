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
        if (string.IsNullOrWhiteSpace(dni))
            return "00000000";

        var trimmed = dni.Trim();
        if (trimmed.Length < 6 || trimmed.Length > 20 || !trimmed.All(char.IsLetterOrDigit))
            throw new ArgumentException("DNI must contain between 6 and 20 alphanumeric characters.", nameof(dni));

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
