using Plutus.Domain.Accounts;
using Plutus.Domain.Common;

namespace Plutus.Domain.Tests.Accounts;

public class AccountTests
{
    private static Money Brl(decimal amount) => Money.Create(amount, "BRL");

    private static Account OpenAccount(decimal limit = 500m) =>
        Account.Open(AccountNumber.FromSequence(1), "Gustavo Osterno Thé", Brl(limit));

    [Fact]
    public void Open_StartsActiveWithZeroBalance()
    {
        var account = OpenAccount();

        Assert.Equal(AccountStatus.Active, account.Status);
        Assert.True(account.Balance.IsZero);
        Assert.Equal("BRL", account.Currency);
        Assert.Equal(Brl(500m), account.AvailableBalance);
    }

    [Fact]
    public void Open_TrimsHolderName()
    {
        var account = Account.Open(AccountNumber.FromSequence(2), "  Ana Lima  ", Brl(0m));

        Assert.Equal("Ana Lima", account.HolderName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("Al")]
    public void Open_RejectsInvalidHolderName(string holderName)
    {
        Assert.Throws<DomainException>(() => Account.Open(AccountNumber.FromSequence(3), holderName, Brl(0m)));
    }

    [Fact]
    public void Open_RejectsNegativeLimit()
    {
        Assert.Throws<DomainException>(() => OpenAccount(limit: -1m));
    }

    [Fact]
    public void Open_RejectsMissingAccountNumber()
    {
        Assert.Throws<DomainException>(() => Account.Open(default, "Ana Lima", Brl(0m)));
    }

    [Fact]
    public void ChangeOverdraftLimit_UpdatesAvailableBalance()
    {
        var account = OpenAccount(limit: 500m);

        account.ChangeOverdraftLimit(Brl(1_000m));

        Assert.Equal(Brl(1_000m), account.AvailableBalance);
    }

    [Fact]
    public void ChangeOverdraftLimit_RejectsOtherCurrency()
    {
        var account = OpenAccount();

        Assert.Throws<DomainException>(() => account.ChangeOverdraftLimit(Money.Create(100m, "USD")));
    }

    [Fact]
    public void BlockAndUnblock_FollowTheStatusRules()
    {
        var account = OpenAccount();

        account.Block();
        Assert.Equal(AccountStatus.Blocked, account.Status);
        Assert.Throws<DomainException>(account.Block);

        account.Unblock();
        Assert.Equal(AccountStatus.Active, account.Status);
        Assert.Throws<DomainException>(account.Unblock);
    }

    [Fact]
    public void Close_WithZeroBalance_ClosesForGood()
    {
        var account = OpenAccount();

        account.Close();

        Assert.Equal(AccountStatus.Closed, account.Status);
        Assert.Throws<DomainException>(account.Close);
        Assert.Throws<DomainException>(account.Block);
        Assert.Throws<DomainException>(() => account.ChangeOverdraftLimit(Brl(10m)));
    }

    [Fact]
    public void Close_BlockedAccount_IsAllowed()
    {
        var account = OpenAccount();
        account.Block();

        account.Close();

        Assert.Equal(AccountStatus.Closed, account.Status);
    }

    [Fact]
    public void Open_GeneratesDistinctIds()
    {
        Assert.NotEqual(OpenAccount().Id, OpenAccount().Id);
    }
}
