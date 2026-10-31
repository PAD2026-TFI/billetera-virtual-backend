using Wallet.Domain;
using Xunit;

namespace Wallet.Tests;

public sealed class WalletOwnerDomainTests
{
    private const string ValidSha256Hex = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    #region User Tests

    [Fact]
    public void User_Create_WithValidArguments_InitializesActiveUserWithClientRole()
    {
        var now = new DateTimeOffset(2026, 10, 30, 18, 0, 0, TimeSpan.FromHours(-3));

        var user = User.Create("  Ana  ", "  Pérez  ", "  Ana.Perez@Example.com  ", now);

        Assert.NotEqual(Guid.Empty, user.Id);
        Assert.Equal("Ana", user.FirstName);
        Assert.Equal("Pérez", user.LastName);
        Assert.Equal("Ana.Perez@Example.com", user.Email);
        Assert.Equal("ANA.PEREZ@EXAMPLE.COM", user.NormalizedEmail);
        Assert.Equal("Client", user.Role);
        Assert.True(user.IsActive);
        Assert.Equal(0, user.SessionVersion);
        Assert.Equal(now.ToUniversalTime(), user.CreatedAt);
        Assert.Equal(TimeSpan.Zero, user.CreatedAt.Offset);
        Assert.Empty(user.PasswordHash);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("plainaddress")]
    [InlineData("@missingusername.com")]
    [InlineData("username@.com")]
    public void User_Create_WithInvalidEmail_ThrowsArgumentException(string? invalidEmail)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            User.Create("Ana", "Pérez", invalidEmail!, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void User_Create_WithEmailExceeding254Characters_ThrowsArgumentException()
    {
        var longEmail = new string('a', 243) + "@example.com"; 

        Assert.ThrowsAny<ArgumentException>(() =>
            User.Create("Ana", "Pérez", longEmail, DateTimeOffset.UtcNow));
    }

    [Theory]
    [InlineData(null, "Pérez")]
    [InlineData("", "Pérez")]
    [InlineData("   ", "Pérez")]
    [InlineData("Ana", null)]
    [InlineData("Ana", "")]
    [InlineData("Ana", "   ")]
    public void User_Create_WithNullOrWhitespaceNames_ThrowsArgumentException(string? firstName, string? lastName)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            User.Create(firstName!, lastName!, "ana@example.com", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void User_Create_WithNamesExceeding100Characters_ThrowsArgumentException()
    {
        var longName = new string('a', 101);

        Assert.ThrowsAny<ArgumentException>(() =>
            User.Create(longName, "Pérez", "ana@example.com", DateTimeOffset.UtcNow));

        Assert.ThrowsAny<ArgumentException>(() =>
            User.Create("Ana", longName, "ana@example.com", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void User_UpdateProfile_WithValidNames_UpdatesNamesSuccessfully()
    {
        var user = User.Create("Ana", "Pérez", "ana@example.com", DateTimeOffset.UtcNow);

        user.UpdateProfile("  Ana María  ", "  Gómez  ");

        Assert.Equal("Ana María", user.FirstName);
        Assert.Equal("Gómez", user.LastName);
    }

    [Fact]
    public void User_UpdateProfile_WithInvalidName_ThrowsAndDoesNotPartiallyUpdate()
    {
        var user = User.Create("Ana", "Pérez", "ana@example.com", DateTimeOffset.UtcNow);

        Assert.ThrowsAny<ArgumentException>(() => user.UpdateProfile("NewName", "  "));

        Assert.Equal("Ana", user.FirstName);//TODO: No impide la actualización parcial. Corregir
        Assert.Equal("Pérez", user.LastName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void User_SetPasswordHash_WithNullOrWhitespace_ThrowsArgumentException(string? invalidHash)
    {
        var user = User.Create("Ana", "Pérez", "ana@example.com", DateTimeOffset.UtcNow);

        Assert.ThrowsAny<ArgumentException>(() => user.SetPasswordHash(invalidHash!));
        Assert.Empty(user.PasswordHash);
    }

    [Fact]
    public void User_SetPasswordHash_WithValidHash_SetsProperty()
    {
        var user = User.Create("Ana", "Pérez", "ana@example.com", DateTimeOffset.UtcNow);

        user.SetPasswordHash("AQAAAAEAACcQAAAAEFakePasswordHashBytes1234567890==");

        Assert.Equal("AQAAAAEAACcQAAAAEFakePasswordHashBytes1234567890==", user.PasswordHash);
    }

    [Fact]
    public void User_RevokeSessions_IncrementsSessionVersion()
    {
        var user = User.Create("Ana", "Pérez", "ana@example.com", DateTimeOffset.UtcNow);
        Assert.Equal(0, user.SessionVersion);

        user.RevokeSessions();
        Assert.Equal(1, user.SessionVersion);

        user.RevokeSessions();
        Assert.Equal(2, user.SessionVersion);
    }

    [Fact]
    public void User_Disable_SetsInactiveAndRevokesSessions()
    {
        var user = User.Create("Ana", "Pérez", "ana@example.com", DateTimeOffset.UtcNow);

        user.Disable();

        Assert.False(user.IsActive);
        Assert.Equal(1, user.SessionVersion);
    }

    [Fact]
    public void User_Activate_RestoresActiveState()
    {
        var user = User.Create("Ana", "Pérez", "ana@example.com", DateTimeOffset.UtcNow);
        user.Disable();
        Assert.False(user.IsActive);

        user.Activate();

        Assert.True(user.IsActive);
        Assert.Equal(1, user.SessionVersion); 
    }

    #endregion

    #region ExternalClient Tests

    [Fact]
    public void ExternalClient_Create_WithValidArguments_InitializesActiveB2BClient()
    {
        var now = new DateTimeOffset(2026, 10, 30, 19, 0, 0, TimeSpan.FromHours(-3));

        var client = ExternalClient.Create("  Fintech Corp S.A.  ", "  dev@fintechcorp.com  ", now, "  Payment API Integration  ");

        Assert.NotEqual(Guid.Empty, client.Id);
        Assert.Equal("Fintech Corp S.A.", client.CompanyName);
        Assert.Equal("dev@fintechcorp.com", client.ContactEmail);
        Assert.Equal("Payment API Integration", client.Description);
        Assert.True(client.IsActive);
        Assert.Empty(client.ApiKeyHash);
        Assert.Null(client.LastRotatedAt);
        Assert.Equal(now.ToUniversalTime(), client.CreatedAt);
        Assert.Equal(TimeSpan.Zero, client.CreatedAt.Offset);
    }

    [Fact]
    public void ExternalClient_Create_WithoutDescription_InitializesNullDescription()
    {
        var client = ExternalClient.Create("Fintech Corp", "dev@fintechcorp.com", DateTimeOffset.UtcNow);

        Assert.Null(client.Description);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ExternalClient_Create_WithInvalidCompanyName_ThrowsArgumentException(string? invalidName)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            ExternalClient.Create(invalidName!, "dev@fintechcorp.com", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void ExternalClient_Create_WithCompanyNameExceeding150Characters_ThrowsArgumentException()
    {
        var longName = new string('B', 151);

        Assert.ThrowsAny<ArgumentException>(() =>
            ExternalClient.Create(longName, "dev@fintechcorp.com", DateTimeOffset.UtcNow));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-email")]
    public void ExternalClient_Create_WithInvalidContactEmail_ThrowsArgumentException(string? invalidEmail)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            ExternalClient.Create("Fintech Corp", invalidEmail!, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void ExternalClient_SetApiKeyHash_WithValid64HexCharacters_UpdatesHashAndTimestampInUtc()
    {
        var client = ExternalClient.Create("Fintech Corp", "dev@fintechcorp.com", DateTimeOffset.UtcNow);
        var rotatedAt = new DateTimeOffset(2026, 10, 30, 20, 0, 0, TimeSpan.FromHours(-3));

        client.SetApiKeyHash(ValidSha256Hex, rotatedAt);

        Assert.Equal(ValidSha256Hex, client.ApiKeyHash);
        Assert.Equal(rotatedAt.ToUniversalTime(), client.LastRotatedAt);
        Assert.Equal(TimeSpan.Zero, client.LastRotatedAt!.Value.Offset);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-hex-string")]
    [InlineData("e3b0c442")] 
    [InlineData("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855aa")] 
    [InlineData("z3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")]   
    public void ExternalClient_SetApiKeyHash_WithInvalidHash_ThrowsArgumentException(string? invalidHash)
    {
        var client = ExternalClient.Create("Fintech Corp", "dev@fintechcorp.com", DateTimeOffset.UtcNow);

        Assert.ThrowsAny<ArgumentException>(() => client.SetApiKeyHash(invalidHash!, DateTimeOffset.UtcNow));
        Assert.Empty(client.ApiKeyHash);
        Assert.Null(client.LastRotatedAt);
    }

    [Fact]
    public void ExternalClient_UpdateDetails_WithValidData_UpdatesPropertiesAndSanitizes()
    {
        var client = ExternalClient.Create("Fintech Corp", "dev@fintechcorp.com", DateTimeOffset.UtcNow);

        client.UpdateDetails("  New Name Ltd.  ", "  contact@newname.com  ", "  Updated desc  ");

        Assert.Equal("New Name Ltd.", client.CompanyName);
        Assert.Equal("contact@newname.com", client.ContactEmail);
        Assert.Equal("Updated desc", client.Description);
    }

    [Fact]
    public void ExternalClient_DisableAndActivate_TogglesOperationalState()
    {
        var client = ExternalClient.Create("Fintech Corp", "dev@fintechcorp.com", DateTimeOffset.UtcNow);
        Assert.True(client.IsActive);

        client.Disable();
        Assert.False(client.IsActive);

        client.Activate();
        Assert.True(client.IsActive);
    }

    #endregion
}