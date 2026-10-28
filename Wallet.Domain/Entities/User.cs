namespace Wallet.Domain.Entities;

public sealed class User
{
    public Guid Id { get; private set; }
    public string FirstName { get; private set; } = "";
    public string LastName { get; private set; } = "";
    public string Email { get; private set; } = "";
    public string NormalizedEmail { get; private set; } = "";
    public string PasswordHash { get; private set; } = "";
    public string Role { get; private set; } = "Client";
    public bool IsActive { get; private set; } = true;
    public int SessionVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    private User() { } // Materialización EF Core.

    public static User Create(string firstName, string lastName, string email, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        var trimmed = email.Trim();
        if (trimmed.Length > 254 || !System.Net.Mail.MailAddress.TryCreate(trimmed, out var parsed)
            || parsed.Address != trimmed)
            throw new ArgumentException("Email inválido.", nameof(email));
        var user = new User { Id = Guid.NewGuid(), Email = trimmed,
            NormalizedEmail = NormalizeEmail(trimmed), CreatedAt = now.ToUniversalTime() };
        user.UpdateProfile(firstName, lastName);
        return user;
    }
    public static string NormalizeEmail(string email) => email.Trim().ToUpperInvariant();
    public void UpdateProfile(string firstName, string lastName)
    {
        var first = ValidateName(firstName);
        var last = ValidateName(lastName);
        FirstName = first;
        LastName = last;
    }
    private static string ValidateName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var trimmed = value.Trim();
        if (trimmed.Length > 100) throw new ArgumentException("Nombre demasiado largo.");
        return trimmed;
    }
    public void SetPasswordHash(string hash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);
        PasswordHash = hash;
    }
    public void RevokeSessions() => SessionVersion = checked(SessionVersion + 1);
    public void Disable() { IsActive = false; RevokeSessions(); }
}
