using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wallet.Application;
namespace Wallet.Api.Controllers;

public sealed record HealthResponse(string Status);
[ApiController, Route("health"), AllowAnonymous]
public sealed class HealthController(IConfiguration configuration) : ControllerBase
{
    [HttpGet("live"), ProducesResponseType<HealthResponse>(200)]
    public ActionResult<HealthResponse> Live() => Ok(new HealthResponse("Healthy"));

    [HttpGet("ready"), ProducesResponseType<HealthResponse>(200), ProducesResponseType<HealthResponse>(503)]
    public async Task<ActionResult<HealthResponse>> Ready(CancellationToken ct)
    {
        if (!configuration.GetValue<bool>("Identity:Enabled"))
            return StatusCode(503, new HealthResponse("Unhealthy"));
        var repository = HttpContext.RequestServices.GetRequiredService<IUserRepository>();
        return await repository.IsReadyAsync(ct)
            ? Ok(new HealthResponse("Healthy"))
            : StatusCode(503, new HealthResponse("Unhealthy"));
    }
}
