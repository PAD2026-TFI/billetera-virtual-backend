using Wallet.Domain;
using Xunit;

namespace Wallet.Domain.Tests;

public sealed class AddressTests
{
    [Fact]
    public void CreateDraft_initializes_pending_address_with_valid_parameters()
    {
        var now = DateTimeOffset.UtcNow;
        var address = Address.CreateDraft("Av. Corrientes", "1234", "CABA", "Buenos Aires", "C1043", now);

        Assert.NotEqual(Guid.Empty, address.Id);
        Assert.Equal("Av. Corrientes", address.Street);
        Assert.Equal("1234", address.Number);
        Assert.Equal("CABA", address.City);
        Assert.Equal("Buenos Aires", address.State);
        Assert.Equal("C1043", address.PostalCode);
        Assert.Equal("Argentina", address.Country);
        Assert.Equal(AddressValidationStatus.Pending, address.ValidationStatus);
        Assert.Null(address.Latitude);
        Assert.Null(address.Longitude);
        Assert.Null(address.ExternalPlaceId);
        Assert.Equal(now.ToUniversalTime(), address.CreatedAt);
        Assert.Null(address.ValidatedAt);
    }

    [Theory]
    [InlineData("", "123", "City", "State", "1000")]
    [InlineData("Street", "", "City", "State", "1000")]
    [InlineData("Street", "123", "", "State", "1000")]
    [InlineData("Street", "123", "City", "", "1000")]
    [InlineData("Street", "123", "City", "State", "")]
    public void CreateDraft_with_empty_or_whitespace_fields_throws_argument_exception(
        string street, string number, string city, string state, string postalCode)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            Address.CreateDraft(street, number, city, state, postalCode, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void MarkValid_sets_coordinates_and_valid_status()
    {
        var address = Address.CreateDraft("Av. Corrientes", "1234", "CABA", "Buenos Aires", "C1043", DateTimeOffset.UtcNow);
        var now = DateTimeOffset.UtcNow;

        address.MarkValid(-34.603722m, -58.381592m, "place_12345", now);

        Assert.Equal(AddressValidationStatus.Valid, address.ValidationStatus);
        Assert.Equal(-34.603722m, address.Latitude);
        Assert.Equal(-58.381592m, address.Longitude);
        Assert.Equal("place_12345", address.ExternalPlaceId);
        Assert.Equal(now.ToUniversalTime(), address.ValidatedAt);
    }

    [Theory]
    [InlineData(-90.000001, -58.38)]
    [InlineData(90.000001, -58.38)]
    [InlineData(-34.60, -180.000001)]
    [InlineData(-34.60, 180.000001)]
    public void MarkValid_with_out_of_range_coordinates_throws_argument_out_of_range_exception(decimal lat, decimal lon)
    {
        var address = Address.CreateDraft("Av. Corrientes", "1234", "CABA", "Buenos Aires", "C1043", DateTimeOffset.UtcNow);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            address.MarkValid(lat, lon, null, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void MarkInvalid_resets_coordinates_and_sets_invalid_status()
    {
        var address = Address.CreateDraft("Av. Corrientes", "1234", "CABA", "Buenos Aires", "C1043", DateTimeOffset.UtcNow);
        address.MarkValid(-34.60m, -58.38m, "place_123", DateTimeOffset.UtcNow);

        var now = DateTimeOffset.UtcNow;
        address.MarkInvalid(now);

        Assert.Equal(AddressValidationStatus.Invalid, address.ValidationStatus);
        Assert.Null(address.Latitude);
        Assert.Null(address.Longitude);
        Assert.Null(address.ExternalPlaceId);
        Assert.Equal(now.ToUniversalTime(), address.ValidatedAt);
    }
}
