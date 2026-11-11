using Wallet.Domain;
using Xunit;

namespace Wallet.Domain.Tests;

public sealed class UserIdentityTests
{
    [Theory]
    [InlineData("1234567")]   // 7 dígitos
    [InlineData("12345678")]  // 8 dígitos
    [InlineData("01234567")]  // 8 dígitos con cero a la izquierda
    public void RegisterClient_with_valid_7_or_8_digit_dni_succeeds(string dni)
    {
        var now = DateTimeOffset.UtcNow;
        var user = User.RegisterClient(dni, "Juan", "Pérez", "juan@example.com", now);

        Assert.Equal(dni.Trim(), user.Dni);
        Assert.Null(user.IdentityVerifiedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("123456")]     // 6 dígitos (demasiado corto)
    [InlineData("123456789")]   // 9 dígitos (demasiado largo)
    [InlineData("1234ABCD")]   // Letras
    [InlineData("1234-567")]   // Guiones
    [InlineData("1234567 ")]   // Trim debería aplicar pero si hay letras o símbolos falla
    public void RegisterClient_with_invalid_dni_throws_argument_exception(string dni)
    {
        var now = DateTimeOffset.UtcNow;
        if (dni == "1234567 ")
        {
            // Trim() produce "1234567" que es válido de 7 dígitos
            var user = User.RegisterClient(dni, "Juan", "Pérez", "juan@example.com", now);
            Assert.Equal("1234567", user.Dni);
            return;
        }

        Assert.ThrowsAny<ArgumentException>(() =>
            User.RegisterClient(dni, "Juan", "Pérez", "juan@example.com", now));
    }

    [Fact]
    public void RegisterClient_with_null_dni_throws_argument_exception()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            User.RegisterClient(null!, "Juan", "Pérez", "juan@example.com", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void RegisterAdmin_with_valid_dni_succeeds()
    {
        var admin = User.RegisterAdmin("99887766", "Admin", "User", "admin@example.com", DateTimeOffset.UtcNow);
        Assert.Equal("99887766", admin.Dni);
        Assert.Equal("Admin", admin.Role);
        Assert.Null(admin.IdentityVerifiedAt);
    }

    [Fact]
    public void MarkIdentityVerified_sets_verified_at_in_utc()
    {
        var user = User.RegisterClient("34567890", "Ana", "García", "ana@example.com", DateTimeOffset.UtcNow);
        Assert.Null(user.IdentityVerifiedAt);

        var verificationTime = DateTimeOffset.UtcNow;
        user.MarkIdentityVerified(verificationTime);

        Assert.NotNull(user.IdentityVerifiedAt);
        Assert.Equal(verificationTime.ToUniversalTime(), user.IdentityVerifiedAt.Value);
    }

    [Fact]
    public void MarkIdentityVerified_with_far_future_date_throws_argument_exception()
    {
        var user = User.RegisterClient("34567890", "Ana", "García", "ana@example.com", DateTimeOffset.UtcNow);
        var future = DateTimeOffset.UtcNow.AddDays(1);

        Assert.Throws<ArgumentException>(() => user.MarkIdentityVerified(future));
    }
}
