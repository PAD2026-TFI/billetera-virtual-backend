using Wallet.Domain;
using Xunit;

namespace Wallet.Domain.Tests;

public sealed class CardRequestTests
{
    private static Address CreateValidAddress()
    {
        var address = Address.CreateDraft("Av. Corrientes", "1234", "CABA", "Buenos Aires", "C1043", DateTimeOffset.UtcNow);
        address.MarkValid(-34.6037m, -58.3816m, "place_abc", DateTimeOffset.UtcNow);
        return address;
    }

    [Fact]
    public void Create_with_valid_address_initializes_card_request_in_pending_status()
    {
        var userId = Guid.NewGuid();
        var address = CreateValidAddress();
        var now = DateTimeOffset.UtcNow;

        var request = CardRequest.Create(userId, address, now);

        Assert.NotEqual(Guid.Empty, request.Id);
        Assert.Equal(userId, request.UserId);
        Assert.Equal(address.Id, request.AddressId);
        Assert.Equal(CardRequestStatus.Pending, request.Status);
        Assert.Equal(now.ToUniversalTime(), request.RequestedAt);
        Assert.Equal(now.ToUniversalTime(), request.UpdatedAt);
    }

    [Fact]
    public void Create_with_unvalidated_address_throws_invalid_operation_exception()
    {
        var userId = Guid.NewGuid();
        var pendingAddress = Address.CreateDraft("Av. Corrientes", "1234", "CABA", "Buenos Aires", "C1043", DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() =>
            CardRequest.Create(userId, pendingAddress, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Create_with_empty_user_id_throws_argument_exception()
    {
        var address = CreateValidAddress();

        Assert.Throws<ArgumentException>(() =>
            CardRequest.Create(Guid.Empty, address, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void CardRequest_lifecycle_from_Pending_to_Delivered_succeeds()
    {
        var request = CardRequest.Create(Guid.NewGuid(), CreateValidAddress(), DateTimeOffset.UtcNow);
        Assert.Equal(CardRequestStatus.Pending, request.Status);

        request.Approve(DateTimeOffset.UtcNow);
        Assert.Equal(CardRequestStatus.Approved, request.Status);

        request.StartDelivery(DateTimeOffset.UtcNow);
        Assert.Equal(CardRequestStatus.InDelivery, request.Status);

        request.MarkDelivered(DateTimeOffset.UtcNow);
        Assert.Equal(CardRequestStatus.Delivered, request.Status);
    }

    [Fact]
    public void CardRequest_can_be_cancelled_from_Pending_or_Approved()
    {
        var req1 = CardRequest.Create(Guid.NewGuid(), CreateValidAddress(), DateTimeOffset.UtcNow);
        req1.Cancel(DateTimeOffset.UtcNow);
        Assert.Equal(CardRequestStatus.Cancelled, req1.Status);

        var req2 = CardRequest.Create(Guid.NewGuid(), CreateValidAddress(), DateTimeOffset.UtcNow);
        req2.Approve(DateTimeOffset.UtcNow);
        req2.Cancel(DateTimeOffset.UtcNow);
        Assert.Equal(CardRequestStatus.Cancelled, req2.Status);
    }

    [Fact]
    public void CardRequest_can_be_rejected_from_Pending()
    {
        var request = CardRequest.Create(Guid.NewGuid(), CreateValidAddress(), DateTimeOffset.UtcNow);
        request.Reject(DateTimeOffset.UtcNow);
        Assert.Equal(CardRequestStatus.Rejected, request.Status);
    }

    [Fact]
    public void CardRequest_invalid_transition_throws_invalid_operation_exception()
    {
        var request = CardRequest.Create(Guid.NewGuid(), CreateValidAddress(), DateTimeOffset.UtcNow);
        request.Reject(DateTimeOffset.UtcNow);

        // Cannot approve after rejection
        Assert.Throws<InvalidOperationException>(() => request.Approve(DateTimeOffset.UtcNow));
    }
}
