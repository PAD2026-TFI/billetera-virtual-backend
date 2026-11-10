using System.Collections.Concurrent;
using Wallet.Application;
using Wallet.Domain;
using Xunit;
using DomainWallet = Wallet.Domain.Wallet;

namespace Wallet.Tests;

public sealed class UserRegistrationServiceTests
{
    private sealed class InMemoryUserRepository : IUserRepository
    {
        private readonly ConcurrentDictionary<string, User> _byEmail = new(StringComparer.OrdinalIgnoreCase);

        public Task<User?> FindByEmailAsync(string normalizedEmail, CancellationToken ct) =>
            Task.FromResult(_byEmail.TryGetValue(normalizedEmail, out var u) ? u : null);

        public Task<User?> FindByIdAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(_byEmail.Values.FirstOrDefault(u => u.Id == id));

        public Task<bool> ExistsByDniAsync(string dni, CancellationToken ct) =>
            Task.FromResult(_byEmail.Values.Any(u => u.Dni == dni));

        public Task<User?> FindByDniAsync(string dni, CancellationToken ct) =>
            Task.FromResult(_byEmail.Values.FirstOrDefault(u => u.Dni == dni));

        public Task AddAsync(User user, CancellationToken ct)
        {
            if (!_byEmail.TryAdd(user.NormalizedEmail, user))
                throw new DuplicateEmailException();

            return Task.CompletedTask;
        }

