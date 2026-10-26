using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wallet.Application;

namespace Wallet.Api.Controllers;

[ApiController, Route("api/wallets/me/movements"), Authorize(Policy = "Client")]
public sealed class MovementsController(MovementHistoryService history) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<MovementPage>(200)]
    [ProducesResponseType<ProblemDetails>(400)]
    [ProducesResponseType<ProblemDetails>(401)]
    [ProducesResponseType<ProblemDetails>(403)]
    [ProducesResponseType<ProblemDetails>(404)]
    [ProducesResponseType<ProblemDetails>(503)]
    public async Task<ActionResult<MovementPage>> Get(CancellationToken ct, [FromQuery] int limit = 20, [FromQuery] string? cursor = null)
    {
        var result = await history.GetAsync(limit, cursor, ct);
        if (result.IsSuccess) return Ok(result.Value);
        return Problem(statusCode: result.Error!.Code == "WALLET_NOT_FOUND" ? 404 : 400, title: result.Error.Message,
            extensions: new Dictionary<string, object?> { ["code"] = result.Error.Code });
    }
}
