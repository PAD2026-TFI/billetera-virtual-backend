using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wallet.Application;

namespace Wallet.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
[AllowAnonymous]
public sealed class AuthController(IdentityService identity) : IdentityControllerBase
{
    /// <summary>
    /// Inicio de sesión de usuarios y emisión de token JWT.
    /// </summary>
    [HttpPost("login")]
    [EnableRateLimiting("credentials")]
    [Consumes("application/json")]
    [ProducesResponseType<TokenResponse>(200)]
    [ProducesResponseType<ValidationProblemDetails>(400)]
    [ProducesResponseType<ProblemDetails>(401)]
    [ProducesResponseType<ProblemDetails>(429)]
    [ProducesResponseType<ProblemDetails>(503)]
    public async Task<ActionResult<TokenResponse>> Login(LoginRequest request, CancellationToken ct) =>
        FromResult(await identity.LoginAsync(request, ct));

    /// <summary>
    /// Ruta legacy de compatibilidad hacia atrás.
    /// </summary>
    [HttpPost("/api/auth/login")]
    [EnableRateLimiting("credentials")]
    [Consumes("application/json")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<ActionResult<TokenResponse>> LegacyLogin(LoginRequest request, CancellationToken ct) =>
        await Login(request, ct);
}
