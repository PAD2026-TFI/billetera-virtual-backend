using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Wallet.Domain;
using DomainWallet = Wallet.Domain.Wallet;

namespace Wallet.Application;

public sealed class PaymentLinkService(
    IPaymentLinkRepository links,
    IWalletRepository wallets,
    IUserRepository users,
    ITransferStore transfers,
    IDevelopmentFundingStore funding,
    CardSimulatorService cardSimulator,
    ICurrentUser currentUser,
    TimeProvider clock,
    string? baseUrl = null)
{
    private readonly string _baseUrl = baseUrl ?? "https://localhost:7081";

    public async Task<PaymentLinkDto> CreateLinkAsync(CreatePaymentLinkRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = clock.GetUtcNow();
        Guid destinationWalletId;
        Guid? merchantOwnerId = null;

        var currentUserId = TryGetCurrentUserId();
        if (currentUserId.HasValue)
        {
            merchantOwnerId = currentUserId.Value;
            var userWallet = await wallets.GetByUserIdAsync(currentUserId.Value, ct)
                ?? (await wallets.CreateOrGetAsync(DomainWallet.Create(currentUserId.Value, now), ct)).Wallet;
            destinationWalletId = userWallet.Id;
        }
        else if (request.DestinationWalletId.HasValue && request.DestinationWalletId.Value != Guid.Empty)
        {
            destinationWalletId = request.DestinationWalletId.Value;
        }
        else if (!string.IsNullOrWhiteSpace(request.MerchantEmail))
        {
            var email = request.MerchantEmail.Trim();
            var normalizedEmail = User.NormalizeEmail(email);
            var existingUser = await users.FindByEmailAsync(normalizedEmail, ct);
            if (existingUser is null)
            {
                var guestUser = User.Create("Comercio", "Invitado", email, now);
                guestUser.SetPasswordHash("GUEST_PENDING_ACTIVATION_HASH_PLACEHOLDER");
                await users.AddAsync(guestUser, ct);
                await users.SaveAsync(ct);
                var createdWallet = await wallets.CreateOrGetAsync(DomainWallet.Create(guestUser.Id, now), ct);
                destinationWalletId = createdWallet.Wallet.Id;
                merchantOwnerId = guestUser.Id;
            }
            else
            {
                var existingWallet = await wallets.GetByUserIdAsync(existingUser.Id, ct)
                    ?? (await wallets.CreateOrGetAsync(DomainWallet.Create(existingUser.Id, now), ct)).Wallet;
                destinationWalletId = existingWallet.Id;
                merchantOwnerId = existingUser.Id;
            }
        }
        else
        {
            throw new ArgumentException("Debes estar autenticado o proveer 'destinationWalletId' o 'merchantEmail' para generar el link.");
        }

        var ttl = request.TtlMinutes.HasValue ? TimeSpan.FromMinutes(request.TtlMinutes.Value) : (TimeSpan?)null;
        var monetaryAmount = Money.Positive(request.Amount);

        var link = PaymentLink.Create(
            destinationWalletId,
            monetaryAmount,
            request.Title,
            request.Description,
            request.Type,
            now,
            ttl,
            merchantOwnerId);

        await links.CreateAsync(link, ct);

        return Map(link);
    }

    public async Task<CheckoutPublicDetailsDto?> GetByTokenAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        var link = await links.FindByTokenAsync(token, ct);
        if (link is null) return null;

        var now = clock.GetUtcNow();
        var isPayable = true;
        try
        {
            link.EnsureIsPayable(now);
        }
        catch (InvalidOperationException)
        {
            isPayable = false;
        }

        return new CheckoutPublicDetailsDto(
            link.Token,
            link.Title,
            link.Description,
            link.Amount.Amount,
            link.Currency,
            link.Status.ToString(),
            link.Type.ToString(),
            link.ExpiresAt,
            isPayable);
    }

    public async Task<CheckoutReceiptDto> ProcessCheckoutAsync(
        string token,
        ProcessCheckoutRequest request,
        string? idempotencyKey,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(token))
            throw new ArgumentException("Token no puede ser nulo o vacío.", nameof(token));

        var link = await links.FindByTokenAsync(token, ct)
            ?? throw new KeyNotFoundException($"No se encontró el link de pago con token '{token}'.");

        var now = clock.GetUtcNow();
        link.EnsureIsPayable(now);

        var paymentId = Guid.TryParse(idempotencyKey, out var parsedKey) && parsedKey != Guid.Empty
            ? parsedKey
            : Guid.NewGuid();

        string? authCode;

        if (request.PaymentMethod == CheckoutPaymentMethod.Card)
        {
            if (request.Card is null)
                throw new ArgumentException("Los datos de la tarjeta son obligatorios para este método de pago.");

            var authResult = cardSimulator.Authorize(request.Card);
            authCode = authResult.AuthorizationCode;

            // Liquidación atómica en la billetera del comercio mediante financiamiento auditado
            var fundingRequest = new DevelopmentFundingRequest(link.DestinationWalletId, link.Amount.Amount, paymentId);
            var hash = FingerprintFunding(fundingRequest);
            await funding.FundAsync(fundingRequest, hash, ct);
        }
        else // CheckoutPaymentMethod.Wallet
        {
            Guid payerUserId;
            var currentUserId = TryGetCurrentUserId();
            if (currentUserId.HasValue)
            {
                payerUserId = currentUserId.Value;
            }
            else if (request.PayerWalletId.HasValue && request.PayerWalletId.Value != Guid.Empty)
            {
                payerUserId = request.PayerWalletId.Value;
            }
            else
            {
                throw new InvalidOperationException("Para pagar con billetera debés iniciar sesión o indicar el ID de billetera pagadora.");
            }

            var transferCommand = new TransferCommand(payerUserId, link.DestinationWalletId, link.Amount, paymentId);
            var hash = TransferService.Fingerprint(link.DestinationWalletId, link.Amount.Amount);
            await transfers.ExecuteAsync(transferCommand, hash, ct);
            authCode = "WALLET-PAY";
        }

        link.MarkCompleted(now);
        await links.UpdateAsync(link, ct);

        return new CheckoutReceiptDto(
            paymentId,
            link.Token,
            link.Amount.Amount,
            link.Currency,
            link.DestinationWalletId,
            request.PaymentMethod.ToString(),
            "Completed",
            now,
            authCode);
    }

    private PaymentLinkDto Map(PaymentLink link)
    {
        var checkoutUrl = $"{_baseUrl.TrimEnd('/')}/checkout/index.html?token={link.Token}";
        return new PaymentLinkDto(
            link.Id,
            link.Token,
            link.DestinationWalletId,
            link.MerchantOwnerId,
            link.Title,
            link.Description,
            link.Amount.Amount,
            link.Currency,
            link.Status.ToString(),
            link.Type.ToString(),
            link.CreatedAt,
            link.ExpiresAt,
            checkoutUrl,
            checkoutUrl);
    }

    private Guid? TryGetCurrentUserId()
    {
        try
        {
            var id = currentUser.Id;
            return id != Guid.Empty ? id : null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string FingerprintFunding(DevelopmentFundingRequest req)
    {
        var canonical = $"{req.WalletId:N}|{req.Amount.ToString("0.00", CultureInfo.InvariantCulture)}|ARS";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
