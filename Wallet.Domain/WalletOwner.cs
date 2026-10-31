namespace Wallet.Domain
{
    public abstract class WalletOwner
    {
        public Guid Id { get; protected set; }
        public bool IsActive { get; protected set; } = true;
        public DateTimeOffset CreatedAt { get; protected set; }
        public byte[] RowVersion { get; protected set; } = [];

        protected WalletOwner() { }
        protected WalletOwner(Guid id, DateTimeOffset createdAt)
        {
            if (id == Guid.Empty)
                throw new ArgumentException("Owner ID cannot be empty.", nameof(id));

            Id = id;
            CreatedAt = createdAt.ToUniversalTime();
            IsActive = true;
        }

        public virtual void Disable()
        {
            IsActive = false;
        }

        public virtual void Activate()
        {
            IsActive = true;
        }

    }
}
