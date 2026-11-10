using System.Collections.Concurrent;
using Wallet.Application;
using Wallet.Domain;
using Xunit;
using DomainWallet = Wallet.Domain.Wallet;

namespace Wallet.Tests;

public sealed class GatewayServiceTests
{
    private sealed class InMemoryExternalClientRepository : IExternalClientRepository
    {
        public readonly ConcurrentDictionary<Guid, ExternalClient> ClientsById = new();
        public readonly ConcurrentDictionary<string, ExternalClient> ClientsByHash = new();

        public Task<ExternalClient?> FindByApiKeyHashAsync(string apiKeyHash, CancellationToken ct)
        {
            ClientsByHash.TryGetValue(apiKeyHash, out var client);
            return Task.FromResult(client);
        }

        public Task<ExternalClient?> FindByIdAsync(Guid id, CancellationToken ct)
        {
            ClientsById.TryGetValue(id, out var client);
            return Task.FromResult(client);
        }

        public Task CreateAsync(ExternalClient client, CancellationToken ct)
        {
            ClientsById[client.Id] = client;
            if (!string.IsNullOrWhiteSpace(client.ApiKeyHash))
            {
                ClientsByHash[client.ApiKeyHash] = client;
            }
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void HashApiKey_produces_64_character_hex_sha256()
    {
        var rawKey = "test_api_key_1234567890abcdef";
        var hash = GatewayService.HashApiKey(rawKey);

        Assert.NotNull(hash);
        Assert.Equal(64, hash.Length);
        Assert.True(hash.All(Uri.IsHexDigit));
    }

    [Fact]
    public async Task RegisterAsync_creates_external_client_and_initial_wallet_with_valid_api_key()
    {
        var clientRepo = new InMemoryExternalClientRepository();
        var walletRepo = new TestWalletRepository();
        var service = new GatewayService(clientRepo, walletRepo, TimeProvider.System);

        var request = new RegisterExternalClientRequest(
            CompanyName: "Tienda B2B S.A.",
            ContactEmail: "integraciones@tiendab2b.com",
            Description: "E-Commerce checkout gateway");

        var response = await service.RegisterAsync(request, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, response.Id);
        Assert.Equal("Tienda B2B S.A.", response.CompanyName);
        Assert.Equal("integraciones@tiendab2b.com", response.ContactEmail);
        Assert.Equal("E-Commerce checkout gateway", response.Description);
        Assert.NotNull(response.ApiKey);
        Assert.Equal(64, response.ApiKey.Length);
        Assert.NotEqual(Guid.Empty, response.WalletId);

        // Verificar que el hash se almacenó correctamente en el repositorio
        var hash = GatewayService.HashApiKey(response.ApiKey);
        var stored = await clientRepo.FindByApiKeyHashAsync(hash, CancellationToken.None);
        Assert.NotNull(stored);
        Assert.Equal(response.Id, stored.Id);
        Assert.True(stored.IsActive);

        // Verificar que la billetera del cliente fue creada
        var storedWallet = await walletRepo.GetByUserIdAsync(response.Id, CancellationToken.None);
        Assert.NotNull(storedWallet);
        Assert.Equal(response.WalletId, storedWallet.Id);
        Assert.Equal(Money.Zero("ARS"), storedWallet.Balance);
    }

    [Fact]
    public async Task SetPasswordAsync_creates_user_linked_to_existing_merchant_wallet()
    {
        var clientRepo = new InMemoryExternalClientRepository();
        var walletRepo = new TestWalletRepository();
        var userRepo = new TestUserRepository();
        var passwords = new FakePasswordService();
        var service = new GatewayService(clientRepo, walletRepo, TimeProvider.System, userRepo, passwords);

        var reg = await service.RegisterAsync(new RegisterExternalClientRequest("Gaming Corp", "gaming@corp.com", "Description"), CancellationToken.None);

        var setPasswordReq = new SetMerchantPasswordRequest("SecurePass123!", "gaming@corp.com", "Gaming", "Admin");
        var response = await service.SetPasswordAsync(reg.Id, setPasswordReq, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, response.UserId);
        Assert.Equal("gaming@corp.com", response.Email);
        Assert.Equal("merchant", response.AccountType);
        Assert.Equal(reg.WalletId, response.WalletId);

        var storedUser = await userRepo.FindByIdAsync(response.UserId, CancellationToken.None);
        Assert.NotNull(storedUser);
        Assert.Equal(reg.WalletId, storedUser.WalletId);
        Assert.True(passwords.Verify(storedUser, "SecurePass123!"));
    }

    [Fact]
    public async Task SetPasswordAsync_with_existing_linked_user_updates_password()
    {
        var clientRepo = new InMemoryExternalClientRepository();
        var walletRepo = new TestWalletRepository();
        var userRepo = new TestUserRepository();
        var passwords = new FakePasswordService();
        var service = new GatewayService(clientRepo, walletRepo, TimeProvider.System, userRepo, passwords);

        var reg = await service.RegisterAsync(new RegisterExternalClientRequest("Gaming Corp", "gaming@corp.com", "Description"), CancellationToken.None);

        await service.SetPasswordAsync(reg.Id, new SetMerchantPasswordRequest("FirstPassword123!"), CancellationToken.None);

        var updated = await service.SetPasswordAsync(reg.Id, new SetMerchantPasswordRequest("SecondPassword456!"), CancellationToken.None);

        var storedUser = await userRepo.FindByIdAsync(updated.UserId, CancellationToken.None);
        Assert.NotNull(storedUser);
        Assert.True(passwords.Verify(storedUser, "SecondPassword456!"));
        Assert.False(passwords.Verify(storedUser, "FirstPassword123!"));
    }

    [Fact]
    public async Task SetPasswordAsync_with_conflicting_email_throws_invalid_operation_exception()
    {
        var clientRepo = new InMemoryExternalClientRepository();
        var walletRepo = new TestWalletRepository();
        var userRepo = new TestUserRepository();
        var passwords = new FakePasswordService();
        var service = new GatewayService(clientRepo, walletRepo, TimeProvider.System, userRepo, passwords);

        var otherUser = User.RegisterClient("30111222", "Other", "Person", "other@example.com", DateTimeOffset.UtcNow);
        otherUser.LinkWallet(Guid.NewGuid());
        await userRepo.AddAsync(otherUser, CancellationToken.None);

        var reg = await service.RegisterAsync(new RegisterExternalClientRequest("Gaming Corp", "gaming@corp.com", "Description"), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SetPasswordAsync(reg.Id, new SetMerchantPasswordRequest("Password123!", "other@example.com"), CancellationToken.None));
    }

    private sealed class FakePasswordService : IPasswordService
    {
        public string Hash(User user, string password) => "hashed_" + password;
        public bool Verify(User user, string password) => user.PasswordHash == "hashed_" + password;
    }

    private sealed class TestUserRepository : IUserRepository
    {
        private readonly Dictionary<string, User> _byEmail = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<Guid, User> _byId = [];

        public Task<User?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
            Task.FromResult(_byEmail.GetValueOrDefault(normalizedEmail));

        public Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(_byId.GetValueOrDefault(id));

        public Task AddAsync(User user, CancellationToken cancellationToken)
        {
            _byId[user.Id] = user;
            _byEmail[user.NormalizedEmail] = user;
            return Task.CompletedTask;
        }

        public Task SaveAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<bool> IsReadyAsync(CancellationToken cancellationToken) => Task.FromResult(true);
    }
}
