namespace Wallet.Api.Security;
public sealed class JwtSettings
{
    public string Issuer { get; init; } = "Wallet.Api";
    public string Audience { get; init; } = "Wallet.Client";
    public string Key { get; init; } = ""; // Base64 de al menos 32 bytes aleatorios.
    public int LifetimeMinutes { get; init; } = 15;
    public byte[] GetKey()
    {
        var bytes = Convert.FromBase64String(Key);
        if (bytes.Length < 32 || string.IsNullOrWhiteSpace(Issuer)
            || string.IsNullOrWhiteSpace(Audience) || LifetimeMinutes is < 1 or > 60)
            throw new InvalidOperationException("Configuración JWT inválida.");
        return bytes;
    }
}
