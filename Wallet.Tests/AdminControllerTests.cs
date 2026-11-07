using System.Net;
using System.Net.Http.Json;
using Wallet.Application;
using Wallet.Domain;
using Xunit;
using DomainWallet = Wallet.Domain.Wallet;

namespace Wallet.Tests;

public sealed class AdminControllerTests
{
    private sealed class FakeFundingStore : IDevelopmentFundingStore
    {
        public readonly List<DevelopmentFundingRequest> Requests = [];

        public Task<DevelopmentFundingResult> FundAsync(DevelopmentFundingRequest request, string hash, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(new DevelopmentFundingResult(
                Guid.NewGuid(),
                request.WalletId,
                request.Amount,
                "ARS",
                DateTimeOffset.UtcNow,
                request.Amount,
                true));
        }
    }

    [Fact]
    public async Task RegisterAdmin_creates_admin_account_successfully()
    {
        await using var host = new IdentityTestHost();
        using var client = host.Client();

        var request = new RegisterAdminRequest
        {
            FirstName = "Super",
            LastName = "Admin",
            Email = "super.admin@virtualwallet.com",
            Password = "AdministratorSecurePassword123!",
            Dni = "99887766"
        };

        using var response = await client.PostAsJsonAsync("/api/admin/register", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var userResponse = await response.Content.ReadFromJsonAsync<UserResponse>();
        Assert.NotNull(userResponse);
        Assert.Equal("Super", userResponse.FirstName);
        Assert.Equal("super.admin@virtualwallet.com", userResponse.Email);

        var saved = await host.Users.FindByEmailAsync(User.NormalizeEmail(request.Email), CancellationToken.None);
        Assert.NotNull(saved);
        Assert.Equal("Admin", saved.Role);
    }

    [Fact]
    public async Task RegisterAdmin_fails_when_email_already_exists()
    {
        await using var host = new IdentityTestHost();
        var existing = host.Seed("admin.duplicate@test.com");
        using var client = host.Client();

        var request = new RegisterAdminRequest
        {
            FirstName = "Another",
            LastName = "Admin",
            Email = "admin.duplicate@test.com",
            Password = "AdministratorSecurePassword123!",
            Dni = "12345678"
        };

        using var response = await client.PostAsJsonAsync("/api/admin/register", request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task FundWallet_requires_admin_role()
    {
        await using var host = new IdentityTestHost();
        var clientUser = host.Seed("client@test.com", role: "Client");
        using var client = host.Client();
        client.DefaultRequestHeaders.Authorization = new("Bearer", host.Token(clientUser));

        var fundRequest = new AdminFundWalletRequest
        {
            Cvu = "0000123400000012345678",
            Amount = 5000m
        };

        using var response = await client.PostAsJsonAsync("/api/admin/funds", fundRequest);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task FundWallet_returns_404_when_target_wallet_does_not_exist()
    {
        await using var host = new IdentityTestHost();
        var adminUser = host.Seed("admin@test.com", role: "Admin");
        using var client = host.Client();
        client.DefaultRequestHeaders.Authorization = new("Bearer", host.Token(adminUser));

        var fundRequest = new AdminFundWalletRequest
        {
            Cvu = "0000123400000099999999",
            Amount = 5000m
        };

        using var response = await client.PostAsJsonAsync("/api/admin/funds", fundRequest);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task FundByCvu_returns_404_when_cvu_does_not_exist()
    {
        await using var host = new IdentityTestHost();
        var adminUser = host.Seed("admin@test.com", role: "Admin");
        using var client = host.Client();
        client.DefaultRequestHeaders.Authorization = new("Bearer", host.Token(adminUser));

        var req = new FundByCvuRequest
        {
            Cvu = "0000123400000099999999",
            Amount = 1000m
        };

        using var response = await client.PostAsJsonAsync("/api/admin/funds/cvu", req);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task FundByAlias_returns_404_when_alias_does_not_exist()
    {
        await using var host = new IdentityTestHost();
        var adminUser = host.Seed("admin@test.com", role: "Admin");
        using var client = host.Client();
        client.DefaultRequestHeaders.Authorization = new("Bearer", host.Token(adminUser));

        var req = new FundByAliasRequest
        {
            Alias = "alias.inexistente.ars",
            Amount = 1000m
        };

        using var response = await client.PostAsJsonAsync("/api/admin/funds/alias", req);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task FundByWalletId_returns_404_when_wallet_does_not_exist()
    {
        await using var host = new IdentityTestHost();
        var adminUser = host.Seed("admin@test.com", role: "Admin");
        using var client = host.Client();
        client.DefaultRequestHeaders.Authorization = new("Bearer", host.Token(adminUser));

        var req = new FundByWalletIdRequest
        {
            WalletId = Guid.NewGuid(),
            Amount = 1000m
        };

        using var response = await client.PostAsJsonAsync("/api/admin/funds/wallet-id", req);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
