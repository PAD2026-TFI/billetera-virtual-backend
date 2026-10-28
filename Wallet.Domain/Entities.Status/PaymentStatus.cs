using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Wallet.Domain.Entities.Status
{
    public enum PaymentStatus
    {
        Pending,
        Approved,
        Rejected,
        Error
    }
}
