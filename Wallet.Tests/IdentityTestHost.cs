using Microsoft.AspNetCore.DataProtection;
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
using Wallet.Infrastructure;
using Wallet.Domain;

namespace Wallet.Tests;

// Cada test tiene host, clave, usuarios y limitadores propios. JWT y hashing son reales.
internal sealed class IdentityTestHost(string? sqlConnection = null) : WebApplicationFactory<Program>
{
    internal const string Password = "Frase de prueba 2026!";
    internal readonly byte[] Key = RandomNumberGenerator.GetBytes(64);
    internal TestUserRepository Users { get; } = new();
    internal TestWalletRepository Wallets { get; } = new();
    internal TestTransferStore Transfers { get; } = new();
    internal TestMovementHistory History { get; } = new();
    internal TestUnitOfWork UnitOfWork { get; } = new();
    internal TestExternalClientRepository Clients { get; } = new();
    internal TestPaymentLinkRepository Links { get; } = new();
    internal TestCardRepository Cards { get; } = new();

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
                s.RemoveAll<ITransferStore>();
                s.AddSingleton<ITransferStore>(Transfers);
                s.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
                s.RemoveAll<IMovementHistoryReader>();
                s.AddSingleton<IMovementHistoryReader>(History);
                s.RemoveAll<ITransferCounterpartyResolver>();
                s.RemoveAll<IUnitOfWork>();
                s.AddSingleton<IUnitOfWork>(UnitOfWork);
                s.RemoveAll<IExternalClientRepository>();
                s.AddSingleton<IExternalClientRepository>(Clients);
                s.RemoveAll<IPaymentLinkRepository>();
                s.AddSingleton<IPaymentLinkRepository>(Links);
                s.RemoveAll<ICardRepository>();
                s.AddSingleton<ICardRepository>(Cards);
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
    internal int Creates { get; private set; }
    internal int Reads { get; private set; }
    public Task<global::Wallet.Domain.Wallet?> GetByUserIdAsync(Guid userId, CancellationToken ct)
    {
        Reads++;
        if (Unavailable) throw new StorageUnavailableException(new InvalidOperationException());
        return Task.FromResult(Items.GetValueOrDefault(userId));
    }
    public Task<WalletCreation> CreateOrGetAsync(global::Wallet.Domain.Wallet candidate, CancellationToken ct)
    {
        if (Unavailable) throw new StorageUnavailableException(new InvalidOperationException());
        Creates++;
        var actual = Items.GetOrAdd(candidate.UserId, candidate);
        return Task.FromResult(new WalletCreation(actual, ReferenceEquals(actual, candidate)));
    }
    public Task<global::Wallet.Domain.Wallet?> FindByCvuAsync(string cvu, CancellationToken ct) =>
        Task.FromResult(Items.Values.FirstOrDefault(w => w.Cvu == cvu));
    public Task<global::Wallet.Domain.Wallet?> FindByAliasAsync(string alias, CancellationToken ct) =>
        Task.FromResult(Items.Values.FirstOrDefault(w => w.Alias.Equals(alias, StringComparison.OrdinalIgnoreCase)));
    public Task<global::Wallet.Domain.Wallet?> FindByIdAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(Items.Values.FirstOrDefault(w => w.Id == id));
    public Task CreateAsync(global::Wallet.Domain.Wallet wallet, CancellationToken ct)
    {
        Items[wallet.UserId] = wallet;
        return Task.CompletedTask;
    }
    public Task<bool> ExistsCvuAsync(string cvu, CancellationToken ct) =>
        Task.FromResult(Items.Values.Any(w => w.Cvu == cvu));
    public Task<bool> ExistsAliasAsync(string alias, CancellationToken ct) =>
        Task.FromResult(Items.Values.Any(w => w.Alias.Equals(alias, StringComparison.OrdinalIgnoreCase)));
}

