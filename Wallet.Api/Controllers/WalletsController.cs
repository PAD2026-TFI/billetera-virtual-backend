using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wallet.Application;

namespace Wallet.Api.Controllers;

[ApiController, Route("api/wallets"), Authorize(Policy = "Client")]
public sealed class WalletsController(WalletService wallets) : ControllerBase
{
    [HttpGet("me")]
    [ProducesResponseType<WalletResponse>(200)]
    [ProducesResponseType<ProblemDetails>(401)]
    [ProducesResponseType<ProblemDetails>(403)]
    [ProducesResponseType<ProblemDetails>(404)]
    [ProducesResponseType<ProblemDetails>(503)]
    public async Task<ActionResult<WalletResponse>> GetMine(CancellationToken ct)
    {
        var wallet = await wallets.GetMineAsync(ct);
        if (wallet is null)
            return Problem(statusCode: 404, title: "No tenés una billetera creada.",
                extensions: new Dictionary<string, object?> { ["code"] = "WALLET_NOT_FOUND" });
        return Ok(wallet);
    }

    [HttpPost]
    [ProducesResponseType<WalletResponse>(201)]
    [ProducesResponseType<WalletResponse>(200)]
    [ProducesResponseType<ProblemDetails>(400)]
    [ProducesResponseType<ProblemDetails>(401)]
    [ProducesResponseType<ProblemDetails>(403)]
    [ProducesResponseType<ProblemDetails>(503)]
    public async Task<ActionResult<WalletResponse>> Create(CancellationToken ct)
    {
        // Sin DTO de entrada: ni siquiera un cuerpo {} forma parte del contrato.
        var firstByte = new byte[1];
        if (await Request.Body.ReadAsync(firstByte.AsMemory(), ct) != 0)
            return Problem(statusCode: 400, title: "La solicitud no admite un cuerpo.",
                extensions: new Dictionary<string, object?> { ["code"] = "INVALID_INPUT" });

        var result = await wallets.CreateAsync(ct);
        return StatusCode(result.Created ? StatusCodes.Status201Created : StatusCodes.Status200OK, result.Wallet);
    }

    [HttpPatch("alias")]
    [ProducesResponseType<WalletResponse>(200)]
    [ProducesResponseType<ProblemDetails>(400)]
    [ProducesResponseType<ProblemDetails>(404)]
    [ProducesResponseType<ProblemDetails>(409)]
    public async Task<ActionResult<WalletResponse>> ModifyAlias(
        [FromBody] ModifyAliasRequest request,
        CancellationToken ct)
    {
        try
        {
            var updated = await wallets.ModifyAliasAsync(request.NewAlias, ct);
            return Ok(updated);
        }
        catch (KeyNotFoundException)
        {
            return Problem(statusCode: 404, title: "No tenés una billetera creada.",
                extensions: new Dictionary<string, object?> { ["code"] = "WALLET_NOT_FOUND" });
        }
        catch (InvalidOperationException ex)
        {
            return Problem(statusCode: 409, title: ex.Message,
                extensions: new Dictionary<string, object?> { ["code"] = "ALIAS_ALREADY_EXISTS" });
        }
        catch (ArgumentException ex)
        {
            return Problem(statusCode: 400, title: ex.Message,
                extensions: new Dictionary<string, object?> { ["code"] = "INVALID_ALIAS" });
        }
    }
}
