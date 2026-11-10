using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Wallet.Domain;

namespace Wallet.Application;

public sealed record VirtualCardDetails(string CardNumber, string CardHolderName,
    int ExpirationMonth, int ExpirationYear, string Cvv);

public interface IVirtualCardRepository
{
    Task<Card?> FindByTokenAsync(string token, CancellationToken ct);
    Task<bool> ActivateAsync(Guid cardId, string previousToken, string token, CancellationToken ct);
}

public sealed class VirtualCardsUnavailableException : Exception { }

public sealed class VirtualCardCredentials
{
    public const string TokenPrefix = "padi_virtual_";
    public const string Brand = "VISA DÉBITO";

    private readonly byte[]? key;
    private readonly ConcurrentDictionary<Guid, (DateTimeOffset Until, int Count)> attempts = new();

    public VirtualCardCredentials(string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret)) return;
        key = Convert.FromBase64String(secret);
        if (key.Length < 32) throw new ArgumentException("VirtualCards:Key must have at least 32 random bytes.");
    }

    public VirtualCardDetails CreateDetails(Card card)
    {
        var id = card.Id.ToString("N");
        var sequence = BinaryPrimitives.ReadUInt32BigEndian(Sign("number", id)) % 100_000_000;
        var prefix = "999" + sequence.ToString("D8", CultureInfo.InvariantCulture);
        var number = Enumerable.Range(0, 10).Select(digit => prefix + digit + card.CardNumberLast4)
            .Single(CardSimulatorService.ValidateLuhn);
        return new(number, card.CardHolderName, card.ExpirationMonth, card.ExpirationYear, GetCvv(card.Id));
    }

    public string GetToken(string number) => TokenPrefix + Convert.ToHexString(Sign("lookup", number));

    public bool MatchesCvv(Guid cardId, string cvv) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(cvv), Encoding.ASCII.GetBytes(GetCvv(cardId)));

    public bool AllowAttempt(Guid cardId, DateTimeOffset now) =>
        attempts.AddOrUpdate(cardId, (now.AddMinutes(10), 1), (_, previous) =>
            now >= previous.Until
                ? (now.AddMinutes(10), 1)
                : (previous.Until, Math.Min(previous.Count + 1, 6))).Count <= 5;

    private byte[] Sign(string purpose, string value) =>
        HMACSHA256.HashData(key ?? throw new VirtualCardsUnavailableException(),
            Encoding.ASCII.GetBytes($"padi-virtual-v1:{purpose}:{value}"));

    private string GetCvv(Guid cardId) =>
        (BinaryPrimitives.ReadUInt32BigEndian(Sign("cvv", cardId.ToString("N"))) % 1000)
            .ToString("D3", CultureInfo.InvariantCulture);
}

public sealed class VirtualCardService(ICardRepository cards, IVirtualCardRepository virtualCards,
    VirtualCardCredentials credentials, IPaymentLinkRepository links, IDebitCardPaymentStore debitPayments,
    TimeProvider clock)
{
    public async Task<VirtualCardDetails?> GetDetailsAsync(Guid userId, Guid cardId, CancellationToken ct)
    {
        var card = await cards.FindByIdAsync(cardId, ct);
        if (card is null || card.UserId != userId || card.Type != CardType.Debit ||
            !card.Brand.Equals(VirtualCardCredentials.Brand, StringComparison.OrdinalIgnoreCase) ||
            !card.IsActive || card.IsExpired(clock.GetUtcNow()))
            return null;

        var details = credentials.CreateDetails(card);
        var token = credentials.GetToken(details.CardNumber);
        if (card.CardToken == token)
            return details;

        // Cards issued before this feature had a local placeholder token and a display-only number.
        if (!card.CardToken.StartsWith("tok_", StringComparison.Ordinal))
            return null;

        if (await virtualCards.ActivateAsync(card.Id, card.CardToken, token, ct))
            return details;

        var activated = await virtualCards.FindByTokenAsync(token, ct);
        return activated?.Id == card.Id ? details : null;
    }

    public async Task<CheckoutReceiptDto> PayAsync(string? linkToken, CardPaymentDetails? input,
        string? idempotencyKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(linkToken) || string.IsNullOrWhiteSpace(idempotencyKey) ||
            idempotencyKey.Trim().Length > 128)
            throw new ArgumentException("El token del link y una clave de idempotencia válida son obligatorios.");

        var number = input?.CardNumber?.Replace(" ", "", StringComparison.Ordinal);
        if (input is null || number is null || !number.StartsWith("999", StringComparison.Ordinal) ||
            !CardSimulatorService.ValidateLuhn(number) ||
            input.Cvv is null || input.Cvv.Length != 3 || !input.Cvv.All(char.IsAsciiDigit))
            throw Declined();

        var token = credentials.GetToken(number);
        var card = await virtualCards.FindByTokenAsync(token, ct);
        var now = clock.GetUtcNow();
        if (card is null || !credentials.AllowAttempt(card.Id, now) ||
            card.Type != CardType.Debit || !card.IsActive || card.IsExpired(now) ||
            input.ExpirationMonth != card.ExpirationMonth || input.ExpirationYear != card.ExpirationYear ||
            !credentials.MatchesCvv(card.Id, input.Cvv))
            throw Declined();

        var link = await links.FindByTokenAsync(linkToken, ct)
            ?? throw new KeyNotFoundException("Link de pago no encontrado.");
        return await debitPayments.ExecuteAsync(
            new(link.Id, card.Id, card.UserId, idempotencyKey.Trim(), token), ct);
    }

    private static CardPaymentRejectedException Declined() =>
        new("PADI_CARD_DECLINED", "No pudimos autorizar la tarjeta virtual de Padi. Revisá sus datos.");
}
