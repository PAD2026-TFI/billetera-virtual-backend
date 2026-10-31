using System.Globalization;
using System.Security.Cryptography;

namespace Wallet.Application;

public sealed record CardPaymentDetails(
    string CardNumber,
    string CardHolderName,
    int ExpirationMonth,
    int ExpirationYear,
    string Cvv);

public sealed record CardAuthorizationResult(
    bool Approved,
    string Brand,
    string LastFour,
    string AuthorizationCode);

public sealed class CardPaymentRejectedException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class CardSimulatorService(TimeProvider clock)
{
    public CardAuthorizationResult Authorize(CardPaymentDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);

        var cleanNumber = new string(details.CardNumber?.Where(char.IsDigit).ToArray() ?? []);
        if (string.IsNullOrWhiteSpace(cleanNumber) || !ValidateLuhn(cleanNumber))
            throw new CardPaymentRejectedException("INVALID_CARD_NUMBER", "El número de tarjeta es inválido (falló validación de Luhn).");

        if (string.IsNullOrWhiteSpace(details.CardHolderName))
            throw new CardPaymentRejectedException("INVALID_CARDHOLDER", "El nombre del titular es requerido.");

        ValidateExpiration(details.ExpirationMonth, details.ExpirationYear);

        var cleanCvv = details.Cvv?.Trim() ?? "";
        if (cleanCvv.Length is < 3 or > 4 || !cleanCvv.All(char.IsDigit))
            throw new CardPaymentRejectedException("INVALID_CVV", "El código de seguridad (CVV) debe contener 3 o 4 dígitos.");

        // Reglas de simulación financiera
        if (cleanNumber.EndsWith("0000", StringComparison.Ordinal) || cleanCvv == "999")
            throw new CardPaymentRejectedException("INSUFFICIENT_FUNDS", "Fondos insuficientes en la tarjeta emisora de prueba.");

        if (cleanNumber.EndsWith("9999", StringComparison.Ordinal))
            throw new CardPaymentRejectedException("CARD_BLOCKED", "Tarjeta simulada bloqueada por la entidad emisora.");

        if (cleanNumber.EndsWith("1111", StringComparison.Ordinal))
            throw new CardPaymentRejectedException("SUSPECTED_FRAUD", "Operación denegada por sospecha de fraude.");

        var brand = DetectBrand(cleanNumber);
        var lastFour = cleanNumber[^4..];
        var authCode = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToUpperInvariant();

        return new CardAuthorizationResult(true, brand, lastFour, authCode);
    }

    public static bool ValidateLuhn(string number)
    {
        if (string.IsNullOrWhiteSpace(number)) return false;
        var digits = number.Where(char.IsDigit).Select(c => c - '0').ToArray();
        if (digits.Length is < 13 or > 19) return false;

        var sum = 0;
        var shouldDouble = false;
        for (var i = digits.Length - 1; i >= 0; i--)
        {
            var digit = digits[i];
            if (shouldDouble)
            {
                digit *= 2;
                if (digit > 9) digit -= 9;
            }
            sum += digit;
            shouldDouble = !shouldDouble;
        }
        return sum % 10 == 0;
    }

    private void ValidateExpiration(int month, int year)
    {
        if (month is < 1 or > 12)
            throw new CardPaymentRejectedException("INVALID_EXPIRATION_MONTH", "El mes de vencimiento debe estar entre 1 y 12.");

        var fullYear = year < 100 ? 2000 + year : year;
        var now = clock.GetUtcNow();

        if (fullYear < now.Year || (fullYear == now.Year && month < now.Month))
            throw new CardPaymentRejectedException("CARD_EXPIRED", "La tarjeta se encuentra vencida.");
    }

    private static string DetectBrand(string number)
    {
        if (number.StartsWith("4", StringComparison.Ordinal)) return "Visa";
        if (number.StartsWith("51", StringComparison.Ordinal) ||
            number.StartsWith("52", StringComparison.Ordinal) ||
            number.StartsWith("53", StringComparison.Ordinal) ||
            number.StartsWith("54", StringComparison.Ordinal) ||
            number.StartsWith("55", StringComparison.Ordinal)) return "Mastercard";
        if (number.StartsWith("34", StringComparison.Ordinal) ||
            number.StartsWith("37", StringComparison.Ordinal)) return "American Express";
        if (number.StartsWith("5896", StringComparison.Ordinal) ||
            number.StartsWith("6042", StringComparison.Ordinal)) return "Cabal";

        return "CreditCard";
    }
}
