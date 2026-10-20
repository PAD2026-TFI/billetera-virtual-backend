using Microsoft.AspNetCore.Identity;
using Wallet.Application;
using Wallet.Domain;
namespace Wallet.Infrastructure;

public sealed class IdentityPasswordService : IPasswordService
{
    private readonly PasswordHasher<User> _hasher = new();
    public string Hash(User user, string password) => _hasher.HashPassword(user, password);
    public bool Verify(User user, string password) =>
        _hasher.VerifyHashedPassword(user, user.PasswordHash, password) != PasswordVerificationResult.Failed;
}
