using System.Net.Mail;
namespace Wallet.Domain;
public sealed class ExternalClient : WalletOwner
{
    public string CompanyName { get; private set; } = "";
    public string ContactEmail { get; private set; } = "";
    public string ApiKeyHash { get; private set; } = "";
    public string? Description { get; private set; }
    public DateTimeOffset? LastRotatedAt { get; private set; }

    private ExternalClient() { } 

    private ExternalClient(Guid id, string companyName, string contactEmail, string? description, DateTimeOffset createdAt)
        : base(id, createdAt)
    {
        UpdateDetails(companyName, contactEmail, description);
    }

    public static ExternalClient Create(string companyName, string contactEmail, DateTimeOffset now, string? description = null)
    {
        return new ExternalClient(Guid.NewGuid(), companyName, contactEmail, description, now);
    }

    public void SetApiKeyHash(string apiKeyHash, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKeyHash);
        var trimmed = apiKeyHash.Trim();
        if (trimmed.Length != 64 || !trimmed.All(Uri.IsHexDigit))
            throw new ArgumentException("The ApiKey fingerprint must be a 64-character SHA-256 hexadecimal hash.", nameof(apiKeyHash));

        ApiKeyHash = trimmed;
        LastRotatedAt = now.ToUniversalTime();
    }

    public void UpdateDetails(string companyName, string contactEmail, string? description)
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

    public override void Disable()
    {
        base.Disable();
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