
namespace Wallet.Domain
{
    public sealed class Adress
    {
        public Guid Id { get; private set; }
        public string Street { get; private set; } = "";
        public string Number { get; private set; } = "";
        public string City { get; private set; } = "";
        public string State { get; private set; } = "";
        public string PostalCode { get; private set; } = "";
        public string Country { get; private set; } = "";
        public int Latitude { get; private set; }
        public int Longitude { get; private set; }
        public AdressValidationStatus Status { get; private set; } = AdressValidationStatus.Pending;
        public Guid UserId { get; private set; }
        public DateTimeOffset CreatedAt { get; private set; }
        public DateTimeOffset UpdatedAt { get; private set; }
        private Adress() { }
    }
}
