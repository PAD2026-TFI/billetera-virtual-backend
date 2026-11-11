using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wallet.Application;

namespace Wallet.Api.Controllers;

[ApiController]
[Route("api/v1/auth/password-reset")]
[AllowAnonymous]
[EnableRateLimiting("credentials")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PasswordResetController(PasswordResetService reset) : IdentityControllerBase
{
    [HttpPost]
    public async Task<ActionResult<PasswordResetCodeSent>> Send(SendPasswordResetCodeRequest request, CancellationToken ct)
    {
        if (!reset.Enabled)
            return Unavailable();

        if (!System.Net.Mail.MailAddress.TryCreate(request.Email.Trim(), out var email) ||
            !string.Equals(email.Address, request.Email.Trim(), StringComparison.Ordinal))
            return Problem(statusCode: 400, title: "Ingresá un correo electrónico válido.");

        try
        {
            return Accepted(await reset.SendAsync(email.Address, ct));
        }
        catch (RegistrationEmailUnavailableException)
        {
            return Unavailable();
        }
    }

    [HttpPost("confirm")]
    public async Task<ActionResult<PasswordResetCompleted>> Confirm(ResetPasswordRequest request, CancellationToken ct)
    {
        if (!reset.Enabled)
            return Unavailable();

        return FromResult(await reset.ResetAsync(request, ct));
    }

    private ObjectResult Unavailable() => Problem(statusCode: 503, title: "El envío de códigos no está disponible. Reintentá en un minuto.",
        extensions: new Dictionary<string, object?> { ["code"] = "PASSWORD_RESET_UNAVAILABLE" });
}