        public Task SaveAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<bool> IsReadyAsync(CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class InMemoryWalletRepository : IWalletRepository
    {
        private readonly ConcurrentDictionary<Guid, DomainWallet> _walletsByOwner = [];

        public Task<DomainWallet?> GetByUserIdAsync(Guid userId, CancellationToken ct) =>
            Task.FromResult(_walletsByOwner.TryGetValue(userId, out var w) ? w : null);

        public Task<WalletCreation> CreateOrGetAsync(DomainWallet candidate, CancellationToken ct)
        {
            if (_walletsByOwner.TryGetValue(candidate.OwnerId, out var existing))
                return Task.FromResult(new WalletCreation(existing, false));

            _walletsByOwner[candidate.OwnerId] = candidate;
            return Task.FromResult(new WalletCreation(candidate, true));
        }

        public Task<bool> ExistsCvuAsync(string cvu, CancellationToken ct) =>
            Task.FromResult(_walletsByOwner.Values.Any(w => w.Cvu == cvu));

        public Task<bool> ExistsAliasAsync(string alias, CancellationToken ct) =>
            Task.FromResult(_walletsByOwner.Values.Any(w => w.Alias.Equals(alias, StringComparison.OrdinalIgnoreCase)));

        public Task<DomainWallet?> FindByCvuAsync(string cvu, CancellationToken ct) =>
            Task.FromResult(_walletsByOwner.Values.FirstOrDefault(w => w.Cvu == cvu));

        public Task<DomainWallet?> FindByAliasAsync(string alias, CancellationToken ct) =>
            Task.FromResult(_walletsByOwner.Values.FirstOrDefault(w => w.Alias.Equals(alias, StringComparison.OrdinalIgnoreCase)));

        public Task CreateAsync(DomainWallet wallet, CancellationToken ct)
        {
            _walletsByOwner[wallet.OwnerId] = wallet;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(DomainWallet wallet, CancellationToken ct)
        {
            _walletsByOwner[wallet.OwnerId] = wallet;
            return Task.CompletedTask;
        }
    }

    private sealed class FakePasswordService : IPasswordService
    {
        public string Hash(User user, string password) => $"hashed_{password}";
        public bool Verify(User user, string password) => true;
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public bool Committed { get; private set; }
        public bool RolledBack { get; private set; }

        public Task<ITransactionScope> BeginTransactionAsync(CancellationToken ct) =>
            Task.FromResult<ITransactionScope>(new FakeTransactionScope(this));

        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;

        private sealed class FakeTransactionScope(FakeUnitOfWork parent) : ITransactionScope
        {
            public Task CommitAsync(CancellationToken ct)
            {
                parent.Committed = true;
                return Task.CompletedTask;
            }

            public Task RollbackAsync(CancellationToken ct)
            {
                parent.RolledBack = true;
                return Task.CompletedTask;
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class InMemoryCardRepository : ICardRepository
    {
        public readonly List<Card> Cards = [];

        public Task<Card?> FindByIdAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(Cards.FirstOrDefault(c => c.Id == id));

        public Task<IReadOnlyList<Card>> GetByUserIdAsync(Guid userId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Card>>(Cards.Where(c => c.UserId == userId).ToList());

        public Task CreateAsync(Card card, CancellationToken ct)
        {
            Cards.Add(card);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Card card, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeIdentityVerifier : IIdentityVerifier
    {
        public Func<IdentityQuery, VerifiedIdentity?>? Handler { get; set; }

        public Task<VerifiedIdentity?> FindAsync(IdentityQuery query, CancellationToken ct = default)
        {
            if (Handler is not null)
                return Task.FromResult(Handler(query));

            return Task.FromResult<VerifiedIdentity?>(new VerifiedIdentity(query.Number, "Carlos", "Gomez"));
        }
    }

    private readonly FakeIdentityVerifier _verifier = new();
    private readonly InMemoryUserRepository _users = new();
    private readonly InMemoryWalletRepository _wallets = new();
    private readonly InMemoryCardRepository _cards = new();
    private readonly FakePasswordService _passwords = new();
    private readonly ICvuGenerator _cvuGenerator = new CvuGenerator();
    private readonly IAliasGenerator _aliasGenerator = new AliasGenerator();
    private readonly FakeUnitOfWork _uow = new();
    private readonly TimeProvider _clock = TimeProvider.System;
    private readonly UserRegistrationService _service;

    public UserRegistrationServiceTests()
    {
        _service = new UserRegistrationService(
            _users,
            _wallets,
            _passwords,
            _cvuGenerator,
            _aliasGenerator,
            _uow,
            _clock,
            _verifier,
            verificationOptions: new IdentityVerificationOptions { Enabled = true },
            cards: _cards);
    }

    [Fact]
    public async Task RegisterAsync_creates_user_and_wallet_atomically_with_cvu_and_alias()
    {
        var request = new RegisterUserRequest
        {
            FirstName = "Carlos",
            LastName = "Gomez",
            Email = "carlos.gomez@test.com",
            Password = "SecurePassword123!",
            Dni = "34567890"
        };

        var result = await _service.RegisterAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("Carlos", result.Value.FirstName);
        Assert.Equal("Gomez", result.Value.LastName);
        Assert.Equal("carlos.gomez@test.com", result.Value.Email);
        Assert.Equal("34567890", result.Value.Dni);

        Assert.StartsWith("00001234", result.Value.Cvu);
        Assert.Equal(22, result.Value.Cvu.Length);
        Assert.Contains(".", result.Value.Alias);

        Assert.True(_uow.Committed);

        var savedUser = await _users.FindByIdAsync(result.Value.UserId, CancellationToken.None);
        Assert.NotNull(savedUser);
        Assert.Equal(result.Value.WalletId, savedUser.WalletId);

        var savedWallet = await _wallets.GetByUserIdAsync(result.Value.UserId, CancellationToken.None);
        Assert.NotNull(savedWallet);
        Assert.Equal(result.Value.Cvu, savedWallet.Cvu);
        Assert.Equal(result.Value.Alias, savedWallet.Alias);
        Assert.Equal(UserRegistrationService.WelcomeBonusAmount, savedWallet.Balance);

        var userCards = await _cards.GetByUserIdAsync(result.Value.UserId, CancellationToken.None);
        Assert.Single(userCards);
        var debitCard = userCards[0];
        Assert.Equal(CardType.Debit, debitCard.Type);
        Assert.True(debitCard.IsActive);
        Assert.Equal(4, debitCard.CardNumberLast4.Length);
    }

    [Fact]
    public async Task RegisterAsync_fails_when_email_already_exists()
    {
        var firstRequest = new RegisterUserRequest
        {
            FirstName = "Carlos",
            LastName = "Gomez",
            Email = "carlos.gomez@test.com",
            Password = "SecurePassword123!",
            Dni = "34567890"
        };

        var first = await _service.RegisterAsync(firstRequest, CancellationToken.None);
        Assert.True(first.IsSuccess);

        var duplicateRequest = new RegisterUserRequest
        {
            FirstName = "Carlos Segundo",
            LastName = "Gomez",
            Email = "carlos.gomez@test.com",
            Password = "AnotherPassword123!",
            Dni = "34567891"
        };

        var second = await _service.RegisterAsync(duplicateRequest, CancellationToken.None);
        Assert.False(second.IsSuccess);
        Assert.Equal("EMAIL_ALREADY_REGISTERED", second.Error!.Code);
    }

    [Fact]
    public async Task RegisterAsync_fails_when_dni_already_exists()
    {
        var firstRequest = new RegisterUserRequest
        {
            FirstName = "Carlos",
            LastName = "Gomez",
            Email = "carlos1@test.com",
            Password = "SecurePassword123!",
            Dni = "34567890"
        };
        var first = await _service.RegisterAsync(firstRequest, CancellationToken.None);
        Assert.True(first.IsSuccess);

        var duplicateDniRequest = new RegisterUserRequest
        {
            FirstName = "Carlos",
            LastName = "Gomez",
            Email = "carlos2@test.com",
            Password = "SecurePassword123!",
            Dni = "34567890"
        };
        var second = await _service.RegisterAsync(duplicateDniRequest, CancellationToken.None);
        Assert.False(second.IsSuccess);
        Assert.Equal("DNI_ALREADY_REGISTERED", second.Error!.Code);
    }

    [Fact]
    public async Task RegisterAsync_fails_when_identity_not_found_in_renaper()
    {
        _verifier.Handler = _ => null;

        var request = new RegisterUserRequest
        {
            FirstName = "Carlos",
            LastName = "Gomez",
            Email = "carlos.notfound@test.com",
            Password = "SecurePassword123!",
            Dni = "34567890"
        };

        var result = await _service.RegisterAsync(request, CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal("IDENTITY_NOT_FOUND", result.Error!.Code);
    }

    [Fact]
    public async Task RegisterAsync_fails_when_name_mismatches_renaper()
    {
        _verifier.Handler = query => new VerifiedIdentity(query.Number, "Juan", "Perez");

        var request = new RegisterUserRequest
        {
            FirstName = "Carlos",
            LastName = "Gomez",
            Email = "carlos.mismatch@test.com",
            Password = "SecurePassword123!",
            Dni = "34567890"
        };

        var result = await _service.RegisterAsync(request, CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal("IDENTITY_MISMATCH", result.Error!.Code);
    }

    [Fact]
    public async Task RegisterAsync_fails_when_renaper_provider_is_unavailable()
    {
        _verifier.Handler = _ => throw new IdentityProviderUnavailableException();

        var request = new RegisterUserRequest
        {
            FirstName = "Carlos",
            LastName = "Gomez",
            Email = "carlos.down@test.com",
            Password = "SecurePassword123!",
            Dni = "34567890"
        };

        var result = await _service.RegisterAsync(request, CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal("IDENTITY_PROVIDER_UNAVAILABLE", result.Error!.Code);
    }

    [Fact]
    public async Task RegisterAsync_succeeds_without_verification_when_disabled_in_options()
    {
        _verifier.Handler = _ => throw new Exception("Should not be called when disabled");

        var disabledService = new UserRegistrationService(
            _users,
            _wallets,
            _passwords,
            _cvuGenerator,
            _aliasGenerator,
            _uow,
            _clock,
            _verifier,
            verificationOptions: new IdentityVerificationOptions { Enabled = false },
            cards: _cards);

        var request = new RegisterUserRequest
        {
            FirstName = "Carlos",
            LastName = "Gomez",
            Email = "carlos.disabled@test.com",
            Password = "SecurePassword123!",
            Dni = "34567899"
        };

        var result = await disabledService.RegisterAsync(request, CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IdentityVerified);
    }

    [Fact]
    public async Task RegisterAsync_succeeds_and_marks_identity_verified_when_renaper_matches()
    {
        _verifier.Handler = query => new VerifiedIdentity(query.Number, "CARLOS ALBERTO", "GOMEZ");

        var request = new RegisterUserRequest
        {
            FirstName = "Carlos",
            LastName = "Gomez",
            Email = "carlos.verified@test.com",
            Password = "SecurePassword123!",
            Dni = "34567892"
        };

        var result = await _service.RegisterAsync(request, CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IdentityVerified);
    }
}
