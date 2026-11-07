using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wallet.Application;
using Wallet.Domain;

namespace Wallet.Api.Controllers;

[ApiController]
[Route("api/v1/cards/requests")]
[Authorize(Policy = "Client")]
public sealed class CardRequestsController(ICardService cardService, ICurrentUser currentUser) : ControllerBase
{
    [HttpPost("/api/v1/me/physical-card-orders", Order = -1)]
    [HttpPost]
    [ProducesResponseType<CardRequestDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<CardRequestDto>> CreateRequest(
        [FromBody] CreateCardRequestRequest? request,
        CancellationToken ct)
    {
        try
        {
            var req = request ?? new CreateCardRequestRequest { Type = CardType.Debit };
            var result = await cardService.CreateCardRequestAsync(currentUser.Id, req, ct);
            return CreatedAtAction(nameof(GetStatus), new { id = result.Id }, result);
        }
        catch (KeyNotFoundException)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Delivery address not found.");
        }
        catch (InvalidOperationException ex)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message);
        }
        catch (ArgumentException ex)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: ex.Message,
                extensions: new Dictionary<string, object?> { ["code"] = "INVALID_CARD_REQUEST" });
        }
    }

    [HttpGet("/api/v1/me/physical-card-orders/{id:guid}", Order = -1)]
    [HttpGet("{id:guid}/status")]
    [ProducesResponseType<CardRequestDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<CardRequestDto>> GetStatus(
        [FromRoute] Guid id,
        CancellationToken ct)
    {
        var status = await cardService.GetCardRequestStatusAsync(id, currentUser.Id, ct);
        if (status is null)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Card request not found.",
                extensions: new Dictionary<string, object?> { ["code"] = "CARD_REQUEST_NOT_FOUND" });
        }

        return Ok(status);
    }
}
