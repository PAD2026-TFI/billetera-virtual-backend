using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wallet.Application;

namespace Wallet.Api.Controllers;

/// <summary>
/// Verificación en dos pasos con una aplicación de autenticación (TOTP).
/// </summary>
[ApiController]
[Route("api/v1/me/two-factor")]
[Authorize(Policy = "Client")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType<ProblemDetails>(401)]
[ProducesResponseType<ProblemDetails>(403)]
[ProducesResponseType<ProblemDetails>(503)]
public sealed class TwoFactorController(TwoFactorService twoFactor) : IdentityControllerBase
{
    [HttpGet]
    [ProducesResponseType<TwoFactorStatusResponse>(200)]
    public async Task<ActionResult<TwoFactorStatusResponse>> Status(CancellationToken ct) =>
        FromResult(await twoFactor.GetStatusAsync(ct));

    /// <summary>
    /// Genera un secreto nuevo y la dirección otpauth para el código QR. Queda pendiente hasta confirmarlo.
    /// </summary>
    [HttpPost("setup")]
    [EnableRateLimiting("credentials")]
    [ProducesResponseType<TwoFactorSetupResponse>(200)]
    [ProducesResponseType<ProblemDetails>(409)]
    public async Task<ActionResult<TwoFactorSetupResponse>> Setup(CancellationToken ct) =>
        FromResult(await twoFactor.BeginSetupAsync(ct));

    [HttpPost("confirm")]
    [EnableRateLimiting("credentials")]
    [Consumes("application/json")]
    [ProducesResponseType<TwoFactorStatusResponse>(200)]
    [ProducesResponseType<ValidationProblemDetails>(400)]
    [ProducesResponseType<ProblemDetails>(409)]
    public async Task<ActionResult<TwoFactorStatusResponse>> Confirm(TwoFactorCodeRequest request, CancellationToken ct) =>
        FromResult(await twoFactor.ConfirmSetupAsync(request, ct));

    [HttpPost("disable")]
    [EnableRateLimiting("credentials")]
    [Consumes("application/json")]
    [ProducesResponseType<TwoFactorStatusResponse>(200)]
    [ProducesResponseType<ValidationProblemDetails>(400)]
    [ProducesResponseType<ProblemDetails>(409)]
    public async Task<ActionResult<TwoFactorStatusResponse>> Disable(TwoFactorCodeRequest request, CancellationToken ct) =>
        FromResult(await twoFactor.DisableAsync(request, ct));
}
