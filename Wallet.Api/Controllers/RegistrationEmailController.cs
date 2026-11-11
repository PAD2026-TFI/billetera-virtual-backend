using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wallet.Application;

namespace Wallet.Api.Controllers;

[ApiController]
[Route("api/v1/auth/email-verification")]
[AllowAnonymous]
[EnableRateLimiting("credentials")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class RegistrationEmailController(EmailVerificationService verification) : IdentityControllerBase
{
    [HttpPost]
    public async Task<ActionResult<RegistrationCodeSent>> Send(SendRegistrationCodeRequest request, CancellationToken ct)
    {
        if (!verification.Enabled)
            return Unavailable();

        if (!System.Net.Mail.MailAddress.TryCreate(request.Email.Trim(), out var email) ||
            !string.Equals(email.Address, request.Email.Trim(), StringComparison.Ordinal))
            return Problem(statusCode: 400, title: "Ingresá un correo electrónico válido.");

        try
        {
            var result = await verification.SendAsync(email.Address, ct);
            return result.IsSuccess ? Accepted(result.Value) : FromResult(result);
        }
        catch (RegistrationEmailUnavailableException)
        {
            return Unavailable();
        }
    }

    [HttpPost("confirm")]
    public async Task<ActionResult<RegistrationEmailProof>> Confirm(VerifyRegistrationCodeRequest request, CancellationToken ct)
    {
        if (!verification.Enabled)
            return Unavailable();

        return FromResult(await verification.VerifyAsync(request.Email, request.Code, ct));
    }

    private ObjectResult Unavailable() => Problem(statusCode: 503, title: "El envío de códigos no está disponible. Reintentá en un minuto.",
        extensions: new Dictionary<string, object?> { ["code"] = "EMAIL_VERIFICATION_UNAVAILABLE" });
}
