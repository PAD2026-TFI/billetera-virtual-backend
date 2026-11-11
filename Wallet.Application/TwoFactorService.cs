using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Wallet.Domain;

namespace Wallet.Application;

public sealed class TwoFactorCodeRequest
{
    [Required, RegularExpression("^[0-9]{6}$")] public string Code { get; init; } = "";
}

public sealed class TwoFactorLoginRequest
{
    [Required, StringLength(512)] public string ChallengeToken { get; init; } = "";
    [Required, RegularExpression("^[0-9]{6}$")] public string Code { get; init; } = "";
}

public sealed record TwoFactorStatusResponse(bool Enabled, DateTimeOffset? EnabledAt);
public sealed record TwoFactorSetupResponse(string Secret, string OtpAuthUri);
public sealed record TwoFactorChallengeResponse(bool TwoFactorRequired, string ChallengeToken, int ExpiresIn);
public sealed record LoginOutcome(TokenResponse? Token, TwoFactorChallengeResponse? Challenge);

/// <summary>
/// Claves derivadas de la clave maestra del servidor: una cifra los secretos TOTP y otra firma los desafíos de inicio de sesión.
/// </summary>
public sealed record TwoFactorSettings(byte[] SecretKey, byte[] ChallengeKey, string Issuer)
{
    public static TwoFactorSettings FromMasterKey(byte[] masterKey, string issuer = "padi") => new(
        HKDF.DeriveKey(HashAlgorithmName.SHA256, masterKey, 32, info: Encoding.ASCII.GetBytes("padi:two-factor-secret:v1")),
        HKDF.DeriveKey(HashAlgorithmName.SHA256, masterKey, 32, info: Encoding.ASCII.GetBytes("padi:two-factor-challenge:v1")),
        issuer);
}

/// <summary>
/// TOTP según RFC 6238 (HMAC-SHA1, 6 dígitos, pasos de 30 segundos), compatible con Google y Microsoft Authenticator.
/// </summary>
public static class Totp
{
    public const int StepSeconds = 30;
    private const int Window = 1;

    public static long StepAt(DateTimeOffset time) => time.ToUnixTimeSeconds() / StepSeconds;

    public static string CodeAt(byte[] secret, long step)
    {
        Span<byte> counter = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(counter, step);
        var hash = HMACSHA1.HashData(secret, counter);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }

    public static long? MatchStep(byte[] secret, string code, DateTimeOffset now, long? lastUsedStep)
    {
        if (code is not { Length: 6 } || !code.All(char.IsAsciiDigit)) return null;
        var current = StepAt(now);
        for (var step = current - Window; step <= current + Window; step++)
        {
            if (lastUsedStep is { } last && step <= last) continue;
            if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(CodeAt(secret, step)), Encoding.ASCII.GetBytes(code)))
                return step;
        }
        return null;
    }
}

