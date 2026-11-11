using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using Wallet.Domain;

namespace Wallet.Application;

public sealed class EmailVerificationChallenge(string email)
{
    public string Email { get; } = email;
    public string? CodeHash { get; set; }
    public DateTimeOffset CodeExpiresAt { get; set; }
    public DateTimeOffset ResendAt { get; set; }
    public int AttemptsLeft { get; set; }
    public string? ProofHash { get; set; }
    public DateTimeOffset? ProofExpiresAt { get; set; }
}

public interface IEmailVerificationRepository
{
    Task<EmailVerificationChallenge?> FindForUpdateAsync(string email, CancellationToken ct);
    void Add(EmailVerificationChallenge challenge);
    void Remove(EmailVerificationChallenge challenge);
}

public interface IRegistrationEmailSender
{
    Task SendCodeAsync(string email, string code, CancellationToken ct);
}

public sealed class RegistrationEmailUnavailableException : Exception { }

public sealed record EmailVerificationSettings(bool Enabled, byte[] Key);

public sealed class SendRegistrationCodeRequest
{
    [Required, EmailAddress, StringLength(254)] public string Email { get; init; } = "";
}

public sealed class VerifyRegistrationCodeRequest
{
    [Required, EmailAddress, StringLength(254)] public string Email { get; init; } = "";
    [Required, RegularExpression("^[0-9]{6}$")] public string Code { get; init; } = "";
}

public sealed record RegistrationCodeSent(int RetryAfterSeconds);
public sealed record RegistrationEmailProof(string VerificationToken);

public sealed class EmailVerificationService(
    EmailVerificationSettings settings,
    IEmailVerificationRepository challenges,
    IUserRepository users,
    IUnitOfWork unitOfWork,
    IRegistrationEmailSender sender,
    TimeProvider clock)
{
    private const int ResendDelaySeconds = 60;
    private const int MaxAttempts = 5;
    public bool Enabled => settings.Enabled;

    public async Task<Result<RegistrationCodeSent>> SendAsync(string email, CancellationToken ct)
    {
        var destination = email.Trim();
        var normalized = User.NormalizeEmail(destination);
        if (await users.FindByEmailAsync(normalized, ct) is not null)
            return Result<RegistrationCodeSent>.Failure(
                "EMAIL_ALREADY_REGISTERED",
                "Este correo electrónico ya está registrado. Iniciá sesión o usá otro correo.");

        var sent = new RegistrationCodeSent(ResendDelaySeconds);
        var code = RandomNumberGenerator.GetInt32(1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
        await using (var tx = await unitOfWork.BeginTransactionAsync(ct))
        {
            var challenge = await challenges.FindForUpdateAsync(normalized, ct);
            var now = clock.GetUtcNow();
            if (challenge is not null && challenge.ResendAt > now)
                return Result<RegistrationCodeSent>.Success(sent);

            if (challenge is null)
            {
                challenge = new EmailVerificationChallenge(normalized);
                challenges.Add(challenge);
            }

            challenge.CodeHash = CodeHash(normalized, code);
            challenge.CodeExpiresAt = now.AddMinutes(10);
            challenge.ResendAt = now.AddSeconds(ResendDelaySeconds);
            challenge.AttemptsLeft = MaxAttempts;
            await unitOfWork.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        await sender.SendCodeAsync(destination, code, ct);
        return Result<RegistrationCodeSent>.Success(sent);
    }

    public async Task<Result<RegistrationEmailProof>> VerifyAsync(string email, string code, CancellationToken ct)
    {
        var normalized = User.NormalizeEmail(email);
        await using var tx = await unitOfWork.BeginTransactionAsync(ct);
        var challenge = await challenges.FindForUpdateAsync(normalized, ct);
        var now = clock.GetUtcNow();
        if (challenge?.CodeHash is null || challenge.CodeExpiresAt <= now || challenge.AttemptsLeft <= 0)
            return InvalidCode();

        if (!Matches(challenge.CodeHash, CodeHash(normalized, code)))
        {
            challenge.AttemptsLeft--;
            await unitOfWork.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return InvalidCode();
        }

        var proof = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        challenge.CodeHash = null;
        challenge.ProofHash = ProofHash(proof);
        challenge.ProofExpiresAt = now.AddMinutes(20);
        await unitOfWork.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Result<RegistrationEmailProof>.Success(new(proof));
    }

    public static bool HasProofFormat(string? proof) =>
        proof is { Length: 64 } && proof.All(char.IsAsciiHexDigit);

    // Called inside the registration transaction so a failed registration preserves the proof.
    public async Task<bool> TryConsumeAsync(string normalizedEmail, string proof, CancellationToken ct)
    {
        var challenge = await challenges.FindForUpdateAsync(normalizedEmail, ct);
        if (challenge?.ProofHash is null || challenge.ProofExpiresAt is not { } expires ||
            expires <= clock.GetUtcNow() || !Matches(challenge.ProofHash, ProofHash(proof)))
            return false;

        challenges.Remove(challenge);
        return true;
    }

    private string CodeHash(string email, string code) => Convert.ToHexString(
        HMACSHA256.HashData(settings.Key, Encoding.UTF8.GetBytes($"padi:registration-email-code:{email}:{code}")));

    private static string ProofHash(string proof) =>
        Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(proof)));

    private static bool Matches(string expected, string actual) =>
        CryptographicOperations.FixedTimeEquals(Convert.FromHexString(expected), Convert.FromHexString(actual));

    private static Result<RegistrationEmailProof> InvalidCode() =>
        Result<RegistrationEmailProof>.Failure("EMAIL_CODE_INVALID", "El código es incorrecto o venció. Revisalo o pedí otro.");
}
