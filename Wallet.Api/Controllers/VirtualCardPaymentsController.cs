using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wallet.Application;

namespace Wallet.Api.Controllers;

public sealed record PayWithVirtualCardRequest(string Token, CardPaymentDetails Card);

[ApiController]
[Route("api/v1/public/checkout/padi-card-payments")]
[AllowAnonymous]
[EnableRateLimiting("padi-card-payments")]
public sealed class VirtualCardPaymentsController(VirtualCardService virtualCards) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<CheckoutReceiptDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CheckoutReceiptDto>> Pay(PayWithVirtualCardRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? key, CancellationToken ct)
    {
        try
        {
            var receipt = await virtualCards.PayAsync(request.Token, request.Card, key, ct);
            Response.Headers["Idempotency-Replayed"] = receipt.IdempotencyReplayed ? "true" : "false";
            return Ok(receipt);
        }
        catch (CardPaymentRejectedException ex)
        {
            return PaymentProblem(422, ex.Message, ex.Code);
        }
        catch (TransferRejectedException ex)
        {
            return ex.Code switch
            {
                "INSUFFICIENT_FUNDS" => PaymentProblem(409, "Saldo insuficiente en la billetera de la tarjeta.", ex.Code),
                "SAME_WALLET" => PaymentProblem(400, "La tarjeta y el link pertenecen a la misma billetera.", ex.Code),
                "IDEMPOTENCY_CONFLICT" => PaymentProblem(409, "La clave ya fue usada para otro pago.", ex.Code),
                "CONCURRENCY_CONFLICT" => PaymentProblem(409, "Hubo un conflicto. Reintentá el mismo pago.", ex.Code),
                _ => PaymentProblem(422, "No pudimos autorizar el pago.", ex.Code),
            };
        }
        catch (VirtualCardsUnavailableException)
        {
            return PaymentProblem(503, "Los pagos con tarjeta Padi aún no están habilitados.", "VIRTUAL_CARDS_UNAVAILABLE");
        }
        catch (StorageUnavailableException)
        {
            return PaymentProblem(503, "No pudimos consultar el pago. Reintentá la misma operación.", "STORAGE_UNAVAILABLE");
        }
        catch (KeyNotFoundException)
        {
            return PaymentProblem(404, "Link de pago no encontrado.", "PAYMENT_LINK_NOT_FOUND");
        }
        catch (UnauthorizedAccessException)
        {
            return PaymentProblem(422, "No pudimos autorizar la tarjeta virtual de Padi.", "PADI_CARD_DECLINED");
        }
        catch (InvalidOperationException)
        {
            return PaymentProblem(409, "El link de pago ya no admite pagos.", "LINK_NOT_PAYABLE");
        }
        catch (ArgumentException ex)
        {
            return PaymentProblem(400, ex.Message, "INVALID_INPUT");
        }
    }

    private ObjectResult PaymentProblem(int status, string title, string code) =>
        Problem(statusCode: status, title: title, extensions: new Dictionary<string, object?> { ["code"] = code });
}