public static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Encode(ReadOnlySpan<byte> data)
    {
        var output = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach (var value in data)
        {
            buffer = (buffer << 8) | value;
            bits += 8;
            while (bits >= 5)
            {
                output.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }
        if (bits > 0) output.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        return output.ToString();
    }

    public static byte[] Decode(string text)
    {
        var output = new List<byte>(text.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (var character in text.TrimEnd('='))
        {
            var index = Alphabet.IndexOf(char.ToUpperInvariant(character));
            if (index < 0) throw new FormatException("Base32 inválido.");
            buffer = (buffer << 5) | index;
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }
        return output.ToArray();
    }
}

public sealed class TwoFactorService(
    IUserRepository users,
    ICurrentUser currentUser,
    ITokenService tokens,
    TwoFactorSettings settings,
    TimeProvider clock)
{
    private const int SecretBytes = 20;
    private const int ChallengeLifetimeSeconds = 300;
    private const string SecretFormat = "v1";

    public async Task<Result<TwoFactorStatusResponse>> GetStatusAsync(CancellationToken ct)
    {
        var user = await CurrentAsync(ct);
        return user is null ? Denied<TwoFactorStatusResponse>() : Result<TwoFactorStatusResponse>.Success(Status(user));
    }

    public async Task<Result<TwoFactorSetupResponse>> BeginSetupAsync(CancellationToken ct)
    {
        var user = await CurrentAsync(ct);
        if (user is null) return Denied<TwoFactorSetupResponse>();
        if (user.IsTwoFactorEnabled)
            return Result<TwoFactorSetupResponse>.Failure("TWO_FACTOR_ALREADY_ENABLED", "La verificación en dos pasos ya está activa.");

        var secret = RandomNumberGenerator.GetBytes(SecretBytes);
        user.BeginTwoFactorSetup(Protect(secret));
        var conflict = await SaveAsync<TwoFactorSetupResponse>(ct);
        if (conflict is not null) return conflict;

        var encoded = Base32.Encode(secret);
        return Result<TwoFactorSetupResponse>.Success(new(encoded, OtpAuthUri(user.Email, encoded)));
    }

    public async Task<Result<TwoFactorStatusResponse>> ConfirmSetupAsync(TwoFactorCodeRequest request, CancellationToken ct)
    {
        var user = await CurrentAsync(ct);
        if (user is null) return Denied<TwoFactorStatusResponse>();
        if (user.IsTwoFactorEnabled)
            return Result<TwoFactorStatusResponse>.Failure("TWO_FACTOR_ALREADY_ENABLED", "La verificación en dos pasos ya está activa.");
        if (user.TwoFactorSecret is null || Unprotect(user.TwoFactorSecret) is not { } secret)
            return Result<TwoFactorStatusResponse>.Failure("TWO_FACTOR_SETUP_REQUIRED", "Generá un nuevo código QR para activar la verificación.");
        if (Totp.MatchStep(secret, request.Code, clock.GetUtcNow(), null) is not { } step)
            return InvalidCode<TwoFactorStatusResponse>();

        user.EnableTwoFactor(step, clock.GetUtcNow());
        return await SaveAsync<TwoFactorStatusResponse>(ct) ?? Result<TwoFactorStatusResponse>.Success(Status(user));
    }

    public async Task<Result<TwoFactorStatusResponse>> DisableAsync(TwoFactorCodeRequest request, CancellationToken ct)
    {
        var user = await CurrentAsync(ct);
        if (user is null) return Denied<TwoFactorStatusResponse>();
        if (!user.IsTwoFactorEnabled)
            return Result<TwoFactorStatusResponse>.Failure("TWO_FACTOR_NOT_ENABLED", "La verificación en dos pasos no está activa.");
        if (!TryUseCode(user, request.Code))
            return InvalidCode<TwoFactorStatusResponse>();

        user.DisableTwoFactor();
        return await SaveAsync<TwoFactorStatusResponse>(ct) ?? Result<TwoFactorStatusResponse>.Success(Status(user));
    }

    public TwoFactorChallengeResponse CreateChallenge(User user)
    {
        var expires = clock.GetUtcNow().AddSeconds(ChallengeLifetimeSeconds).ToUnixTimeSeconds();
        var payload = Encoding.UTF8.GetBytes(string.Join('|', user.Id.ToString("N"),
            user.SessionVersion.ToString(CultureInfo.InvariantCulture),
            user.TwoFactorEnabledAt!.Value.UtcTicks.ToString(CultureInfo.InvariantCulture),
            expires.ToString(CultureInfo.InvariantCulture)));
        var signature = HMACSHA256.HashData(settings.ChallengeKey, payload);
        return new(true, $"{Base64Url(payload)}.{Base64Url(signature)}", ChallengeLifetimeSeconds);
    }

    public async Task<Result<TokenResponse>> CompleteLoginAsync(TwoFactorLoginRequest request, CancellationToken ct)
    {
        var expired = Result<TokenResponse>.Failure("TWO_FACTOR_CHALLENGE_EXPIRED", "La verificación venció. Iniciá sesión de nuevo.");
        if (ReadChallenge(request.ChallengeToken) is not { } challenge) return expired;

        var user = await users.FindByIdAsync(challenge.UserId, ct);
        if (user is null || !user.IsActive || !user.IsTwoFactorEnabled || user.SessionVersion != challenge.SessionVersion
            || user.TwoFactorEnabledAt!.Value.UtcTicks != challenge.EnabledAtTicks)
            return expired;
        if (!TryUseCode(user, request.Code))
            return Result<TokenResponse>.Failure("TWO_FACTOR_LOGIN_FAILED", "El código no es correcto. Revisá tu app de autenticación.");

        return await SaveAsync<TokenResponse>(ct) ?? Result<TokenResponse>.Success(tokens.Create(user));
    }

    private bool TryUseCode(User user, string code)
    {
        if (user.TwoFactorSecret is null || Unprotect(user.TwoFactorSecret) is not { } secret) return false;
        return Totp.MatchStep(secret, code, clock.GetUtcNow(), user.TwoFactorLastUsedStep) is { } step && user.TryUseTwoFactorStep(step);
    }

    private sealed record Challenge(Guid UserId, int SessionVersion, long EnabledAtTicks);

    private Challenge? ReadChallenge(string token)
    {
        var parts = token.Split('.');
        if (parts.Length != 2) return null;
        byte[] payload, signature;
        try
        {
            payload = FromBase64Url(parts[0]);
            signature = FromBase64Url(parts[1]);
        }
        catch (FormatException) { return null; }
        if (!CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(settings.ChallengeKey, payload), signature))
            return null;

        var fields = Encoding.UTF8.GetString(payload).Split('|');
        if (fields.Length != 4 || !Guid.TryParseExact(fields[0], "N", out var userId)
            || !int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var sessionVersion)
            || !long.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out var enabledAtTicks)
            || !long.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out var expires)
            || expires <= clock.GetUtcNow().ToUnixTimeSeconds())
            return null;
        return new(userId, sessionVersion, enabledAtTicks);
    }

    private string Protect(byte[] secret)
    {
        var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
        var cipher = new byte[secret.Length];
        var tag = new byte[AesGcm.TagByteSizes.MaxSize];
        using var aes = new AesGcm(settings.SecretKey, tag.Length);
        aes.Encrypt(nonce, secret, cipher, tag);
        return $"{SecretFormat}.{Convert.ToBase64String([.. nonce, .. cipher, .. tag])}";
    }

    private byte[]? Unprotect(string stored)
    {
        var parts = stored.Split('.');
        if (parts.Length != 2 || parts[0] != SecretFormat) return null;
        try
        {
            var data = Convert.FromBase64String(parts[1]);
            var nonceSize = AesGcm.NonceByteSizes.MaxSize;
            var tagSize = AesGcm.TagByteSizes.MaxSize;
            if (data.Length <= nonceSize + tagSize) return null;
            var secret = new byte[data.Length - nonceSize - tagSize];
            using var aes = new AesGcm(settings.SecretKey, tagSize);
            aes.Decrypt(data.AsSpan(0, nonceSize), data.AsSpan(nonceSize, secret.Length), data.AsSpan(data.Length - tagSize), secret);
            return secret;
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException) { return null; }
    }

    private string OtpAuthUri(string email, string secret)
    {
        var issuer = Uri.EscapeDataString(settings.Issuer);
        return $"otpauth://totp/{issuer}:{Uri.EscapeDataString(email)}?secret={secret}&issuer={issuer}&algorithm=SHA1&digits=6&period={Totp.StepSeconds}";
    }

    private async Task<User?> CurrentAsync(CancellationToken ct)
    {
        var user = await users.FindByIdAsync(currentUser.Id, ct);
        return user is { IsActive: true } ? user : null;
    }

    private async Task<Result<T>?> SaveAsync<T>(CancellationToken ct)
    {
        try
        {
            await users.SaveAsync(ct);
            return null;
        }
        catch (ConcurrentUpdateException)
        {
            return Result<T>.Failure("TWO_FACTOR_CONFLICT", "Tu cuenta cambió mientras tanto. Volvé a intentarlo.");
        }
    }

    private static TwoFactorStatusResponse Status(User user) => new(user.IsTwoFactorEnabled, user.TwoFactorEnabledAt);
    private static Result<T> Denied<T>() => Result<T>.Failure("ACCESS_DENIED", "Acceso no permitido.");
    private static Result<T> InvalidCode<T>() => Result<T>.Failure("TWO_FACTOR_CODE_INVALID", "El código no es correcto. Revisá tu app de autenticación.");
    private static string Base64Url(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string text)
    {
        var base64 = text.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '='));
    }
}
