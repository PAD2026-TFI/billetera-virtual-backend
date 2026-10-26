using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wallet.Application;

namespace Wallet.Api.Controllers;

[ApiController, Route("api/transfers"), Authorize(Policy = "Client")]
public sealed class TransfersController(TransferService transfers) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<TransferResponse>(201)]
    [ProducesResponseType<TransferResponse>(200)]
    [ProducesResponseType<ProblemDetails>(400)]
    [ProducesResponseType<ProblemDetails>(401)]
    [ProducesResponseType<ProblemDetails>(403)]
    [ProducesResponseType<ProblemDetails>(404)]
    [ProducesResponseType<ProblemDetails>(409)]
    [ProducesResponseType<ProblemDetails>(422)]
    [ProducesResponseType<ProblemDetails>(503)]
    public async Task<ActionResult<TransferResponse>> Create(TransferRequest request,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey, CancellationToken ct)
    {
        try
        {
            var result = await transfers.ExecuteAsync(request, idempotencyKey, ct);
            return StatusCode(result.Created ? 201 : 200, result.Transfer);
        }
        catch (TransferRejectedException e)
        {
            var (status, title) = e.Code switch
            {
                "SOURCE_WALLET_NOT_FOUND" => (404, "No tenés una billetera creada."),
                "DESTINATION_UNAVAILABLE" => (422, "La billetera de destino no está disponible."),
                "INSUFFICIENT_FUNDS" => (409, "Saldo insuficiente."),
                "DESTINATION_BALANCE_LIMIT" => (409, "El destino no admite este importe."),
                "IDEMPOTENCY_CONFLICT" => (409, "La clave ya identifica una transferencia con otros datos."),
                "CONCURRENCY_CONFLICT" => (409, "Conflicto concurrente. Reintentá con la misma clave."),
                "INVALID_SESSION" => (401, "Sesión inválida."),
                "SAME_WALLET" => (400, "Origen y destino deben ser distintos."),
                "INVALID_AMOUNT" => (400, "El importe debe estar entre 0,01 y 780400 ARS, sin fracciones de centavo."),
                _ => (400, "Datos o clave de idempotencia inválidos.")
            };
            if (status == 401) Response.Headers.WWWAuthenticate = "Bearer";
            return Problem(statusCode: status, title: title,
                extensions: new Dictionary<string, object?> { ["code"] = e.Code });
        }
    }
}
