using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Wallet.Application;

namespace Wallet.Api.Payments;

public sealed class MercadoPagoSignature(PaymentSettings settings, TimeProvider clock)
{
    public bool Verify(string paymentId, string requestId, string signature)
    {
        if (!settings.Enabled || paymentId.Length is < 1 or > 64 || !paymentId.All(char.IsAsciiDigit)
            || requestId.Length is < 1 or > 128 || requestId.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')
            || signature.Length > 512) return false;
        var fields = signature.Split(',').Select(x => x.Trim().Split('=', 2)).ToArray();
        if (fields.Length != 2 || fields.Any(x => x.Length != 2)
            || fields.Count(x => x[0] == "ts") != 1 || fields.Count(x => x[0] == "v1") != 1) return false;
        var timestamp = fields.Single(x => x[0] == "ts")[1];
        var hex = fields.Single(x => x[0] == "v1")[1];
        if (timestamp.Length is not (10 or 13) || !timestamp.All(char.IsAsciiDigit)
            || !long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            || hex.Length != 64 || !hex.All(Uri.IsHexDigit)) return false;
        DateTimeOffset issued;
        try { issued = timestamp.Length == 13 ? DateTimeOffset.FromUnixTimeMilliseconds(parsed) : DateTimeOffset.FromUnixTimeSeconds(parsed); }
        catch (ArgumentOutOfRangeException) { return false; }
        if ((clock.GetUtcNow() - issued).Duration() > TimeSpan.FromMinutes(5)) return false;
        var manifest = $"id:{paymentId.ToLowerInvariant()};request-id:{requestId};ts:{timestamp};";
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(settings.WebhookSecret), Encoding.UTF8.GetBytes(manifest));
        return CryptographicOperations.FixedTimeEquals(expected, Convert.FromHexString(hex));
    }
}
