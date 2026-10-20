using System.ComponentModel.DataAnnotations;
namespace Wallet.Application;

public sealed class RegisterUserRequest
{
    [Required, StringLength(100)] public string FirstName { get; init; } = "";
    [Required, StringLength(100)] public string LastName { get; init; } = "";
    [Required, EmailAddress, StringLength(254)] public string Email { get; init; } = "";
    [Required, StringLength(128, MinimumLength = 12)] public string Password { get; init; } = "";
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
public sealed record UserResponse(Guid Id, string FirstName, string LastName, string Email);
public sealed record TokenResponse(string AccessToken, string TokenType, int ExpiresIn);
public sealed record Error(string Code, string Message);
// Resultado genérico para rechazos esperados; sin dependencias HTTP en Application.
public sealed class Result<T>
{
    public T? Value { get; }
    public Error? Error { get; }
    public bool IsSuccess => Error is null;
    private Result(T? value, Error? error) { Value = value; Error = error; }
    public static Result<T> Success(T value) => new(value, null);
    public static Result<T> Failure(string code, string message) => new(default, new(code, message));
}