internal sealed class TestTransferStore : ITransferStore
{
    internal int Calls;
    internal Func<TransferCommand, string, TransferExecution>? Handler;
    public Task<TransferExecution> ExecuteAsync(TransferCommand command, string hash, CancellationToken ct)
    {
        Calls++;
        return Task.FromResult((Handler ?? throw new InvalidOperationException("Configurar resultado de persistencia en el test."))(command, hash));
    }
}

internal sealed class TestMovementHistory : IMovementHistoryReader
{
    internal readonly List<Movement> Items = [];
    internal bool Unavailable = false;
    internal int LastCount;
    public Task<IReadOnlyList<Movement>> ReadAsync(Guid walletId, int count, MovementPosition? before, CancellationToken ct)
    {
        LastCount = count;
        if (Unavailable) throw new StorageUnavailableException(new InvalidOperationException());
        var result = Items.Where(m => m.WalletId == walletId)
            .Where(m => before is null || m.CreatedAt < before.CreatedAt || m.CreatedAt == before.CreatedAt && m.Id.CompareTo(before.Id) < 0)
            .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).Take(count).ToArray();
        return Task.FromResult<IReadOnlyList<Movement>>(result);
    }
}

internal sealed class TestUnitOfWork : IUnitOfWork
{
    public Task<ITransactionScope> BeginTransactionAsync(CancellationToken ct) =>
        Task.FromResult<ITransactionScope>(new TestTransactionScope());

    public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;

    private sealed class TestTransactionScope : ITransactionScope
    {
        public Task CommitAsync(CancellationToken ct) => Task.CompletedTask;
        public Task RollbackAsync(CancellationToken ct) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

internal sealed class TestExternalClientRepository : IExternalClientRepository
{
    private readonly ConcurrentDictionary<Guid, ExternalClient> _byId = new();
    private readonly ConcurrentDictionary<string, ExternalClient> _byHash = new(StringComparer.OrdinalIgnoreCase);

    public Task<ExternalClient?> FindByApiKeyHashAsync(string apiKeyHash, CancellationToken ct) =>
        Task.FromResult(_byHash.GetValueOrDefault(apiKeyHash));

    public Task<ExternalClient?> FindByIdAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(_byId.GetValueOrDefault(id));

    public Task CreateAsync(ExternalClient client, CancellationToken ct)
    {
        _byId[client.Id] = client;
        if (!string.IsNullOrEmpty(client.ApiKeyHash))
            _byHash[client.ApiKeyHash] = client;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(ExternalClient client, CancellationToken ct)
    {
        _byId[client.Id] = client;
        if (!string.IsNullOrEmpty(client.ApiKeyHash))
            _byHash[client.ApiKeyHash] = client;
        return Task.CompletedTask;
    }
}

internal sealed class TestPaymentLinkRepository : IPaymentLinkRepository
{
    private readonly ConcurrentDictionary<string, PaymentLink> _byToken = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<Guid, PaymentLink> _byId = new();

    public Task<PaymentLink?> FindByTokenAsync(string token, CancellationToken ct) =>
        Task.FromResult(_byToken.TryGetValue(token, out var val) ? val : null);

    public Task<PaymentLink?> FindByIdAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(_byId.TryGetValue(id, out var val) ? val : null);

    public Task CreateAsync(PaymentLink link, CancellationToken ct)
    {
        _byToken[link.Token] = link;
        _byId[link.Id] = link;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(PaymentLink link, CancellationToken ct)
    {
        _byToken[link.Token] = link;
        _byId[link.Id] = link;
        return Task.CompletedTask;
    }
}

internal sealed class TestCardRepository : ICardRepository
{
    private readonly ConcurrentDictionary<Guid, Card> _cards = new();

    public Task<Card?> FindByIdAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(_cards.GetValueOrDefault(id));

    public Task<IReadOnlyList<Card>> GetByUserIdAsync(Guid userId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Card>>(_cards.Values.Where(c => c.UserId == userId).ToList());

    public Task CreateAsync(Card card, CancellationToken ct)
    {
        _cards[card.Id] = card;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Card card, CancellationToken ct)
    {
        _cards[card.Id] = card;
        return Task.CompletedTask;
    }
}
