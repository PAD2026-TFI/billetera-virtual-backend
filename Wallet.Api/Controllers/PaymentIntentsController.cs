using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wallet.Application;

namespace Wallet.Api.Controllers;

[ApiController]
[Route("api/v1/payment-intents")]
[Authorize(Policy = "PaymentIntegration")]
public sealed class PaymentIntentsController(
    ICheckoutService checkoutService,
    PaymentLinkService paymentLinkService,
    ICurrentUser currentUser) : ControllerBase
{
    /// <summary>
    /// Genera una nueva intención de cobro omnicanal para el comercio B2B identificado por su X-Api-Key.
    /// Retorna el token, la URL pública de checkout y la URL del código QR dinámico.
    /// </summary>
    [HttpPost("/api/v1/integrations/payment-links", Order = -1)]
    [HttpPost]
    [ProducesResponseType<PaymentIntentResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PaymentIntentResponse>> Create(
        [FromBody] CreatePaymentIntentRequest request,
        [FromHeader(Name = "X-Api-Key")] string? apiKey,
        CancellationToken ct)
    {
        try
        {
            var linkRequest = new CreatePaymentLinkRequest
            {
                Amount = request.Amount,
                Title = request.Title,
                Currency = request.Currency ?? "ARS",
                Description = request.Description,
                Type = request.Type,
                TtlMinutes = request.TtlMinutes
            };

            var created = await checkoutService.CreateLinkAsync(linkRequest, ct);

            var scheme = Request.Scheme;
            var host = Request.Host.Value;
            var checkoutUrl = $"{scheme}://{host}/checkout?token={created.Token}";
            var qrCodeUrl = $"{scheme}://{host}/api/v1/public/payment-links/{created.Token}/qr";

            var response = new PaymentIntentResponse(
                created.Id,
                created.Token,
                created.Amount,
                created.Currency,
                created.Title,
                created.Description,
                created.Status,
                created.Type,
                created.CreatedAt,
                created.ExpiresAt,
                checkoutUrl,
                qrCodeUrl);

            return CreatedAtAction(nameof(GetById), new { id = response.Id }, response);
        }
        catch (ArgumentException ex)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Parámetros inválidos al crear intención de pago.",
                extensions: new Dictionary<string, object?> { ["code"] = "INVALID_INPUT", ["detail"] = ex.Message });
        }
    }

    /// <summary>
    /// Consulta los detalles y estado actual de una intención de pago perteneciente al comercio autenticado.
    /// </summary>
    [HttpGet("/api/v1/integrations/payment-links/{id:guid}", Order = -1)]
    [HttpGet("{id:guid}")]
    [ProducesResponseType<PaymentIntentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PaymentIntentResponse>> GetById(
        [FromRoute] Guid id,
        [FromHeader(Name = "X-Api-Key")] string? apiKey,
        CancellationToken ct)
    {
        var link = await paymentLinkService.GetByIdAsync(id, currentUser.Id, ct);
        if (link is null)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Intención de pago no encontrada o no pertenece al comercio autenticado.",
                extensions: new Dictionary<string, object?> { ["code"] = "PAYMENT_INTENT_NOT_FOUND" });
        }

        var scheme = Request.Scheme;
        var host = Request.Host.Value;
        var checkoutUrl = $"{scheme}://{host}/checkout?token={link.Token}";
        var qrCodeUrl = $"{scheme}://{host}/api/v1/public/payment-links/{link.Token}/qr";

        var response = new PaymentIntentResponse(
            link.Id,
            link.Token,
            link.Amount,
            link.Currency,
            link.Title,
            link.Description,
            link.Status,
            link.Type,
            link.CreatedAt,
            link.ExpiresAt,
            checkoutUrl,
            qrCodeUrl);

        return Ok(response);
    }
}
