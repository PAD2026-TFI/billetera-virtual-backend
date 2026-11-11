using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wallet.Application;

namespace Wallet.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
[AllowAnonymous]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AuthController(IdentityService identity, TwoFactorService twoFactor) : IdentityControllerBase
{
    /// <summary>
    /// Inicio de sesión de usuarios y emisión de token JWT.
    /// </summary>
    [HttpPost("login")]
    [EnableRateLimiting("credentials")]
    [Consumes("application/json")]
    [ProducesResponseType<TokenResponse>(200)]
    [ProducesResponseType<TwoFactorChallengeResponse>(202)]
    [ProducesResponseType<ValidationProblemDetails>(400)]
    [ProducesResponseType<ProblemDetails>(401)]
    [ProducesResponseType<ProblemDetails>(429)]
    [ProducesResponseType<ProblemDetails>(503)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        var result = await identity.LoginAsync(request, ct);
        if (!result.IsSuccess) return FromResult(result).Result!;
        return result.Value!.Challenge is { } challenge ? Accepted(challenge) : Ok(result.Value.Token);
    }

    /// <summary>
    /// Segundo paso del inicio de sesión para cuentas con verificación en dos pasos.
    /// </summary>
    [HttpPost("login/two-factor")]
    [EnableRateLimiting("credentials")]
    [Consumes("application/json")]
    [ProducesResponseType<TokenResponse>(200)]
    [ProducesResponseType<ValidationProblemDetails>(400)]
    [ProducesResponseType<ProblemDetails>(401)]
    [ProducesResponseType<ProblemDetails>(429)]
    [ProducesResponseType<ProblemDetails>(503)]
    [ProducesResponseType<ProblemDetails>(409)]
    public async Task<ActionResult<TokenResponse>> LoginTwoFactor(TwoFactorLoginRequest request, CancellationToken ct) =>
        FromResult(await twoFactor.CompleteLoginAsync(request, ct));

    /// <summary>
    /// Valida preliminarmente los datos de identidad (DNI, Nombre y Apellido) ante OpenRENAPER
    /// y verifica que el DNI no se encuentre registrado previamente.
    /// </summary>
    [HttpPost("validate-identity")]
    [EnableRateLimiting("credentials")]
    [Consumes("application/json")]
    [ProducesResponseType<IdentityValidationResult>(200)]
    [ProducesResponseType<ValidationProblemDetails>(400)]
    [ProducesResponseType<ProblemDetails>(409)]
    [ProducesResponseType<ProblemDetails>(422)]
    [ProducesResponseType<ProblemDetails>(429)]
    [ProducesResponseType<ProblemDetails>(503)]
    public async Task<ActionResult<IdentityValidationResult>> ValidateIdentity(
        ValidateIdentityRequest request, CancellationToken ct) =>
        FromResult(await identity.ValidateIdentityAsync(request, ct));

    /// <summary>
    /// Ruta legacy de compatibilidad hacia atrás.
    /// </summary>
    [HttpPost("/api/auth/login")]
    [EnableRateLimiting("credentials")]
    [Consumes("application/json")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<IActionResult> LegacyLogin(LoginRequest request, CancellationToken ct) =>
        await Login(request, ct);
}
