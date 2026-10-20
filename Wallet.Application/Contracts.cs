using Wallet.Domain;
namespace Wallet.Application;

public interface IUserRepository
{
    Task<User?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken);
    Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken);
    Task AddAsync(User user, CancellationToken cancellationToken);
    Task SaveAsync(CancellationToken cancellationToken);
    Task<bool> IsReadyAsync(CancellationToken cancellationToken);
}
public interface IPasswordService
{
    string Hash(User user, string password);
    bool Verify(User user, string password);
}
public interface ITokenService { TokenResponse Create(User user); }
public interface ICurrentUser { Guid Id { get; } }

public sealed class DuplicateEmailException : Exception { }
public sealed class ConcurrentUpdateException : Exception { }
public sealed class StorageUnavailableException(Exception inner) : Exception("Storage unavailable", inner) { }
