using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QRCoder;
using Wallet.Application;

namespace Wallet.Api.Controllers;

[ApiController]
[Route("api/v1/checkout")]
public sealed class CheckoutController(PaymentLinkService paymentLinks) : ControllerBase
{
    [HttpPost("links")]
    [AllowAnonymous]
    [ProducesResponseType<PaymentLinkDto>(201)]
    [ProducesResponseType<ProblemDetails>(400)]
    public async Task<ActionResult<PaymentLinkDto>> CreateLink(
        [FromBody] CreatePaymentLinkRequest request,
        CancellationToken ct)
    {
        try
        {
            var result = await paymentLinks.CreateLinkAsync(request, ct);
            return CreatedAtAction(nameof(GetLinkByToken), new { token = result.Token }, result);
        }
        catch (ArgumentException ex)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Datos de link de pago inválidos.",
                extensions: new Dictionary<string, object?> { ["code"] = "INVALID_INPUT", ["detail"] = ex.Message });
        }
    }

    [HttpGet("links/{token}")]
    [AllowAnonymous]
    [ProducesResponseType<CheckoutPublicDetailsDto>(200)]
    [ProducesResponseType<ProblemDetails>(404)]
    public async Task<ActionResult<CheckoutPublicDetailsDto>> GetLinkByToken(
        [FromRoute] string token,
        CancellationToken ct)
    {
        var details = await paymentLinks.GetByTokenAsync(token, ct);
        if (details is null)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Link de pago no encontrado.",
                extensions: new Dictionary<string, object?> { ["code"] = "PAYMENT_LINK_NOT_FOUND" });
        }

        return Ok(details);
    }

    [HttpPost("pay")]
    [AllowAnonymous]
    [ProducesResponseType<CheckoutReceiptDto>(200)]
    [ProducesResponseType<ProblemDetails>(400)]
    [ProducesResponseType<ProblemDetails>(404)]
    [ProducesResponseType<ProblemDetails>(409)]
    [ProducesResponseType<ProblemDetails>(422)]
    public async Task<ActionResult<CheckoutReceiptDto>> Pay(
        [FromBody] ProcessCheckoutRequest request,
        [FromQuery] string? token,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        var effectiveToken = !string.IsNullOrWhiteSpace(request.Token)
            ? request.Token
            : token;

        if (string.IsNullOrWhiteSpace(effectiveToken))
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Debe proveer el token del link de pago.",
                extensions: new Dictionary<string, object?> { ["code"] = "TOKEN_REQUIRED" });
        }

        try
        {
            var receipt = await paymentLinks.ProcessCheckoutAsync(effectiveToken, request, idempotencyKey, ct);
            Response.Headers["Idempotency-Replayed"] = receipt.IdempotencyReplayed ? "true" : "false";
            return Ok(receipt);
        }
        catch (KeyNotFoundException ex)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: ex.Message,
                extensions: new Dictionary<string, object?> { ["code"] = "PAYMENT_LINK_NOT_FOUND" });
        }
        catch (InvalidOperationException ex)
        {
            var status = ex.Message.Contains("iniciar sesión", StringComparison.OrdinalIgnoreCase)
                ? StatusCodes.Status401Unauthorized
                : StatusCodes.Status409Conflict;

            return Problem(
                statusCode: status,
                title: ex.Message,
                extensions: new Dictionary<string, object?> { ["code"] = status == 401 ? "AUTH_REQUIRED" : "LINK_NOT_PAYABLE" });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: ex.Message,
                extensions: new Dictionary<string, object?> { ["code"] = "UNAUTHORIZED" });
        }
        catch (CardPaymentRejectedException ex)
        {
            return Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: ex.Message,
                extensions: new Dictionary<string, object?> { ["code"] = ex.Code });
        }
        catch (TransferRejectedException ex)
        {
            var (status, title) = ex.Code switch
            {
                "SOURCE_WALLET_NOT_FOUND" => (404, "No tenés una billetera creada para realizar el pago."),
                "INSUFFICIENT_FUNDS" => (409, "Saldo insuficiente en tu billetera."),
                "DESTINATION_BALANCE_LIMIT" => (409, "La billetera destino excede el límite operativo."),
                "IDEMPOTENCY_CONFLICT" => (409, "La clave de idempotencia ya fue utilizada con otros parámetros."),
                "SAME_WALLET" => (400, "Origen y destino deben ser distintos."),
                _ => (400, "Error en la transferencia de billetera.")
            };

            return Problem(
                statusCode: status,
                title: title,
                extensions: new Dictionary<string, object?> { ["code"] = ex.Code });
        }
        catch (ArgumentException ex)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: ex.Message,
                extensions: new Dictionary<string, object?> { ["code"] = "INVALID_INPUT" });
        }
    }

    [HttpGet("links/{token}/qr")]
    [AllowAnonymous]
    [Produces("image/svg+xml")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(404)]
    public async Task<IActionResult> GetQrCode(
        [FromRoute] string token,
        CancellationToken ct)
    {
        var details = await paymentLinks.GetByTokenAsync(token, ct);
        if (details is null)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Link de pago no encontrado.",
                extensions: new Dictionary<string, object?> { ["code"] = "PAYMENT_LINK_NOT_FOUND" });
        }

        var scheme = Request.Scheme;
        var host = Request.Host.Value;
        var checkoutUrl = $"{scheme}://{host}/checkout/index.html?token={token}";

        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(checkoutUrl, QRCodeGenerator.ECCLevel.Q);
        using var qrCode = new SvgQRCode(qrCodeData);
        var svg = qrCode.GetGraphic(20);

        return Content(svg, "image/svg+xml", Encoding.UTF8);
    }
}
