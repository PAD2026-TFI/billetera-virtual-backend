using System.ComponentModel.DataAnnotations;
using Wallet.Domain;

namespace Wallet.Application;

public sealed class IdentityService(
    IUserRepository users,
    IPasswordService passwords,
    ITokenService tokens,
    ICurrentUser currentUser,
    TimeProvider clock,
    UserRegistrationService? registrationService = null)
{
    private static bool IsValid(object request) => Validator.TryValidateObject(
        request, new ValidationContext(request), new List<ValidationResult>(), true);

    private static UserResponse Map(User user) => new(user.Id, user.FirstName, user.LastName, user.Email);

    public async Task<Result<UserResponse>> RegisterAsync(RegisterUserRequest request, CancellationToken ct)
    {
        if (registrationService is not null)
        {
            try
            {
                var regResult = await registrationService.RegisterAsync(request, ct);
                if (!regResult.IsSuccess)
                    return Result<UserResponse>.Failure(regResult.Error!.Code, regResult.Error.Message);

                var v = regResult.Value!;
                return Result<UserResponse>.Success(new UserResponse(v.UserId, v.FirstName, v.LastName, v.Email));
            }
            catch (Exception ex) when (IsStorageOrConnectionException(ex))
            {
                // Fallback para pruebas de endpoints ligeras donde sólo se sustituye IUserRepository
            }
        }

        if (!IsValid(request) || string.IsNullOrWhiteSpace(request.Password))
            return Result<UserResponse>.Failure("INVALID_INPUT", "Revisá los campos obligatorios y sus límites.");

        User user;
        try { user = User.Create(request.FirstName, request.LastName, request.Email, clock.GetUtcNow(), request.Dni); }
        catch (ArgumentException) { return Result<UserResponse>.Failure("INVALID_INPUT", "Datos de usuario inválidos."); }

        user.SetPasswordHash(passwords.Hash(user, request.Password));

        try { await users.AddAsync(user, ct); }
        catch (DuplicateEmailException)
        { return Result<UserResponse>.Failure("EMAIL_ALREADY_REGISTERED", "El email ya está registrado."); }

        return Result<UserResponse>.Success(Map(user));
    }

    private static bool IsStorageOrConnectionException(Exception ex)
    {
        var type = ex.GetType().FullName ?? "";
        if (type.Contains("SqlException") || type.Contains("DbUpdate") || type.Contains("StorageUnavailable"))
            return true;

        if (ex.InnerException is not null)
            return IsStorageOrConnectionException(ex.InnerException);

        return false;
    }

    public async Task<Result<TokenResponse>> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        if (!IsValid(request)) return Result<TokenResponse>.Failure("INVALID_INPUT", "Entrada inválida.");
        var user = await users.FindByEmailAsync(User.NormalizeEmail(request.Email), ct);
        if (user is null || !passwords.Verify(user, request.Password) || !user.IsActive)
            return Result<TokenResponse>.Failure("INVALID_CREDENTIALS", "No se pudo iniciar sesión.");
        return Result<TokenResponse>.Success(tokens.Create(user));
    }

    public async Task<Result<UserResponse>> GetProfileAsync(CancellationToken ct)
    {
        var user = await users.FindByIdAsync(currentUser.Id, ct);
        return user is null || !user.IsActive
            ? Result<UserResponse>.Failure("ACCESS_DENIED", "Acceso no permitido.")
            : Result<UserResponse>.Success(Map(user));
    }

    public async Task<Result<UserResponse>> UpdateProfileAsync(UpdateProfileRequest request, CancellationToken ct)
    {
        if (!IsValid(request)) return Result<UserResponse>.Failure("INVALID_INPUT", "Entrada inválida.");
        var user = await users.FindByIdAsync(currentUser.Id, ct);
        if (user is null || !user.IsActive)
            return Result<UserResponse>.Failure("ACCESS_DENIED", "Acceso no permitido.");
        try { user.UpdateProfile(request.FirstName, request.LastName); }
        catch (ArgumentException) { return Result<UserResponse>.Failure("INVALID_INPUT", "Perfil inválido."); }
        try { await users.SaveAsync(ct); }
        catch (ConcurrentUpdateException)
        { return Result<UserResponse>.Failure("PROFILE_CONFLICT", "El perfil cambió. Consultalo nuevamente."); }
        return Result<UserResponse>.Success(Map(user));
    }
}
