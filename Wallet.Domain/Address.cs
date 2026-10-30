namespace Wallet.Domain;

public sealed class Address
{
    public Guid Id { get; private set; }
    public string Street { get; private set; } = "";
    public string Number { get; private set; } = "";
    public string City { get; private set; } = "";
    public string State { get; private set; } = "";
    public string Country { get; private set; } = "Argentina";
    public string PostalCode { get; private set; } = "";
    public decimal? Latitude { get; private set; }
    public decimal? Longitude { get; private set; }
    public AddressValidationStatus ValidationStatus { get; private set; }
    public string? ExternalPlaceId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ValidatedAt { get; private set; }

    private Address() { }

    public static Address CreateDraft(
        string street,
        string number,
        string city,
        string state,
        string postalCode,
        DateTimeOffset now,
        string country = "Argentina")
    {
        ValidateStreet(street);
        ValidateNumber(number);
        ValidateCity(city);
        ValidateState(state);
        ValidatePostalCode(postalCode);

        return new Address
        {
            Id = Guid.NewGuid(),
            Street = street.Trim(),
            Number = number.Trim(),
            City = city.Trim(),
            State = state.Trim(),
            Country = string.IsNullOrWhiteSpace(country) ? "Argentina" : country.Trim(),
            PostalCode = postalCode.Trim(),
            ValidationStatus = AddressValidationStatus.Pending,
            CreatedAt = now.ToUniversalTime()
        };
    }

    public void MarkValid(decimal latitude, decimal longitude, string? externalPlaceId, DateTimeOffset now)
    {
        if (latitude is < -90m or > 90m)
            throw new ArgumentOutOfRangeException(nameof(latitude), "Latitude must be between -90 and 90 degrees.");

        if (longitude is < -180m or > 180m)
            throw new ArgumentOutOfRangeException(nameof(longitude), "Longitude must be between -180 and 180 degrees.");

        Latitude = decimal.Round(latitude, 6);
        Longitude = decimal.Round(longitude, 6);
        ExternalPlaceId = string.IsNullOrWhiteSpace(externalPlaceId) ? null : externalPlaceId.Trim();
        ValidationStatus = AddressValidationStatus.Valid;
        ValidatedAt = now.ToUniversalTime();
    }

    public void MarkInvalid(DateTimeOffset now)
    {
        ValidationStatus = AddressValidationStatus.Invalid;
        Latitude = null;
        Longitude = null;
        ExternalPlaceId = null;
        ValidatedAt = now.ToUniversalTime();
    }

    private static void ValidateStreet(string street)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(street);
        if (street.Trim().Length > 150)
            throw new ArgumentException("Street name cannot exceed 150 characters.", nameof(street));
    }

    private static void ValidateNumber(string number)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(number);
        if (number.Trim().Length > 20)
            throw new ArgumentException("Street number cannot exceed 20 characters.", nameof(number));
    }

    private static void ValidateCity(string city)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(city);
        if (city.Trim().Length > 100)
            throw new ArgumentException("City name cannot exceed 100 characters.", nameof(city));
    }

    private static void ValidateState(string state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(state);
        if (state.Trim().Length > 100)
            throw new ArgumentException("State/Province name cannot exceed 100 characters.", nameof(state));
    }

    private static void ValidatePostalCode(string postalCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(postalCode);
        if (postalCode.Trim().Length > 20)
            throw new ArgumentException("Postal code cannot exceed 20 characters.", nameof(postalCode));
    }
}