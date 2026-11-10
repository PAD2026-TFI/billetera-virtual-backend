using System.ComponentModel.DataAnnotations;

namespace Wallet.Application;

public sealed class RegisterUserRequest
{
    [Required, StringLength(100)] public string FirstName { get; init; } = "";
    [Required, StringLength(100)] public string LastName { get; init; } = "";
    [Required, EmailAddress, StringLength(254)] public string Email { get; init; } = "";
    [Required, StringLength(128, MinimumLength = 8)] public string Password { get; init; } = "";
    public string? Dni { get; init; }
    public string? EmailVerificationToken { get; init; }
    [Required, RegularExpression("^(personal|merchant)$")] public string AccountType { get; init; } = "personal";
}

public sealed class LoginRequest
{
    [Required, EmailAddress, StringLength(254)] public string Email { get; init; } = "";
    [Required, StringLength(128)] public string Password { get; init; } = "";
}

public sealed class UpdateProfileRequest
{
    [Required, StringLength(100)] public string FirstName { get; init; } = "";
    [Required, StringLength(100)] public string LastName { get; init; } = "";
}

public sealed record UserResponse(Guid Id, string FirstName, string LastName, string Email, string AccountType = "personal");

public sealed record UserRegistrationResult(
    Guid UserId,
    string FirstName,
    string LastName,
    string Email,
    string Dni,
    Guid WalletId,
    string Cvu,
    string Alias,
    string AccountType = "personal");

public sealed record TokenResponse(string AccessToken, string TokenType, int ExpiresIn);

public sealed class RegisterAdminRequest
{
    [Required, StringLength(100)] public string FirstName { get; init; } = "";
    [Required, StringLength(100)] public string LastName { get; init; } = "";
    [Required, EmailAddress, StringLength(254)] public string Email { get; init; } = "";
    [Required, StringLength(128, MinimumLength = 12)] public string Password { get; init; } = "";
    public string? Dni { get; init; }
}

public sealed class AdminFundWalletRequest
{
    public Guid? WalletId { get; init; }
    public string? Cvu { get; init; }
    public string? Alias { get; init; }
    [Range(0.01, 100_000_000, ErrorMessage = "El monto a fondear debe ser positivo.")]
    public decimal Amount { get; init; }
}

public sealed class FundByCvuRequest
{
    [Required, StringLength(22, MinimumLength = 22, ErrorMessage = "El CVU debe tener exactamente 22 dígitos.")]
    public string Cvu { get; init; } = "";

    [Range(0.01, 100_000_000, ErrorMessage = "El monto a fondear debe ser positivo.")]
    public decimal Amount { get; init; }
}

public sealed class FundByAliasRequest
{
    [Required, StringLength(40, MinimumLength = 6, ErrorMessage = "El Alias debe tener entre 6 y 40 caracteres.")]
    public string Alias { get; init; } = "";

    [Range(0.01, 100_000_000, ErrorMessage = "El monto a fondear debe ser positivo.")]
    public decimal Amount { get; init; }
}

public sealed class FundByWalletIdRequest
{
    [Required]
    public Guid WalletId { get; init; }

    [Range(0.01, 100_000_000, ErrorMessage = "El monto a fondear debe ser positivo.")]
    public decimal Amount { get; init; }
}

public sealed record AdminFundWalletResponse(
    Guid FundingId,
    Guid WalletId,
    string Cvu,
    string Alias,
    decimal AmountCredited,
    decimal BalanceAfter,
    string Currency,
    DateTimeOffset CreatedAt);

public sealed record Error(string Code, string Message);

public sealed class Result<T>
{
    public T? Value { get; }
    public Error? Error { get; }
    public bool IsSuccess => Error is null;
    private Result(T? value, Error? error) { Value = value; Error = error; }
    public static Result<T> Success(T value) => new(value, null);
    public static Result<T> Failure(string code, string message) => new(default, new(code, message));
}
