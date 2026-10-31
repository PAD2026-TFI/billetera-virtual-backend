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
}
