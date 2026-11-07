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
    MovementHistoryService movements,
    PaymentLinkService paymentLinks,
    ICurrentUser currentUser) : ControllerBase
{
    /// <summary>
    /// Registro y onboarding de clientes externos / comercios B2B.
    /// Crea la entidad ExternalClient, su billetera atómica con CVU/Alias, y emite la API Key en texto plano una única vez.
    /// </summary>
    [HttpPost("/api/v1/integrations/register", Order = -1)]
    [HttpPost("/api/v1/merchants/register")]
    [AllowAnonymous]
    [ProducesResponseType<RegisterExternalClientResponse>(201)]
    [ProducesResponseType<ProblemDetails>(400)]
    public async Task<ActionResult<RegisterExternalClientResponse>> RegisterMerchant(
        RegisterExternalClientRequest request,
        CancellationToken ct) =>
        await RegisterClient(request, ct);

    [HttpPost("clients")]
    [AllowAnonymous]
    [ApiExplorerSettings(IgnoreApi = true)]
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

    [HttpGet("/api/v1/integrations/profile", Order = -1)]
    [HttpGet("me")]
    [Authorize(Policy = "ExternalClient")]
    [ProducesResponseType<ExternalClientDetailsResponse>(200)]
    [ProducesResponseType<ProblemDetails>(401)]
    [ProducesResponseType<ProblemDetails>(404)]
    public async Task<ActionResult<ExternalClientDetailsResponse>> GetMe(CancellationToken ct)
    {
        var client = await gateway.GetClientByIdAsync(currentUser.Id, ct);
        if (client is null)
            return Problem(statusCode: 404, title: "Comercio no encontrado.",
                extensions: new Dictionary<string, object?> { ["code"] = "CLIENT_NOT_FOUND" });

        return Ok(client);
    }

    [HttpPut("/api/v1/integrations/webhook", Order = -1)]
    [HttpPut("webhook")]
    [Authorize(Policy = "ExternalClient")]
    [ProducesResponseType<ExternalClientDetailsResponse>(200)]
    [ProducesResponseType<ProblemDetails>(400)]
    [ProducesResponseType<ProblemDetails>(401)]
    [ProducesResponseType<ProblemDetails>(404)]
    public async Task<ActionResult<ExternalClientDetailsResponse>> ConfigureWebhook(
        [FromBody] ConfigureWebhookRequest request,
        CancellationToken ct)
    {
        try
        {
            var response = await gateway.ConfigureWebhookAsync(currentUser.Id, request, ct);
            return Ok(response);
        }
        catch (ArgumentException e)
        {
            return Problem(
                statusCode: 400,
                title: "Datos de webhook inválidos.",
                extensions: new Dictionary<string, object?> { ["code"] = "INVALID_WEBHOOK", ["detail"] = e.Message });
        }
        catch (KeyNotFoundException)
        {
            return Problem(statusCode: 404, title: "Comercio no encontrado.",
                extensions: new Dictionary<string, object?> { ["code"] = "CLIENT_NOT_FOUND" });
        }
    }

    [HttpDelete("/api/v1/integrations/webhook", Order = -1)]
    [HttpDelete("webhook")]
    [Authorize(Policy = "ExternalClient")]
    [ProducesResponseType<ExternalClientDetailsResponse>(200)]
    [ProducesResponseType<ProblemDetails>(401)]
    [ProducesResponseType<ProblemDetails>(404)]
    public async Task<ActionResult<ExternalClientDetailsResponse>> DeactivateWebhook(CancellationToken ct)
    {
        try
        {
            var response = await gateway.DeactivateWebhookAsync(currentUser.Id, ct);
            return Ok(response);
        }
        catch (KeyNotFoundException)
        {
            return Problem(statusCode: 404, title: "Comercio no encontrado.",
                extensions: new Dictionary<string, object?> { ["code"] = "CLIENT_NOT_FOUND" });
        }
    }

    [HttpGet("/api/v1/integrations/wallet", Order = -1)]
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
    [ApiExplorerSettings(IgnoreApi = true)]
    [ProducesResponseType<WalletResponse>(201)]
    [ProducesResponseType<WalletResponse>(200)]
    public async Task<ActionResult<WalletResponse>> CreateWallet(
        [FromHeader(Name = "X-Api-Key")] string? apiKey,
        CancellationToken ct)
    {
        var result = await wallets.CreateAsync(ct);
        return StatusCode(result.Created ? StatusCodes.Status201Created : StatusCodes.Status200OK, result.Wallet);
    }

    [HttpPost("/api/v1/integrations/transfers", Order = -1)]
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
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        [FromHeader(Name = "X-Api-Key")] string? apiKey,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            idempotencyKey = Guid.NewGuid().ToString("D");
        }

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

    [HttpGet("/api/v1/integrations/wallet/movements", Order = -1)]
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

    [HttpGet("/api/v1/integrations/received-payments/{paymentId:guid}", Order = -1)]
    [HttpGet("checkout-payments/{paymentId:guid}")]
    [Authorize(Policy = "ExternalClient")]
    [ProducesResponseType<MerchantPaymentDto>(200)]
    [ProducesResponseType<ProblemDetails>(401)]
    [ProducesResponseType<ProblemDetails>(404)]
    public async Task<ActionResult<MerchantPaymentDto>> GetCheckoutPaymentById(
        [FromRoute] Guid paymentId,
        [FromHeader(Name = "X-Api-Key")] string? apiKey,
        CancellationToken ct)
    {
        var payment = await paymentLinks.GetPaymentByIdAsync(paymentId, currentUser.Id, ct);
        if (payment is null)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Pago no encontrado o no pertenece a este comercio.",
                extensions: new Dictionary<string, object?> { ["code"] = "PAYMENT_NOT_FOUND" });
        }

        return Ok(payment);
    }

    [HttpGet("/api/v1/integrations/received-payments", Order = -1)]
    [HttpGet("checkout-payments")]
    [Authorize(Policy = "ExternalClient")]
    [ProducesResponseType<IReadOnlyList<MerchantPaymentDto>>(200)]
    [ProducesResponseType<ProblemDetails>(401)]
    public async Task<ActionResult<IReadOnlyList<MerchantPaymentDto>>> GetReceivedCheckoutPayments(
        [FromHeader(Name = "X-Api-Key")] string? apiKey,
        [FromQuery] int limit = 20,
        CancellationToken ct = default)
    {
        var list = await paymentLinks.GetPaymentsByMerchantAsync(currentUser.Id, limit, ct);
        return Ok(list);
    }
}
