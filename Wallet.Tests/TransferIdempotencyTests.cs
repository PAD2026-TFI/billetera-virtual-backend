using System.Globalization;
using Wallet.Application;
using Wallet.Domain;
using Xunit;

namespace Wallet.Tests;

public sealed class TransferIdempotencyTests
{
    [Fact]
    public void Equivalent_amounts_and_cultures_identify_same_content_but_changed_destination_does_not()
    {
        var destination = Guid.NewGuid(); var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("es-AR");
            var hash = TransferService.Fingerprint(destination, 300m);
            var transfer = Transfer.Complete(Guid.NewGuid(), destination, 300m, Guid.NewGuid(), hash, DateTimeOffset.UtcNow);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            Assert.Equal(hash, TransferService.Fingerprint(destination, 300.00m));
            Assert.True(transfer.MatchesRequest(destination, 300.00m, hash));
            Assert.False(transfer.MatchesRequest(destination, 301m, hash));
            Assert.False(transfer.MatchesRequest(Guid.NewGuid(), 300m, hash));
            Assert.False(transfer.MatchesRequest(destination, 300m, new string('0', 64)));
        }
        finally { CultureInfo.CurrentCulture = originalCulture; }
    }
}
