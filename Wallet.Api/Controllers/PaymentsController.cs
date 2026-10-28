using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wallet.Api.Payments;
using Wallet.Application;

namespace Wallet.Api.Controllers;

[ApiController, Route("api/payments"), Authorize(Policy = "Client")]
[ProducesResponseType<ProblemDetails>(400)]
[ProducesResponseType<ProblemDetails>(401)]
[ProducesResponseType<ProblemDetails>(403)]
[ProducesResponseType<ProblemDetails>(404)]
[ProducesResponseType<ProblemDetails>(409)]
[ProducesResponseType<ProblemDetails>(503)]
public sealed class PaymentsController(PaymentService payments, PaymentProcessor processor, IPaymentStore store,
    ICurrentUser user, PaymentSettings settings) : ControllerBase
{
    [HttpGet("config")]
    public IActionResult Config() => Ok(new { settings.Enabled, settings.MaxTopUpAmount, Currency = "ARS", Sandbox = true });

    [HttpPost, EnableRateLimiting("payments")]
    [ProducesResponseType<PaymentResponse>(201)]
    [ProducesResponseType<PaymentResponse>(200)]
    [ProducesResponseType<PaymentResponse>(202)]
    public async Task<ActionResult<PaymentResponse>> Create(PaymentRequest request,
        [FromHeader(Name = "Idempotency-Key"), Required] string key, CancellationToken ct)
    {
        var result = await payments.StartAsync(request, key, ct);
        Response.Headers["Idempotency-Replayed"] = result.Created ? "false" : "true";
        Response.Headers.Location = $"/api/payments/{result.Payment.Id}";
        if (result.Payment.Status is "Created" or "Unknown") return StatusCode(202, result.Payment);
        return StatusCode(result.Created ? 201 : 200, result.Payment);
    }
    [HttpGet("{id:guid}"), ProducesResponseType<PaymentResponse>(200)]
    public async Task<ActionResult<PaymentResponse>> Get(Guid id, CancellationToken ct) => Ok(await payments.GetAsync(id, ct));

    // Consulta al proveedor la MISMA operación; nunca crea otra preferencia ni acepta estados del cliente.
    [HttpPost("{id:guid}/refresh"), EnableRateLimiting("payments")]
    [ProducesResponseType<PaymentResponse>(200)]
    public async Task<ActionResult<PaymentResponse>> Refresh(Guid id, CancellationToken ct)
    {
        if (!settings.Enabled) throw new PaymentRejectedException("PAYMENTS_DISABLED");
        var payment = await store.GetOwnedAsync(user.Id, id, ct) ?? throw new PaymentRejectedException("PAYMENT_NOT_FOUND");
        await processor.ReconcileAsync(payment, ct);
        return Ok(await payments.GetAsync(id, ct));
    }
}

[ApiController, Route("api/payments/mercadopago/webhook"), AllowAnonymous]
public sealed class MercadoPagoWebhookController(MercadoPagoSignature signature, PaymentProcessor processor,
    PaymentSettings settings) : ControllerBase
{
    [HttpPost, ProducesResponseType(200), ProducesResponseType(400), ProducesResponseType(401), ProducesResponseType(503)]
    public async Task<IActionResult> Receive(JsonElement body, CancellationToken ct)
    {
        if (!settings.Enabled) throw new PaymentRejectedException("PAYMENTS_DISABLED");
        var ids = Request.Query["data.id"]; var signatures = Request.Headers["x-signature"]; var requests = Request.Headers["x-request-id"];
        if (ids.Count != 1 || signatures.Count != 1 || requests.Count != 1
            || !signature.Verify(ids[0] ?? "", requests[0] ?? "", signatures[0] ?? ""))
            return Problem(statusCode: 401, title: "Notificación no autenticada.");
        if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("type", out var type)
            || type.ValueKind != JsonValueKind.String) return BadRequest();
        if (type.GetString() != "payment") return Ok();
        if (!body.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("id", out var id) || id.ValueKind is not (JsonValueKind.String or JsonValueKind.Number)) return BadRequest();
        var bodyId = id.ValueKind == JsonValueKind.String ? id.GetString() : id.GetRawText();
        if (bodyId != ids[0]) return BadRequest();
        // Sin cola en memoria: 200 sólo después de consultar al proveedor y confirmar SQL.
        // Ante falla devuelvo error para permitir reenvío; el recuperador cubre notificaciones perdidas.
        await processor.ProcessAsync(ids[0]!, ct); return Ok();
    }
}
