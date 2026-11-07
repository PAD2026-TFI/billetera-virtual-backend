using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wallet.Application;

namespace Wallet.Api.Controllers;

[ApiController]
[Route("api/v1/cards")]
[Authorize(Policy = "Client")]
public sealed class CardsController(ICardService cardService, ICurrentUser currentUser) : ControllerBase
{
    [HttpGet("/api/v1/me/cards", Order = -1)]
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<CardDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<CardDto>>> GetUserCards(CancellationToken ct)
    {
        var cards = await cardService.GetUserCardsAsync(currentUser.Id, ct);
        return Ok(cards);
    }

    [HttpPost("/api/v1/me/cards", Order = -1)]
    [HttpPost]
    [ProducesResponseType<CardDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<CardDto>> AddCard(
        [FromBody] CreateCardCommand command,
        CancellationToken ct)
    {
        try
        {
            var card = await cardService.AddCardAsync(currentUser.Id, command, ct);
            return StatusCode(StatusCodes.Status201Created, card);
        }
        catch (ArgumentException ex)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: ex.Message,
                extensions: new Dictionary<string, object?> { ["code"] = "INVALID_CARD_DATA" });
        }
    }
}
