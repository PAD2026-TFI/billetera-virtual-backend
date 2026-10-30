using System.Globalization;
using Wallet.Application;
using Wallet.DevelopmentTools;
using Wallet.Domain;
using Wallet.Domain.Entities.Status;
using Xunit;
using DomainWallet = Wallet.Domain.Wallet;

namespace Wallet.Tests;

public sealed class DevelopmentFundingTests
{
    private static readonly Guid WalletId = Guid.Parse("d03d179d-951d-4c05-9c2e-b15c2916c378");
    private static readonly Guid Key = Guid.Parse("b4e2a833-a70b-409c-b722-018e0a6ae6ed");
    private static string[] Args(string amount = "1000.00") =>
        ["fund-wallet", "--wallet-id", WalletId.ToString(), "--amount", amount, "--idempotency-key", Key.ToString(),
            "--server", "local", "--database", "WalletDev", "--confirm-development-database"];

    [Fact]
    public void Credit_is_additive_and_has_no_transfer_limit()
    {
        var wallet = DomainWallet.Create(Guid.NewGuid(), DateTimeOffset.UtcNow);
        wallet.Credit(780401m);
        wallet.Credit(1.50m);
        Assert.Equal(780402.50m, wallet.Balance);
        Assert.Equal("ARS", wallet.Currency);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("0.001")]
    [InlineData("10000000000000000")]
    public void Invalid_amount_does_not_modify_balance(string value)
    {
        var wallet = DomainWallet.Create(Guid.NewGuid(), DateTimeOffset.UtcNow);
        wallet.Credit(10m);
        Assert.Throws<ArgumentException>(() => wallet.Credit(decimal.Parse(value, CultureInfo.InvariantCulture)));
        Assert.Equal(10m, wallet.Balance);
    }

    [Fact]
    public void Overflow_does_not_modify_balance()
    {
        var wallet = DomainWallet.Create(Guid.NewGuid(), DateTimeOffset.UtcNow);
        wallet.Credit(MoneyRules.MaxBalance);
        Assert.Throws<ArgumentException>(() => wallet.Credit(0.01m));
        Assert.Equal(MoneyRules.MaxBalance, wallet.Balance);
    }

    [Fact]
    public void Fingerprint_is_canonical_and_sensitive_to_wallet_or_amount()
    {
        var request = new DevelopmentFundingRequest(WalletId, 1000m, Key);
        var hash = DevelopmentFundingService.Fingerprint(request);
        Assert.Equal(hash, DevelopmentFundingService.Fingerprint(request with { Amount = 1000.00m }));
        Assert.NotEqual(hash, DevelopmentFundingService.Fingerprint(request with { Amount = 1000.01m }));
        Assert.NotEqual(hash, DevelopmentFundingService.Fingerprint(request with { WalletId = Guid.NewGuid() }));
        Assert.Equal(64, hash.Length);
    }

    [Fact]
    public void Funding_and_credit_movement_share_amount_wallet_identity_and_UTC_time()
    {
        var request = new DevelopmentFundingRequest(WalletId, 100m, Key);
        var now = new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.FromHours(-3));
        var funding = DevelopmentFunding.Create(WalletId, request.Amount, Key, DevelopmentFundingService.Fingerprint(request), now);
        var movement = Movement.ForDevelopmentFunding(funding, 150m);
        Assert.Equal(WalletId, movement.WalletId);
        Assert.Equal(funding.Id, movement.DevelopmentFundingId);
        Assert.Equal(MovementType.Credit, movement.Type);
        Assert.Equal(MovementSource.DevelopmentFunding, movement.Source);
        Assert.Equal(100m, movement.Amount);
        Assert.Equal(150m, movement.BalanceAfter);
        Assert.Equal(now.ToUniversalTime(), funding.CreatedAt);
        Assert.Equal(funding.CreatedAt, movement.CreatedAt);
        Assert.Equal(TimeSpan.Zero, movement.CreatedAt.Offset);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Command_refuses_non_development_environment(string? environment)
    {
        var error = Assert.Throws<FundingRejectedException>(() => FundingCommand.Validate(Args(), environment, "Server=local;Database=WalletDev"));
        Assert.Equal("DEVELOPMENT_ONLY", error.Code);
    }

    [Theory]
    [InlineData("Server=remote;Database=WalletDev")]
    [InlineData("Server=local;Database=Other")]
    [InlineData("Server=local")]
    public void Command_refuses_unconfirmed_database_or_server(string connection)
    {
        var error = Assert.Throws<FundingRejectedException>(() => FundingCommand.Validate(Args(), "Development", connection));
        Assert.Equal("DATABASE_NOT_CONFIRMED", error.Code);
    }

    [Fact]
    public void Command_refuses_missing_confirmation_and_system_database()
    {
        Assert.Throws<ArgumentException>(() => FundingCommand.Validate(Args()[..^1], "Development", "Server=local;Database=WalletDev"));
        var args = Args(); args[10] = "master";
        Assert.Equal("DATABASE_NOT_CONFIRMED", Assert.Throws<FundingRejectedException>(() =>
            FundingCommand.Validate(args, "Development", "Server=local;Database=master")).Code);
    }

    [Theory]
    [InlineData("1,000")]
    [InlineData("1.001")]
    [InlineData("-1")]
    [InlineData("0")]
    public void Command_rejects_ambiguous_or_invalid_amounts(string amount) =>
        Assert.Throws<ArgumentException>(() => FundingCommand.Validate(Args(amount), "Development", "Server=local;Database=WalletDev"));

    [Fact]
    public void Command_accepts_explicit_destination_and_dot_decimal()
    {
        var request = FundingCommand.Validate(Args(), "Development", "Server=local;Database=WalletDev");
        Assert.Equal(new(WalletId, 1000m, Key), request);
    }

    [Fact]
    public async Task Invalid_request_never_reaches_store()
    {
        var store = new CountingStore();
        var service = new DevelopmentFundingService(store);
        await Assert.ThrowsAsync<ArgumentException>(() => service.FundAsync(new(WalletId, 0m, Key), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => service.FundAsync(new(Guid.Empty, 1m, Key), CancellationToken.None));
        Assert.Equal(0, store.Calls);
    }

    private sealed class CountingStore : IDevelopmentFundingStore
    {
        internal int Calls;
        public Task<DevelopmentFundingResult> FundAsync(DevelopmentFundingRequest request, string hash, CancellationToken ct)
        {
            Calls++;
            throw new InvalidOperationException("No debe acceder a persistencia.");
        }
    }
}
