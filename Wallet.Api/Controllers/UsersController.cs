using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wallet.Application;
namespace Wallet.Api.Controllers;

[ApiController, Route("api/users"), Authorize(Policy = "Client")]
[ProducesResponseType<ProblemDetails>(503)]
public sealed class UsersController(IdentityService identity) : IdentityControllerBase
{
    [HttpPost, AllowAnonymous, EnableRateLimiting("credentials")]
    [Consumes("application/json")]
    [ProducesResponseType<UserResponse>(201)]
    [ProducesResponseType<ValidationProblemDetails>(400)]
    [ProducesResponseType<ProblemDetails>(409)]
    [ProducesResponseType<ProblemDetails>(429)]
    public async Task<ActionResult<UserResponse>> Register(RegisterUserRequest request, CancellationToken ct)
    {
        var result = await identity.RegisterAsync(request, ct);
        return result.IsSuccess ? Created("/api/users/me", result.Value) : FromResult(result);
    }
    [HttpGet("me")]
    [ProducesResponseType<UserResponse>(200)]
    [ProducesResponseType<ProblemDetails>(401)]
    [ProducesResponseType<ProblemDetails>(403)]
    public async Task<ActionResult<UserResponse>> GetMe(CancellationToken ct) =>
        FromResult(await identity.GetProfileAsync(ct));

    [HttpPut("me/profile"), Consumes("application/json")]
    [ProducesResponseType<UserResponse>(200)]
    [ProducesResponseType<ValidationProblemDetails>(400)]
    [ProducesResponseType<ProblemDetails>(401)]
    [ProducesResponseType<ProblemDetails>(403)]
    [ProducesResponseType<ProblemDetails>(409)]
    public async Task<ActionResult<UserResponse>> Update(UpdateProfileRequest request, CancellationToken ct) =>
        FromResult(await identity.UpdateProfileAsync(request, ct));
}
