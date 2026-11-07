using Wallet.Domain;

namespace Wallet.Domain.Tests;

public sealed class PendingCardAddressTests
{
    [Fact]
    public void Pending_address_blocks_manufacturing_until_that_exact_address_is_validated()
    {
        var now = DateTimeOffset.UtcNow;
        var userId = Guid.NewGuid();
        var address = Address.CreateDraft("Calle", "12", "Ciudad", "Provincia", "1000", now, userId: userId);
        var request = CardRequest.CreateAwaitingAddressValidation(userId, address, now);
        Assert.Equal(CardRequestStatus.AwaitingAddressValidation, request.Status);
        Assert.Throws<InvalidOperationException>(() => request.Process(now));
        Assert.Throws<InvalidOperationException>(() => request.UpdateStatus(CardRequestStatus.Pending, now));
        Assert.Throws<InvalidOperationException>(() => request.ConfirmAddressValidation(address, now));
        var other = Address.CreateDraft("Otra", "1", "Ciudad", "Provincia", "1000", now, userId: userId);
        other.MarkValid(0, 0, "provider-result", now);
        Assert.Throws<InvalidOperationException>(() => request.ConfirmAddressValidation(other, now));
        address.MarkValid(-34.6m, -58.4m, "provider-result", now);
        request.ConfirmAddressValidation(address, now);
        request.Process(now);
        Assert.Equal(CardRequestStatus.Processing, request.Status);
    }

    [Fact]
    public void Pending_request_can_be_cancelled()
    {
        var now = DateTimeOffset.UtcNow;
        var userId = Guid.NewGuid();
        var address = Address.CreateDraft("Calle", "12", "Ciudad", "Provincia", "1000", now, userId: userId);
        var request = CardRequest.CreateAwaitingAddressValidation(userId, address, now);
        request.Cancel(now);
        Assert.Equal(CardRequestStatus.Cancelled, request.Status);
    }
}
