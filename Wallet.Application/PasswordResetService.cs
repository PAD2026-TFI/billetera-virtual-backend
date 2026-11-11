using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using Wallet.Domain;

namespace Wallet.Application;

public sealed class PasswordResetChallenge(string email)
{
    public string Email { get; } = email;
    public string? CodeHash { get; set; }
    public DateTimeOffset CodeExpiresAt { get; set; }
    public DateTimeOffset ResendAt { get; set; }
    public int AttemptsLeft { get; set; }
}

public interface IPasswordResetRepository
{
    Task<PasswordResetChallenge?> FindForUpdateAsync(string email, CancellationToken ct);
    void Add(PasswordResetChallenge challenge);
    void Remove(PasswordResetChallenge challenge);
}

public interface IPasswordResetEmailSender
{
    Task SendPasswordResetCodeAsync(string email, string code, CancellationToken ct);
}

public sealed class SendPasswordResetCodeRequest
{
    [Required, EmailAddress, StringLength(254)] public string Email { get; init; } = "";
}

public sealed class ResetPasswordRequest
{
    [Required, EmailAddress, StringLength(254)] public string Email { get; init; } = "";
    [Required, RegularExpression("^[0-9]{6}$")] public string Code { get; init; } = "";

    [Required, StringLength(128, MinimumLength = 8)]
    [RegularExpression("^(?=.*[a-z])(?=.*[A-Z])(?=.*[0-9]).+$",
        ErrorMessage = "La contraseña debe tener al menos una mayúscula, una minúscula y un número.")]
    public string NewPassword { get; init; } = "";
}

public sealed record PasswordResetCodeSent(int RetryAfterSeconds);
public sealed record PasswordResetCompleted(bool Reset);

public sealed class PasswordResetService(
    EmailVerificationSettings settings,
    IPasswordResetRepository challenges,
    IUserRepository users,
    IPasswordService passwords,
    IUnitOfWork unitOfWork,
    IPasswordResetEmailSender sender,
    TimeProvider clock)
{
    private const int ResendDelaySeconds = 60;
    private const int MaxAttempts = 5;
    public bool Enabled => settings.Enabled;

    public async Task<PasswordResetCodeSent> SendAsync(string email, CancellationToken ct)
    {
        var destination = email.Trim();
        var normalized = User.NormalizeEmail(destination);
        var sent = new PasswordResetCodeSent(ResendDelaySeconds);
        var user = await users.FindByEmailAsync(normalized, ct);
        if (user is null || !user.IsActive || user.Role != "Client")
            return sent;

        var code = RandomNumberGenerator.GetInt32(1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
        await using (var tx = await unitOfWork.BeginTransactionAsync(ct))
        {
            var challenge = await challenges.FindForUpdateAsync(normalized, ct);
            var now = clock.GetUtcNow();
            if (challenge is not null && challenge.ResendAt > now)
                return sent;

            if (challenge is null)
            {
                challenge = new PasswordResetChallenge(normalized);
                challenges.Add(challenge);
            }

            challenge.CodeHash = CodeHash(normalized, code);
            challenge.CodeExpiresAt = now.AddMinutes(10);
            challenge.ResendAt = now.AddSeconds(ResendDelaySeconds);
            challenge.AttemptsLeft = MaxAttempts;
            await unitOfWork.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        await sender.SendPasswordResetCodeAsync(destination, code, ct);
        return sent;
    }

    public async Task<Result<PasswordResetCompleted>> ResetAsync(ResetPasswordRequest request, CancellationToken ct)
    {
        var normalized = User.NormalizeEmail(request.Email);
        await using var tx = await unitOfWork.BeginTransactionAsync(ct);
        var challenge = await challenges.FindForUpdateAsync(normalized, ct);
        var now = clock.GetUtcNow();
        if (challenge?.CodeHash is null || challenge.CodeExpiresAt <= now || challenge.AttemptsLeft <= 0)
            return InvalidCode();

        if (!Matches(challenge.CodeHash, CodeHash(normalized, request.Code)))
        {
            challenge.AttemptsLeft--;
            await unitOfWork.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return InvalidCode();
        }

        var user = await users.FindByEmailAsync(normalized, ct);
        challenges.Remove(challenge);
        if (user is null || !user.IsActive || user.Role != "Client")
        {
            await unitOfWork.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return InvalidCode();
        }

        user.SetPasswordHash(passwords.Hash(user, request.NewPassword));
        user.RevokeSessions();
        await unitOfWork.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Result<PasswordResetCompleted>.Success(new(true));
    }

    private string CodeHash(string email, string code) => Convert.ToHexString(
        HMACSHA256.HashData(settings.Key, Encoding.UTF8.GetBytes($"padi:password-reset-code:{email}:{code}")));

    private static bool Matches(string expected, string actual) =>
        CryptographicOperations.FixedTimeEquals(Convert.FromHexString(expected), Convert.FromHexString(actual));

    private static Result<PasswordResetCompleted> InvalidCode() =>
        Result<PasswordResetCompleted>.Failure("PASSWORD_RESET_CODE_INVALID", "El código es incorrecto o venció. Revisalo o pedí otro.");
}
