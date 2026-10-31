using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using Wallet.Domain;
using DomainWallet = Wallet.Domain.Wallet;

namespace Wallet.Application;

public sealed record RegisterExternalClientRequest(
    [Required, MaxLength(150)] string CompanyName,
    [Required, EmailAddress, MaxLength(254)] string ContactEmail,
    [MaxLength(500)] string? Description);

public sealed record RegisterExternalClientResponse(
    Guid Id,
    string CompanyName,
    string ContactEmail,
    string? Description,
    string ApiKey,
    Guid WalletId,
    DateTimeOffset CreatedAt);

public sealed class GatewayService(
    IExternalClientRepository clients,
    IWalletRepository wallets,
    TimeProvider clock)
{
    public static string HashApiKey(string apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        var bytes = Encoding.UTF8.GetBytes(apiKey.Trim());
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public async Task<RegisterExternalClientResponse> RegisterAsync(
        RegisterExternalClientRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = clock.GetUtcNow();
        var client = ExternalClient.Create(
            request.CompanyName,
            request.ContactEmail,
            now,
            request.Description);

        // Generar clave de 32 bytes aleatorios (64 caracteres hex)
        var randomBytes = new byte[32];
        RandomNumberGenerator.Fill(randomBytes);
        var rawKey = Convert.ToHexString(randomBytes).ToLowerInvariant();
        var hash = HashApiKey(rawKey);

        client.SetApiKeyHash(hash, now);

        await clients.CreateAsync(client, ct);

        // Crear billetera para el cliente externo automáticamente
        var wallet = DomainWallet.Create(client.Id, now);
        await wallets.CreateOrGetAsync(wallet, ct);

        return new RegisterExternalClientResponse(
            client.Id,
            client.CompanyName,
            client.ContactEmail,
            client.Description,
            rawKey,
            wallet.Id,
            client.CreatedAt);
    }
}
