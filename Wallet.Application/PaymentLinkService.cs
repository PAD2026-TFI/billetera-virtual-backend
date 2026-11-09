using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Wallet.Domain;
using DomainWallet = Wallet.Domain.Wallet;

namespace Wallet.Application;

public sealed class PaymentLinkService(
    IPaymentLinkRepository links,
    IPaymentRepository payments,
    IWalletRepository wallets,
    IUserRepository users,
    ITransferStore transfers,
    IDevelopmentFundingStore funding,
    CardSimulatorService cardSimulator,
    ICurrentUser currentUser,
    TimeProvider clock,
    ICardRepository? cards = null,
    string? baseUrl = null,
    IDebitCardPaymentStore? debitCardPayments = null) : ICheckoutService
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
        else if (!string.IsNullOrWhiteSpace(request.DestinationCvu))
        {
            var target = await wallets.FindByCvuAsync(request.DestinationCvu.Trim(), ct)
                ?? throw new ArgumentException($"No se encontró ninguna billetera con el CVU '{request.DestinationCvu}'.");
            destinationWalletId = target.Id;
            merchantOwnerId = target.OwnerId;
        }
        else if (!string.IsNullOrWhiteSpace(request.DestinationAlias))
        {
            var target = await wallets.FindByAliasAsync(request.DestinationAlias.Trim().ToLowerInvariant(), ct)
                ?? throw new ArgumentException($"No se encontró ninguna billetera con el Alias '{request.DestinationAlias}'.");
            destinationWalletId = target.Id;
            merchantOwnerId = target.OwnerId;
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
        else if (request.DestinationWalletId.HasValue && request.DestinationWalletId.Value != Guid.Empty)
        {
            destinationWalletId = request.DestinationWalletId.Value;
        }
        else
        {
            throw new ArgumentException("Debes estar autenticado con X-Api-Key / Bearer, o indicar 'merchantEmail', 'destinationCvu', 'destinationAlias' o 'destinationWalletId'.");
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

    public async Task<IReadOnlyList<PaymentLinkDto>> ListByMerchantAsync(Guid merchantOwnerId, CancellationToken ct)
    {
        var list = await links.ListByMerchantAsync(merchantOwnerId, ct);
        return list.Select(Map).ToList();
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
            isPayable,
            link.Id,
            link.MerchantOwnerId,
            link.DestinationWalletId);
    }

    public async Task<PaymentLinkDto?> GetByIdAsync(Guid id, Guid merchantOwnerId, CancellationToken ct)
    {
        var link = await links.FindByIdAsync(id, ct);
        if (link is null || link.MerchantOwnerId != merchantOwnerId)
            return null;

        return Map(link);
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

        if (!Enum.IsDefined(request.PaymentMethod))
            throw new ArgumentException("Unknown payment method.", nameof(request));
        if (request.CardId.HasValue)
        {
            if (request.PaymentMethod != CheckoutPaymentMethod.Card || request.Card is not null)
                throw new ArgumentException("Use CardId alone with paymentMethod Card for an associated card.");
            var payerId = TryGetCurrentUserId() ?? throw new UnauthorizedAccessException("Login required for an associated card.");
            return await PayWithSavedCardAsync(link.Id, new PayWithSavedCardRequest { CardId = request.CardId.Value }, payerId, idempotencyKey, ct);
        }
        var now = clock.GetUtcNow();
        link.EnsureIsPayable(now);

        var currentUserId = TryGetCurrentUserId();
        if (currentUserId.HasValue)
        {
            if (link.MerchantOwnerId.HasValue && link.MerchantOwnerId.Value == currentUserId.Value)
            {
                throw new TransferRejectedException("SAME_WALLET");
            }
            var checkWallet = await wallets.GetByUserIdAsync(currentUserId.Value, ct);
            if (checkWallet is not null && checkWallet.Id == link.DestinationWalletId)
            {
                throw new TransferRejectedException("SAME_WALLET");
            }
        }
        var normalizedIdempotencyKey = !string.IsNullOrWhiteSpace(idempotencyKey) ? idempotencyKey.Trim() : null;
        var requestHash = FingerprintPayment(link.Id, link.Amount.Amount, request.PaymentMethod, request.Card?.CardNumber, currentUserId);

        // 1. Verificación de idempotencia
        if (normalizedIdempotencyKey is not null && link.MerchantOwnerId.HasValue)
        {
            var existingPayment = await payments.FindByIdempotencyKeyAsync(link.MerchantOwnerId.Value, normalizedIdempotencyKey, ct);
            if (existingPayment is not null)
            {
                if (existingPayment.RequestHash == requestHash)
                {
                    return new CheckoutReceiptDto(
                        existingPayment.Id,
                        link.Token,
                        existingPayment.Amount.Amount,
                        existingPayment.Currency,
                        existingPayment.DestinationWalletId,
                        existingPayment.Method.ToString(),
                        existingPayment.Status == PaymentStatus.Succeeded ? "Completed" : existingPayment.Status.ToString(),
                        existingPayment.CompletedAt ?? existingPayment.CreatedAt,
                        existingPayment.AuthorizationCode,
                        IdempotencyReplayed: true);
                }

                throw new TransferRejectedException("IDEMPOTENCY_CONFLICT");
            }
        }

        // 2. Control de concurrencia y exclusividad en links de un solo uso (SingleUse)
        if (link.Type == PaymentLinkType.SingleUse)
        {
            var priorPayments = await payments.GetByPaymentLinkIdAsync(link.Id, ct);
            if (priorPayments.Any(p => p.Status == PaymentStatus.Succeeded || p.Status == PaymentStatus.Processing))
            {
                throw new InvalidOperationException("This payment link has already been completed.");
            }
        }

        Payment payment;

        if (request.PaymentMethod == CheckoutPaymentMethod.Card)
        {
            if (request.Card is null)
                throw new ArgumentException("Los datos de la tarjeta son obligatorios para este método de pago.");

            var cleanNumber = new string(request.Card.CardNumber?.Where(char.IsDigit).ToArray() ?? []);
            var last4 = cleanNumber.Length >= 4 ? cleanNumber[^4..] : "0000";
            var brand = cleanNumber.StartsWith("4", StringComparison.Ordinal) ? "Visa" : "Mastercard";

            payment = Payment.CreateForCard(
                link.Id,
                link.MerchantOwnerId ?? Guid.Empty,
                link.DestinationWalletId,
                link.Amount,
                brand,
                last4,
                request.Card.CardHolderName,
                currentUserId,
                normalizedIdempotencyKey,
                requestHash,
                now);

            CardAuthorizationResult authResult;
            try
            {
                authResult = cardSimulator.Authorize(request.Card);
            }
            catch (CardPaymentRejectedException ex)
            {
                payment.MarkFailed(ex.Code, now);
                await payments.CreateAsync(payment, ct);
                throw;
            }

            if (currentUserId.HasValue)
            {
                var payerWallet = await wallets.GetByUserIdAsync(currentUserId.Value, ct);
                if (payerWallet is not null)
                {
                    if (payerWallet.Id == link.DestinationWalletId)
                        throw new TransferRejectedException("SAME_WALLET");

                    var transferCommand = new TransferCommand(currentUserId.Value, link.DestinationWalletId, link.Amount, payment.Id);
                    var transferHash = TransferService.Fingerprint(link.DestinationWalletId, link.Amount.Amount);
                    var transferResult = await transfers.ExecuteAsync(transferCommand, transferHash, ct);
                    payment.MarkSucceeded(transferResult.Transfer.Id, authResult.AuthorizationCode, now);
                }
                else
                {
                    var fundingRequest = new DevelopmentFundingRequest(link.DestinationWalletId, link.Amount.Amount, payment.Id);
                    var hash = FingerprintFunding(fundingRequest);
                    await funding.FundAsync(fundingRequest, hash, ct);
                    payment.MarkSucceeded(null, authResult.AuthorizationCode, now);
                }
            }
            else
            {
                var fundingRequest = new DevelopmentFundingRequest(link.DestinationWalletId, link.Amount.Amount, payment.Id);
                var hash = FingerprintFunding(fundingRequest);
                await funding.FundAsync(fundingRequest, hash, ct);
                payment.MarkSucceeded(null, authResult.AuthorizationCode, now);
            }
        }
        else // CheckoutPaymentMethod.Wallet
        {
            Guid payerUserId;
            if (currentUserId.HasValue)
            {
                payerUserId = currentUserId.Value;
            }
            else
            {
                throw new InvalidOperationException("Para pagar con billetera debés iniciar sesión.");
            }

            var payerWallet = await wallets.GetByUserIdAsync(payerUserId, ct)
                ?? throw new TransferRejectedException("SOURCE_WALLET_NOT_FOUND");

            if (payerWallet.Id == link.DestinationWalletId)
            {
                throw new TransferRejectedException("SAME_WALLET");
            }

            payment = Payment.CreateForWallet(
                link.Id,
                link.MerchantOwnerId ?? Guid.Empty,
                link.DestinationWalletId,
                payerUserId,
                payerWallet.Id,
                link.Amount,
                normalizedIdempotencyKey,
                requestHash,
                now);

            var transferCommand = new TransferCommand(payerUserId, link.DestinationWalletId, link.Amount, payment.Id);
            var hash = TransferService.Fingerprint(link.DestinationWalletId, link.Amount.Amount);
            var transferResult = await transfers.ExecuteAsync(transferCommand, hash, ct);

            payment.MarkSucceeded(transferResult.Transfer.Id, "WALLET-PAY", now);
        }

        link.MarkCompleted(now);
        await links.UpdateAsync(link, ct);
        await payments.CreateAsync(payment, ct);

        return new CheckoutReceiptDto(
            payment.Id,
            link.Token,
            payment.Amount.Amount,
            payment.Currency,
            payment.DestinationWalletId,
            payment.Method.ToString(),
            payment.Status == PaymentStatus.Succeeded ? "Completed" : payment.Status.ToString(),
            payment.CompletedAt ?? now,
            payment.AuthorizationCode,
            IdempotencyReplayed: false);
    }

    public async Task<CheckoutReceiptDto> PayWithSavedCardAsync(
        Guid paymentLinkId,
        PayWithSavedCardRequest request,
        Guid userId,
        string? idempotencyKey,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (paymentLinkId == Guid.Empty)
            throw new ArgumentException("Payment intent identifier cannot be empty.", nameof(paymentLinkId));

        if (userId == Guid.Empty)
            throw new ArgumentException("User identifier cannot be empty.", nameof(userId));

        var link = await links.FindByIdAsync(paymentLinkId, ct)
            ?? throw new KeyNotFoundException($"Payment intent '{paymentLinkId}' not found.");

        var now = clock.GetUtcNow();

        if (cards is null)
            throw new InvalidOperationException("Card repository is not configured.");

        var card = await cards.FindByIdAsync(request.CardId, ct);
        if (card is null)
            throw new KeyNotFoundException($"Saved card '{request.CardId}' was not found.");

        if (card.UserId != userId)
            throw new UnauthorizedAccessException("The specified card does not belong to the authenticated user.");

        if (!card.IsActive)
            throw new InvalidOperationException("The specified card is inactive.");

        if (card.IsExpired(now))
            throw new InvalidOperationException("The specified card is expired.");

        if (card.Type == CardType.Debit)
        {
            var key = idempotencyKey?.Trim();
            if (string.IsNullOrEmpty(key) || key.Length > 128)
                throw new ArgumentException("Idempotency-Key is required and must not exceed 128 characters.", nameof(idempotencyKey));
            if (debitCardPayments is null)
                throw new InvalidOperationException("Debit card payment store is not configured.");
            return await debitCardPayments.ExecuteAsync(new(link.Id, card.Id, userId, key), ct);
        }
        var normalizedIdempotencyKey = !string.IsNullOrWhiteSpace(idempotencyKey) ? idempotencyKey.Trim() : null;
        var requestHash = FingerprintPayment(link.Id, link.Amount.Amount, CheckoutPaymentMethod.Card, card.CardNumberLast4, userId);

        if (normalizedIdempotencyKey is not null && link.MerchantOwnerId.HasValue)
        {
            var existingPayment = await payments.FindByIdempotencyKeyAsync(link.MerchantOwnerId.Value, normalizedIdempotencyKey, ct);
            if (existingPayment is not null)
            {
                if (existingPayment.RequestHash == requestHash)
                {
                    return new CheckoutReceiptDto(
                        existingPayment.Id,
                        link.Token,
                        existingPayment.Amount.Amount,
                        existingPayment.Currency,
                        existingPayment.DestinationWalletId,
                        existingPayment.Method.ToString(),
                        existingPayment.Status == PaymentStatus.Succeeded ? "Completed" : existingPayment.Status.ToString(),
                        existingPayment.CompletedAt ?? existingPayment.CreatedAt,
                        existingPayment.AuthorizationCode,
                        IdempotencyReplayed: true);
                }

                throw new TransferRejectedException("IDEMPOTENCY_CONFLICT");
            }
        }

        link.EnsureIsPayable(now);

        if (link.Type == PaymentLinkType.SingleUse)
        {
            var priorPayments = await payments.GetByPaymentLinkIdAsync(link.Id, ct);
            if (priorPayments.Any(p => p.Status == PaymentStatus.Succeeded || p.Status == PaymentStatus.Processing))
            {
                throw new InvalidOperationException("This payment link has already been completed.");
            }
        }

        var fullNumber = card.Brand.Equals("Visa", StringComparison.OrdinalIgnoreCase)
            ? $"453201511283{card.CardNumberLast4}"
            : $"555555555555{card.CardNumberLast4}";

        var cardDetails = new CardPaymentDetails(
            fullNumber,
            card.CardHolderName,
            card.ExpirationMonth,
            card.ExpirationYear,
            !string.IsNullOrWhiteSpace(request.Cvv) ? request.Cvv : "123");

        var payment = Payment.CreateForCard(
            link.Id,
            link.MerchantOwnerId ?? Guid.Empty,
            link.DestinationWalletId,
            link.Amount,
            card.Brand,
            card.CardNumberLast4,
            card.CardHolderName,
            userId,
            normalizedIdempotencyKey,
            requestHash,
            now);

        CardAuthorizationResult authResult;
        try
        {
            authResult = cardSimulator.Authorize(cardDetails);
        }
        catch (CardPaymentRejectedException ex)
        {
            payment.MarkFailed(ex.Code, now);
            await payments.CreateAsync(payment, ct);
            throw;
        }

        var fundingRequest = new DevelopmentFundingRequest(link.DestinationWalletId, link.Amount.Amount, payment.Id);
        var hash = FingerprintFunding(fundingRequest);
        await funding.FundAsync(fundingRequest, hash, ct);

        payment.MarkSucceeded(null, authResult.AuthorizationCode, now);

        link.MarkCompleted(now);
        await links.UpdateAsync(link, ct);
        await payments.CreateAsync(payment, ct);

        return new CheckoutReceiptDto(
            payment.Id,
            link.Token,
            payment.Amount.Amount,
            payment.Currency,
            payment.DestinationWalletId,
            payment.Method.ToString(),
            payment.Status == PaymentStatus.Succeeded ? "Completed" : payment.Status.ToString(),
            payment.CompletedAt ?? now,
            payment.AuthorizationCode,
            IdempotencyReplayed: false);
    }

    public async Task<MerchantPaymentDto?> GetPaymentByIdAsync(Guid paymentId, Guid merchantOwnerId, CancellationToken ct)
    {
        var payment = await payments.FindByIdAsync(paymentId, ct);
        if (payment is null || payment.MerchantOwnerId != merchantOwnerId)
            return null;

        return MapToMerchantPayment(payment);
    }

    public async Task<IReadOnlyList<MerchantPaymentDto>> GetPaymentsByMerchantAsync(Guid merchantOwnerId, int limit, CancellationToken ct)
    {
        var list = await payments.GetByMerchantOwnerIdAsync(merchantOwnerId, limit, ct);
        return list.Select(MapToMerchantPayment).ToList();
    }

    private static MerchantPaymentDto MapToMerchantPayment(Payment p) =>
        new(p.Id,
            p.PaymentLinkId,
            p.DestinationWalletId,
            p.Amount.Amount,
            p.Currency,
            p.Method.ToString(),
            p.Status.ToString(),
            p.TransferId,
            p.CardBrand,
            p.CardLast4,
            p.CardHolderName,
            p.AuthorizationCode,
            p.FailureReason,
            p.CreatedAt,
            p.CompletedAt);

    private PaymentLinkDto Map(PaymentLink link)
    {
        var checkoutUrl = $"{_baseUrl.TrimEnd('/')}/checkout?token={link.Token}";
        var qrCodeUrl = $"{_baseUrl.TrimEnd('/')}/api/v1/public/payment-links/{link.Token}/qr";
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
            checkoutUrl,
            qrCodeUrl);
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

    private static string FingerprintPayment(Guid linkId, decimal amount, CheckoutPaymentMethod method, string? cardNumber, Guid? payerId)
    {
        var last4 = cardNumber is not null && cardNumber.Length >= 4 ? cardNumber[^4..] : "NONE";
        var payer = payerId.HasValue ? payerId.Value.ToString("N") : "ANONYMOUS";
        var canonical = $"{linkId:N}|{amount.ToString("0.00", CultureInfo.InvariantCulture)}|{method}|{last4}|{payer}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
