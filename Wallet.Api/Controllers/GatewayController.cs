using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wallet.Application;

namespace Wallet.Api.Controllers;

[ApiController]
[Route("api/v1/gateway")]
public sealed class GatewayController(
    GatewayService gateway,
    WalletService wallets,
    TransferService transfers,
    MovementHistoryService movements) : ControllerBase
{
    [HttpPost("clients")]
    [AllowAnonymous]
    [ProducesResponseType<RegisterExternalClientResponse>(201)]
    [ProducesResponseType<ProblemDetails>(400)]
    public async Task<ActionResult<RegisterExternalClientResponse>> RegisterClient(
        RegisterExternalClientRequest request,
        CancellationToken ct)
    {
        try
        {
            var response = await gateway.RegisterAsync(request, ct);
            return CreatedAtAction(nameof(GetWallet), new { }, response);
        }
        catch (ArgumentException e)
        {
            return Problem(
                statusCode: 400,
                title: "Entrada inválida al registrar cliente externo.",
                extensions: new Dictionary<string, object?> { ["code"] = "INVALID_INPUT", ["detail"] = e.Message });
        }
    }

    [HttpGet("wallet")]
    [Authorize(Policy = "ExternalClient")]
    [ProducesResponseType<WalletResponse>(200)]
    [ProducesResponseType<ProblemDetails>(401)]
    [ProducesResponseType<ProblemDetails>(404)]
    public async Task<ActionResult<WalletResponse>> GetWallet(
        [FromHeader(Name = "X-Api-Key")] string? apiKey,
        CancellationToken ct)
    {
        var wallet = await wallets.GetMineAsync(ct);
        if (wallet is null)
            return Problem(statusCode: 404, title: "No se encontró una billetera asociada al cliente.",
                extensions: new Dictionary<string, object?> { ["code"] = "WALLET_NOT_FOUND" });

        return Ok(wallet);
    }

    [HttpPost("wallet")]
    [Authorize(Policy = "ExternalClient")]
    [ProducesResponseType<WalletResponse>(201)]
    [ProducesResponseType<WalletResponse>(200)]
    public async Task<ActionResult<WalletResponse>> CreateWallet(
        [FromHeader(Name = "X-Api-Key")] string? apiKey,
        CancellationToken ct)
    {
        var result = await wallets.CreateAsync(ct);
        return StatusCode(result.Created ? StatusCodes.Status201Created : StatusCodes.Status200OK, result.Wallet);
    }

    [HttpPost("payments")]
    [Authorize(Policy = "ExternalClient")]
    [ProducesResponseType<TransferResponse>(201)]
    [ProducesResponseType<TransferResponse>(200)]
    [ProducesResponseType<ProblemDetails>(400)]
    [ProducesResponseType<ProblemDetails>(401)]
    [ProducesResponseType<ProblemDetails>(409)]
    [ProducesResponseType<ProblemDetails>(422)]
    public async Task<ActionResult<TransferResponse>> ProcessPayment(
        TransferRequest request,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        [FromHeader(Name = "X-Api-Key")] string? apiKey,
        CancellationToken ct)
    {
        try
        {
            var result = await transfers.ExecuteAsync(request, idempotencyKey, ct);
            Response.Headers["Idempotency-Replayed"] = result.Created ? "false" : "true";
            return StatusCode(result.Created ? 201 : 200, result.Transfer);
        }
        catch (TransferRejectedException e)
        {
            var (status, title) = e.Code switch
            {
                "SOURCE_WALLET_NOT_FOUND" => (404, "El cliente no tiene una billetera creada."),
                "DESTINATION_UNAVAILABLE" => (422, "La billetera destino no está disponible o no existe."),
                "INSUFFICIENT_FUNDS" => (409, "Saldo insuficiente en la billetera del cliente."),
                "DESTINATION_BALANCE_LIMIT" => (409, "La billetera destino no admite este importe."),
                "IDEMPOTENCY_CONFLICT" => (409, "La clave de idempotencia ya fue utilizada con otros parámetros."),
                "CONCURRENCY_CONFLICT" => (409, "Conflicto de concurrencia. Reintente con la misma clave."),
                "INVALID_SESSION" => (401, "Credenciales o cliente inactivo."),
                "SAME_WALLET" => (400, "Origen y destino deben ser distintos."),
                "INVALID_AMOUNT" => (400, "El importe es inválido."),
                _ => (400, "Datos o clave de idempotencia inválidos.")
            };

            return Problem(
                statusCode: status,
                title: title,
                extensions: new Dictionary<string, object?> { ["code"] = e.Code });
        }
    }

    [HttpGet("movements")]
    [Authorize(Policy = "ExternalClient")]
    [ProducesResponseType<MovementPage>(200)]
    [ProducesResponseType<ProblemDetails>(401)]
    [ProducesResponseType<ProblemDetails>(404)]
    public async Task<ActionResult<MovementPage>> GetMovements(
        [FromHeader(Name = "X-Api-Key")] string? apiKey,
        [FromQuery] int limit = 20,
        [FromQuery] string? cursor = null,
        CancellationToken ct = default)
    {
        var result = await movements.GetAsync(limit, cursor, ct);
        if (result.IsSuccess) return Ok(result.Value);
        return Problem(statusCode: result.Error!.Code == "WALLET_NOT_FOUND" ? 404 : 400, title: result.Error.Message,
            extensions: new Dictionary<string, object?> { ["code"] = result.Error.Code });
    }
}
