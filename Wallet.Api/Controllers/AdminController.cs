using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wallet.Application;
using DomainUser = Wallet.Domain.User;
using DomainWallet = Wallet.Domain.Wallet;

namespace Wallet.Api.Controllers;

[ApiController]
[Route("api/admin")]
public sealed class AdminController(
    IUserRepository users,
    IWalletRepository wallets,
    IPasswordService passwords,
    DevelopmentFundingService fundingService,
    TimeProvider clock) : ControllerBase
{
    /// <summary>
    /// Permite crear una cuenta con rol de Administrador para pruebas y administración de la plataforma.
    /// </summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType<UserResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserResponse>> RegisterAdmin(
        [FromBody] RegisterAdminRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.FirstName) ||
            string.IsNullOrWhiteSpace(request.LastName) ||
            string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Password) ||
            request.Password.Length < 12)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Revisá los campos obligatorios y sus límites (contraseña mínima 12 caracteres).",
                extensions: new Dictionary<string, object?> { ["code"] = "INVALID_INPUT" });
        }

        var normalizedEmail = DomainUser.NormalizeEmail(request.Email);
        var existing = await users.FindByEmailAsync(normalizedEmail, ct);
        if (existing is not null)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "El email ya está registrado.",
                extensions: new Dictionary<string, object?> { ["code"] = "EMAIL_ALREADY_REGISTERED" });
        }

        var now = clock.GetUtcNow();
        var effectiveDni = string.IsNullOrWhiteSpace(request.Dni) ? "00000000" : request.Dni.Trim();

        DomainUser admin;
        try
        {
            admin = DomainUser.RegisterAdmin(effectiveDni, request.FirstName.Trim(), request.LastName.Trim(), request.Email.Trim(), now);
        }
        catch (ArgumentException ex)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: ex.Message,
                extensions: new Dictionary<string, object?> { ["code"] = "INVALID_INPUT" });
        }

        admin.SetPasswordHash(passwords.Hash(admin, request.Password));
        await users.AddAsync(admin, ct);

        return StatusCode(
            StatusCodes.Status201Created,
            new UserResponse(admin.Id, admin.FirstName, admin.LastName, admin.Email));
    }

    /// <summary>
    /// Permite al Administrador inyectar fondos directamente en cualquier billetera (por CVU, Alias o WalletId).
    /// </summary>
    [HttpPost("funds")]
    [Authorize(Policy = "Admin")]
    [ApiExplorerSettings(IgnoreApi = true)]
    [ProducesResponseType<AdminFundWalletResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdminFundWalletResponse>> FundWallet(
        [FromBody] AdminFundWalletRequest request,
        CancellationToken ct)
    {
        if (request.Amount <= 0)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "El monto debe ser mayor a cero.",
                extensions: new Dictionary<string, object?> { ["code"] = "INVALID_AMOUNT" });
        }

        DomainWallet? targetWallet = null;

        if (!string.IsNullOrWhiteSpace(request.Cvu))
        {
            targetWallet = await wallets.FindByCvuAsync(request.Cvu.Trim(), ct);
        }
        else if (!string.IsNullOrWhiteSpace(request.Alias))
        {
            targetWallet = await wallets.FindByAliasAsync(request.Alias.Trim().ToLowerInvariant(), ct);
        }
        else if (request.WalletId.HasValue && request.WalletId != Guid.Empty)
        {
            targetWallet = await wallets.FindByIdAsync(request.WalletId.Value, ct);
        }
        else
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Debes especificar al menos Cvu, Alias o WalletId de la billetera destino.",
                extensions: new Dictionary<string, object?> { ["code"] = "INVALID_INPUT" });
        }

        if (targetWallet is null)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Billetera destino no encontrada.",
                extensions: new Dictionary<string, object?> { ["code"] = "WALLET_NOT_FOUND" });
        }

        try
        {
            var result = await fundingService.FundAsync(
                new DevelopmentFundingRequest(targetWallet.Id, request.Amount, Guid.NewGuid()),
                ct);

            return Ok(new AdminFundWalletResponse(
                result.Id,
                result.WalletId,
                targetWallet.Cvu,
                targetWallet.Alias,
                result.Amount,
                result.BalanceAfter,
                result.Currency,
                result.CreatedAt));
        }
        catch (FundingRejectedException ex)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: ex.Code,
                extensions: new Dictionary<string, object?> { ["code"] = ex.Code });
        }
    }

    /// <summary>
    /// Permite al Administrador inyectar fondos en una billetera indicando su CVU de 22 dígitos.
    /// </summary>
    [HttpPost("funds/cvu")]
    [Authorize(Policy = "Admin")]
    [ProducesResponseType<AdminFundWalletResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdminFundWalletResponse>> FundByCvu(
        [FromBody] FundByCvuRequest request,
        CancellationToken ct)
    {
        if (request.Amount <= 0)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "El monto debe ser mayor a cero.",
                extensions: new Dictionary<string, object?> { ["code"] = "INVALID_AMOUNT" });
        }

        var trimmedCvu = request.Cvu?.Trim() ?? "";
        var targetWallet = await wallets.FindByCvuAsync(trimmedCvu, ct);
        if (targetWallet is null)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: $"No se encontró ninguna billetera con el CVU '{request.Cvu}'.",
                extensions: new Dictionary<string, object?> { ["code"] = "WALLET_NOT_FOUND" });
        }

        try
        {
            var result = await fundingService.FundAsync(
                new DevelopmentFundingRequest(targetWallet.Id, request.Amount, Guid.NewGuid()),
                ct);

            return Ok(new AdminFundWalletResponse(
                result.Id,
                result.WalletId,
                targetWallet.Cvu,
                targetWallet.Alias,
                result.Amount,
                result.BalanceAfter,
                result.Currency,
                result.CreatedAt));
        }
        catch (FundingRejectedException ex)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: ex.Code,
                extensions: new Dictionary<string, object?> { ["code"] = ex.Code });
        }
    }

    /// <summary>
    /// Permite al Administrador inyectar fondos en una billetera indicando su Alias de transferencia.
    /// </summary>
    [HttpPost("funds/alias")]
    [Authorize(Policy = "Admin")]
    [ProducesResponseType<AdminFundWalletResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdminFundWalletResponse>> FundByAlias(
        [FromBody] FundByAliasRequest request,
        CancellationToken ct)
    {
        if (request.Amount <= 0)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "El monto debe ser mayor a cero.",
                extensions: new Dictionary<string, object?> { ["code"] = "INVALID_AMOUNT" });
        }

        var normalizedAlias = request.Alias?.Trim().ToLowerInvariant() ?? "";
        var targetWallet = await wallets.FindByAliasAsync(normalizedAlias, ct);
        if (targetWallet is null)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: $"No se encontró ninguna billetera con el Alias '{request.Alias}'.",
                extensions: new Dictionary<string, object?> { ["code"] = "WALLET_NOT_FOUND" });
        }

        try
        {
            var result = await fundingService.FundAsync(
                new DevelopmentFundingRequest(targetWallet.Id, request.Amount, Guid.NewGuid()),
                ct);

            return Ok(new AdminFundWalletResponse(
                result.Id,
                result.WalletId,
                targetWallet.Cvu,
                targetWallet.Alias,
                result.Amount,
                result.BalanceAfter,
                result.Currency,
                result.CreatedAt));
        }
        catch (FundingRejectedException ex)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: ex.Code,
                extensions: new Dictionary<string, object?> { ["code"] = ex.Code });
        }
    }

    /// <summary>
    /// Permite al Administrador inyectar fondos en una billetera indicando su identificador GUID (WalletId).
    /// </summary>
    [HttpPost("funds/wallet-id")]
    [Authorize(Policy = "Admin")]
    [ProducesResponseType<AdminFundWalletResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdminFundWalletResponse>> FundByWalletId(
        [FromBody] FundByWalletIdRequest request,
        CancellationToken ct)
    {
        if (request.Amount <= 0)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "El monto debe ser mayor a cero.",
                extensions: new Dictionary<string, object?> { ["code"] = "INVALID_AMOUNT" });
        }

        if (request.WalletId == Guid.Empty)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "El identificador WalletId no puede ser vacío.",
                extensions: new Dictionary<string, object?> { ["code"] = "INVALID_INPUT" });
        }

        var targetWallet = await wallets.FindByIdAsync(request.WalletId, ct);
        if (targetWallet is null)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: $"No se encontró ninguna billetera con el Id '{request.WalletId}'.",
                extensions: new Dictionary<string, object?> { ["code"] = "WALLET_NOT_FOUND" });
        }

        try
        {
            var result = await fundingService.FundAsync(
                new DevelopmentFundingRequest(targetWallet.Id, request.Amount, Guid.NewGuid()),
                ct);

            return Ok(new AdminFundWalletResponse(
                result.Id,
                result.WalletId,
                targetWallet.Cvu,
                targetWallet.Alias,
                result.Amount,
                result.BalanceAfter,
                result.Currency,
                result.CreatedAt));
        }
        catch (FundingRejectedException ex)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: ex.Code,
                extensions: new Dictionary<string, object?> { ["code"] = ex.Code });
        }
    }
}
