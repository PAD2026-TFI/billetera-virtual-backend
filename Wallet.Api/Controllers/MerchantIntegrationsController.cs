using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wallet.Application;

namespace Wallet.Api.Controllers;

[ApiController]
[Route("api/v1/me/merchant/integration")]
[Authorize(Policy = "Client")]
public sealed class MerchantIntegrationsController(MerchantIntegrationService integrations) : ControllerBase
{
    [HttpGet]
    public Task<ActionResult<MerchantIntegrationResponse>> Get(CancellationToken ct) =>
        Execute(() => integrations.GetAsync(ct));

    [HttpPut("/api/v1/me/account-type")]
    public Task<ActionResult<UserResponse>> SetAccountType(AccountTypeRequest request, CancellationToken ct) =>
        Execute(() => integrations.SetAccountTypeAsync(request, ct));

    [HttpPost]
    [EnableRateLimiting("credentials")]
    public Task<ActionResult<MerchantIntegrationKeyResponse>> Create(MerchantIntegrationRequest request, CancellationToken ct) =>
        Execute(() => integrations.IssueAsync(request.Name, false, ct));

    [HttpPost("rotate")]
    [EnableRateLimiting("credentials")]
    public Task<ActionResult<MerchantIntegrationKeyResponse>> Rotate(CancellationToken ct) =>
        Execute(() => integrations.IssueAsync(null, true, ct));

    [HttpDelete]
    public Task<ActionResult<MerchantIntegrationResponse>> Revoke(CancellationToken ct) =>
        Execute(() => integrations.RevokeAsync(ct));

    private async Task<ActionResult<T>> Execute<T>(Func<Task<T>> operation)
    {
        // Secret responses must never be cached, including responses served outside Program's middleware.
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        try { return Ok(await operation()); }
        catch (MerchantIntegrationException e)
        {
            return Problem(statusCode: e.Code is "ACCESS_DENIED" or "MERCHANT_REQUIRED" ? 403 : 409,
                title: e.Message, extensions: new Dictionary<string, object?> { ["code"] = e.Code });
        }
        catch (ConcurrentUpdateException)
        {
            return Problem(statusCode: 409, title: "La integración cambió. Consultá su estado antes de volver a intentar.",
                extensions: new Dictionary<string, object?> { ["code"] = "INTEGRATION_CONFLICT" });
        }
        catch (ArgumentException)
        {
            return Problem(statusCode: 400, title: "Revisá los datos de la integración.",
                extensions: new Dictionary<string, object?> { ["code"] = "INVALID_INPUT" });
        }
    }
}
