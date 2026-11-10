using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using Wallet.Domain;
using DomainWallet = Wallet.Domain.Wallet;

namespace Wallet.Application;

public sealed record RegisterExternalClientRequest(
    [Required, MaxLength(150)] string CompanyName,
    [Required, EmailAddress, MaxLength(254)] string ContactEmail,
    [MaxLength(500)] string? Description,
    string? Cuit = null);

public sealed record RegisterExternalClientResponse(
    Guid Id,
    string CompanyName,
    string ContactEmail,
    string? Description,
    string ApiKey,
    Guid WalletId,
    DateTimeOffset CreatedAt,
    string Cuit = "",
    string? WebhookUrl = null);

public sealed record ConfigureWebhookRequest(
    [Required, MaxLength(2048)] string WebhookUrl,
    [Required, MinLength(32), MaxLength(256)] string WebhookSecret);

public sealed record ExternalClientDetailsResponse(
    Guid Id,
    string CompanyName,
    string Cuit,
    string ContactEmail,
    string? Description,
    string? WebhookUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastRotatedAt);

public sealed record SetMerchantPasswordRequest(
    [Required, StringLength(128, MinimumLength = 8)] string Password,
    [EmailAddress, StringLength(254)] string? Email = null,
    [StringLength(100)] string? FirstName = null,
    [StringLength(100)] string? LastName = null);

public sealed record SetMerchantPasswordResponse(
    Guid UserId,
    string Email,
    string FirstName,
    string LastName,
    string AccountType,
    Guid WalletId,
    string Message);

