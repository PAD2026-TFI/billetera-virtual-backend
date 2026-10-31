using Wallet.Domain;
using Xunit;

namespace Wallet.Domain.Tests;

public sealed class WalletOwnerTests
{
    [Fact]
    public void User_Create_with_valid_parameters_initializes_owner_and_user_properties()
    {
        var now = DateTimeOffset.UtcNow;
        var user = User.Create("Juan", "Pérez", "juan.perez@example.com", now);

        Assert.NotEqual(Guid.Empty, user.Id);
        Assert.True(user.IsActive);
        Assert.Equal(now.ToUniversalTime(), user.CreatedAt);
        Assert.Equal("Juan", user.FirstName);
        Assert.Equal("Pérez", user.LastName);
        Assert.Equal("juan.perez@example.com", user.Email);
        Assert.Equal("JUAN.PEREZ@EXAMPLE.COM", user.NormalizedEmail);
        Assert.Equal("Client", user.Role);
        Assert.Equal(0, user.SessionVersion);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid-email")]
    [InlineData("user@")]
    public void User_Create_with_invalid_email_throws_argument_exception(string email)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            User.Create("Juan", "Pérez", email, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void User_UpdateProfile_validates_and_updates_names()
    {
        var user = User.Create("Juan", "Pérez", "juan@example.com", DateTimeOffset.UtcNow);
        user.UpdateProfile(" Carlos ", " Gómez ");

        Assert.Equal("Carlos", user.FirstName);
        Assert.Equal("Gómez", user.LastName);
    }

    [Fact]
    public void User_UpdateProfile_with_name_exceeding_100_chars_throws_argument_exception()
    {
        var user = User.Create("Juan", "Pérez", "juan@example.com", DateTimeOffset.UtcNow);
        var longName = new string('A', 101);

        Assert.Throws<ArgumentException>(() => user.UpdateProfile(longName, "Pérez"));
        Assert.Throws<ArgumentException>(() => user.UpdateProfile("Juan", longName));
    }

    [Fact]
    public void User_SetPasswordHash_sets_hash_when_valid()
    {
        var user = User.Create("Juan", "Pérez", "juan@example.com", DateTimeOffset.UtcNow);
        user.SetPasswordHash("hashed_secret_value");

        Assert.Equal("hashed_secret_value", user.PasswordHash);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void User_SetPasswordHash_with_null_or_whitespace_throws_argument_exception(string? hash)
    {
        var user = User.Create("Juan", "Pérez", "juan@example.com", DateTimeOffset.UtcNow);
        Assert.ThrowsAny<ArgumentException>(() => user.SetPasswordHash(hash!));
    }

    [Fact]
    public void User_RevokeSessions_increments_session_version()
    {
        var user = User.Create("Juan", "Pérez", "juan@example.com", DateTimeOffset.UtcNow);
        Assert.Equal(0, user.SessionVersion);

        user.RevokeSessions();
        Assert.Equal(1, user.SessionVersion);

        user.RevokeSessions();
        Assert.Equal(2, user.SessionVersion);
    }

    [Fact]
    public void User_Disable_deactivates_and_revokes_sessions()
    {
        var user = User.Create("Juan", "Pérez", "juan@example.com", DateTimeOffset.UtcNow);
        user.Disable();

        Assert.False(user.IsActive);
        Assert.Equal(1, user.SessionVersion);

        user.Activate();
        Assert.True(user.IsActive);
    }

    [Fact]
    public void ExternalClient_Create_with_valid_parameters_initializes_properly()
    {
        var now = DateTimeOffset.UtcNow;
        var client = ExternalClient.Create("Fintech Corp", "tech@fintech.com", now, "Payment gateway consumer");

        Assert.NotEqual(Guid.Empty, client.Id);
        Assert.True(client.IsActive);
        Assert.Equal(now.ToUniversalTime(), client.CreatedAt);
        Assert.Equal("Fintech Corp", client.CompanyName);
        Assert.Equal("tech@fintech.com", client.ContactEmail);
        Assert.Equal("Payment gateway consumer", client.Description);
        Assert.Empty(client.ApiKeyHash);
        Assert.Null(client.LastRotatedAt);
    }

    [Fact]
    public void ExternalClient_SetApiKeyHash_with_valid_64_hex_hash_updates_hash_and_rotation_date()
    {
        var client = ExternalClient.Create("Fintech Corp", "tech@fintech.com", DateTimeOffset.UtcNow);
        var validHash = new string('a', 64);
        var now = DateTimeOffset.UtcNow;

        client.SetApiKeyHash(validHash, now);

        Assert.Equal(validHash, client.ApiKeyHash);
        Assert.Equal(now.ToUniversalTime(), client.LastRotatedAt);
    }

    [Theory]
    [InlineData("too-short")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public void ExternalClient_SetApiKeyHash_with_invalid_hash_throws_argument_exception(string invalidHash)
    {
        var client = ExternalClient.Create("Fintech Corp", "tech@fintech.com", DateTimeOffset.UtcNow);

        Assert.Throws<ArgumentException>(() => client.SetApiKeyHash(invalidHash, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void ExternalClient_UpdateDetails_updates_and_validates_fields()
    {
        var client = ExternalClient.Create("Fintech Corp", "tech@fintech.com", DateTimeOffset.UtcNow);
        client.UpdateDetails("New Corp", "new@fintech.com", "Updated description");

        Assert.Equal("New Corp", client.CompanyName);
        Assert.Equal("new@fintech.com", client.ContactEmail);
        Assert.Equal("Updated description", client.Description);
    }

    [Fact]
    public void ExternalClient_Disable_and_Activate_manage_state()
    {
        var client = ExternalClient.Create("Fintech Corp", "tech@fintech.com", DateTimeOffset.UtcNow);
        Assert.True(client.IsActive);

        client.Disable();
        Assert.False(client.IsActive);

        client.Activate();
        Assert.True(client.IsActive);
    }
}
