using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Wallet.Application;
using Wallet.Domain;
using Wallet.Infrastructure;

namespace Wallet.Tests;

// Cada test tiene host, clave, usuarios y limitadores propios. JWT y hashing son reales.
internal sealed class IdentityTestHost(string? sqlConnection = null) : WebApplicationFactory<Program>
{
    internal const string Password = "Frase de prueba 2026!";
    internal readonly byte[] Key = RandomNumberGenerator.GetBytes(64);
    internal TestUserRepository Users { get; } = new();
    internal TestWalletRepository Wallets { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Identity:Enabled", "true");
        builder.UseSetting("ConnectionStrings:DefaultConnection", sqlConnection ??
            "Server=unused;Database=unused;Integrated Security=true");
        builder.UseSetting("Jwt:Key", Convert.ToBase64String(Key));
        builder.UseSetting("Jwt:Issuer", "Wallet.Api");
        builder.UseSetting("Jwt:Audience", "Wallet.Client");
        builder.UseSetting("Jwt:LifetimeMinutes", "15");
        if (sqlConnection is null)
            builder.ConfigureTestServices(s =>
            {
                s.RemoveAll<IUserRepository>();
                s.AddSingleton<IUserRepository>(Users);
                s.RemoveAll<IWalletRepository>();
                s.AddSingleton<IWalletRepository>(Wallets);
            });
    }

    internal HttpClient Client() => CreateClient(new()
        { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

    internal User Seed(string email = "ana@example.com", string role = "Client")
    {
        var user = User.Create("Ana", "Pérez", email, DateTimeOffset.UtcNow);
        user.SetPasswordHash(new IdentityPasswordService().Hash(user, Password));
        // Sólo fixture: construir un rol distinto permite probar la política real Client.
        // No se agrega ningún rol ni endpoint a producción.
        typeof(User).GetProperty(nameof(User.Role))!.SetValue(user, role);
        Users.AddAsync(user, CancellationToken.None).GetAwaiter().GetResult();
        return user;
    }

    internal string Token(User user, string variant = "valid")
    {
        var claims = new List<Claim>
        {
            new("sub", user.Id.ToString()), new("jti", Guid.NewGuid().ToString()),
            new("role", user.Role), new("session_version", user.SessionVersion.ToString())
        };
        if (variant.StartsWith("missing:")) claims.RemoveAll(c => c.Type == variant[8..]);
        if (variant.StartsWith("invalid:"))
        {
            var name = variant[8..];
            claims.RemoveAll(c => c.Type == name);
            claims.Add(new(name, "invalid"));
        }
        var expired = variant == "expired";
        var jwt = new JwtSecurityToken(
            variant == "issuer" ? "Other.Issuer" : "Wallet.Api",
            variant == "audience" ? "Other.Audience" : "Wallet.Client",
            claims, DateTime.UtcNow.AddMinutes(-10),
            expired ? DateTime.UtcNow.AddMinutes(-5) : DateTime.UtcNow.AddMinutes(15),
            new SigningCredentials(new SymmetricSecurityKey(
                variant == "signature" ? RandomNumberGenerator.GetBytes(64) : Key),
                variant == "algorithm" ? SecurityAlgorithms.HmacSha384 : SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }
}

internal sealed class TestUserRepository : IUserRepository
{
    private readonly ConcurrentDictionary<string, User> _users = new();
    internal bool Unavailable { get; set; }
    internal int Saves { get; private set; }
    private void CheckAvailability()
    {
        if (Unavailable) throw new StorageUnavailableException(new InvalidOperationException());
    }
    public Task<User?> FindByEmailAsync(string email, CancellationToken ct)
    {
        CheckAvailability();
        return Task.FromResult(_users.GetValueOrDefault(email));
    }
    public Task<User?> FindByIdAsync(Guid id, CancellationToken ct)
    {
        CheckAvailability();
        return Task.FromResult(_users.Values.SingleOrDefault(u => u.Id == id));
    }
    public Task AddAsync(User user, CancellationToken ct)
    {
        CheckAvailability();
        if (!_users.TryAdd(user.NormalizedEmail, user)) throw new DuplicateEmailException();
        return Task.CompletedTask;
    }
    public Task SaveAsync(CancellationToken ct)
    {
        CheckAvailability();
        Saves++;
        return Task.CompletedTask;
    }
    public Task<bool> IsReadyAsync(CancellationToken ct) => Task.FromResult(!Unavailable);
}

internal sealed class TestWalletRepository : IWalletRepository
{
    internal readonly ConcurrentDictionary<Guid, global::Wallet.Domain.Wallet> Items = new();
    internal bool Unavailable { get; set; }
    public Task<WalletCreation> CreateOrGetAsync(global::Wallet.Domain.Wallet candidate, CancellationToken ct)
    {
        if (Unavailable) throw new StorageUnavailableException(new InvalidOperationException());
        var actual = Items.GetOrAdd(candidate.UserId, candidate);
        return Task.FromResult(new WalletCreation(actual, ReferenceEquals(actual, candidate)));
    }
}