public sealed class GatewayService(
    IExternalClientRepository clients,
    IWalletRepository wallets,
    TimeProvider clock,
    IUserRepository? users = null,
    IPasswordService? passwords = null)
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
        var effectiveCuit = string.IsNullOrWhiteSpace(request.Cuit) ? "00000000000" : request.Cuit.Trim();

        var client = ExternalClient.Create(
            request.CompanyName,
            effectiveCuit,
            request.ContactEmail,
            now,
            request.Description);

        var randomBytes = new byte[32];
        RandomNumberGenerator.Fill(randomBytes);
        var rawKey = Convert.ToHexString(randomBytes).ToLowerInvariant();
        var hash = HashApiKey(rawKey);

        client.SetApiKeyHash(hash, now);

        await clients.CreateAsync(client, ct);

        var wallet = DomainWallet.Create(client.Id, now);
        await wallets.CreateOrGetAsync(wallet, ct);

        return new RegisterExternalClientResponse(
            client.Id,
            client.CompanyName,
            client.ContactEmail,
            client.Description,
            rawKey,
            wallet.Id,
            client.CreatedAt,
            client.Cuit,
            client.WebhookUrl);
    }

    public async Task<ExternalClientDetailsResponse> ConfigureWebhookAsync(
        Guid clientId,
        ConfigureWebhookRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var client = await clients.FindByIdAsync(clientId, ct)
            ?? throw new KeyNotFoundException($"External client '{clientId}' was not found.");

        client.ConfigureWebhook(request.WebhookUrl, request.WebhookSecret);
        await clients.UpdateAsync(client, ct);

        return new ExternalClientDetailsResponse(
            client.Id,
            client.CompanyName,
            client.Cuit,
            client.ContactEmail,
            client.Description,
            client.WebhookUrl,
            client.CreatedAt,
            client.LastRotatedAt);
    }

    public async Task<ExternalClientDetailsResponse> DeactivateWebhookAsync(
        Guid clientId,
        CancellationToken ct)
    {
        var client = await clients.FindByIdAsync(clientId, ct)
            ?? throw new KeyNotFoundException($"External client '{clientId}' was not found.");

        client.DeactivateWebhook();
        await clients.UpdateAsync(client, ct);

        return new ExternalClientDetailsResponse(
            client.Id,
            client.CompanyName,
            client.Cuit,
            client.ContactEmail,
            client.Description,
            client.WebhookUrl,
            client.CreatedAt,
            client.LastRotatedAt);
    }

    public async Task<ExternalClientDetailsResponse?> GetClientByIdAsync(
        Guid clientId,
        CancellationToken ct)
    {
        var client = await clients.FindByIdAsync(clientId, ct);
        if (client is null) return null;

        return new ExternalClientDetailsResponse(
            client.Id,
            client.CompanyName,
            client.Cuit,
            client.ContactEmail,
            client.Description,
            client.WebhookUrl,
            client.CreatedAt,
            client.LastRotatedAt);
    }

    public async Task<bool> TestWebhookAsync(
        Guid clientId,
        IWebhookDispatcher dispatcher,
        CancellationToken ct)
    {
        var client = await clients.FindByIdAsync(clientId, ct)
            ?? throw new KeyNotFoundException($"External client '{clientId}' was not found.");

        if (string.IsNullOrWhiteSpace(client.WebhookUrl))
            throw new InvalidOperationException("Webhook URL is not configured.");

        var now = clock.GetUtcNow();
        var notification = new WebhookNotification(
            "webhook.test",
            new WebhookPaymentData(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "test-link-token",
                1000.00m,
                "ARS",
                "Completed",
                "Simulator",
                "TEST-AUTH-OK",
                Guid.NewGuid(),
                now),
            now);

        await dispatcher.DispatchPaymentSucceededAsync(
            client.WebhookUrl,
            client.WebhookSecret,
            notification,
            ct);

        return true;
    }

    public async Task<SetMerchantPasswordResponse> SetPasswordAsync(
        Guid clientId,
        SetMerchantPasswordRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
            throw new ArgumentException("La contraseña debe tener al menos 8 caracteres.", nameof(request));

        if (users is null || passwords is null)
            throw new InvalidOperationException("User repository or password service is not configured.");

        var client = await clients.FindByIdAsync(clientId, ct)
            ?? throw new KeyNotFoundException($"External client '{clientId}' was not found.");

        var wallet = await wallets.GetByUserIdAsync(clientId, ct)
            ?? throw new InvalidOperationException("External client does not have a wallet.");

        var email = string.IsNullOrWhiteSpace(request.Email)
            ? client.ContactEmail
            : request.Email.Trim();

        var normalizedEmail = User.NormalizeEmail(email);
        var existingUser = await users.FindByEmailAsync(normalizedEmail, ct);
        var now = clock.GetUtcNow();

        User user;
        if (existingUser is not null)
        {
            if (existingUser.WalletId == wallet.Id)
            {
                existingUser.SetPasswordHash(passwords.Hash(existingUser, request.Password));
                await users.SaveAsync(ct);
                user = existingUser;
            }
            else
            {
                throw new InvalidOperationException("El correo ya se encuentra registrado para otra billetera.");
            }
        }
        else
        {
            var firstName = !string.IsNullOrWhiteSpace(request.FirstName)
                ? request.FirstName.Trim()
                : (client.CompanyName.Length > 100 ? client.CompanyName[..100] : client.CompanyName);

            var lastName = !string.IsNullOrWhiteSpace(request.LastName)
                ? request.LastName.Trim()
                : "Comercio";

            var dni = !string.IsNullOrWhiteSpace(client.Cuit) && client.Cuit.Length >= 10
                ? client.Cuit[2..10]
                : "00000000";

            user = User.RegisterClient(dni, firstName, lastName, email, now);
            user.SetAccountType("merchant");
            user.SetPasswordHash(passwords.Hash(user, request.Password));
            user.LinkWallet(wallet.Id);

            await users.AddAsync(user, ct);
        }

        return new SetMerchantPasswordResponse(
            user.Id,
            user.Email,
            user.FirstName,
            user.LastName,
            user.AccountType,
            wallet.Id,
            "Credenciales creadas exitosamente. Ya podés ingresar a la aplicación web y móvil con tu correo y contraseña.");
    }
}
