using System.Net.Mail;

namespace Wallet.Domain;

public sealed class User : WalletOwner
{
    public string FirstName { get; private set; } = "";
    public string LastName { get; private set; } = "";
    public string Email { get; private set; } = "";
    public string NormalizedEmail { get; private set; } = "";
    public string PasswordHash { get; private set; } = "";
    public string Role { get; private set; } = "Client";
    public int SessionVersion { get; private set; }
    private User() { } // Materialización EF Core.
    private User(Guid id, string firstName, string lastName, string email, DateTimeOffset createdAt)
        : base(id, createdAt)
    {
        var trimmedEmail = ValidateAndSanitizeEmail(email);
        Email = trimmedEmail;
        NormalizedEmail = NormalizeEmail(trimmedEmail);
        UpdateProfile(firstName, lastName);
    }
    public static User Create(string firstName, string lastName, string email, DateTimeOffset now)
    {
        return new User(Guid.NewGuid(), firstName, lastName, email, now);
    }
    public static string NormalizeEmail(string email) => email.Trim().ToUpperInvariant();
    public void UpdateProfile(string firstName, string lastName)
    {
        FirstName = ValidateName(firstName, nameof(firstName));
        LastName = ValidateName(lastName, nameof(lastName));
    }
    public void SetPasswordHash(string hash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);
        PasswordHash = hash;
    }
    public void RevokeSessions() => SessionVersion = checked(SessionVersion + 1);
    public override void Disable() 
    {
        base.Disable(); 
        RevokeSessions();
    }

    private static string ValidateName(string value, string paramName) 
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);
        var trimmed = value.Trim();
        if (trimmed.Length > 100) 
        {
            throw new ArgumentException("First Name / Last Name can't exceed 100 characters", paramName);
        }
        return trimmed;
    }
    private static string ValidateAndSanitizeEmail(string email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        var trimmed = email.Trim();
        if (trimmed.Length > 254 || !MailAddress.TryCreate(trimmed, out var parsed) || parsed.Address != trimmed) //TODO: Biblioteca recomendada por el chat, habría que ver que tan fiable es y sino reutilizamos la comprobación que ya estaba.
            throw new ArgumentException("Invalid email format.", nameof(email)); 

        return trimmed;
    }

}
