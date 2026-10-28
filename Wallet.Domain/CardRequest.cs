
namespace Wallet.Domain
{
    public sealed class CardRequest
    {
        public Guid Id { get; private set; }
        public Guid UserId { get; private set; }
        public Guid AdressId { get; private set; }
        public CardRequestStatus Status { get; private set; } = CardRequestStatus.Pending;
        public DateTimeOffset CreatedAt { get; private set; }
        public DateTimeOffset UpdatedAt { get; private set; }

        private CardRequest() { }

    }
}
