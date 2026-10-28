using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Wallet.Domain.Entities.Status;

namespace Wallet.Domain.Entities
{
    public sealed class Payment
    {
        public Guid Id { get; private set; }
        public Guid WalletId { get; private set; }
        public decimal Amount { get; private set; }
        public PaymentStatus Status { get; private set; } = PaymentStatus.Pending;
        public string Currency { get; private set; } = "ARS";
        public string ExternalReference { get; private set; } = "";
        public DateTimeOffset CreatedAt { get; private set; }
        public DateTimeOffset UpdatedAt { get; set; }

    }
}
