using Plutus.Domain.Accounts;
using Plutus.Domain.Common;

namespace Plutus.Domain.Tests.Accounts;

public class MonthlyPoliciesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 3, 0, 0, TimeSpan.Zero);

    private static Money Brl(decimal amount) => Money.Create(amount, "BRL");

    private static Account Open(AccountType type, decimal balance = 0m, decimal limit = 0m)
    {
        var account = Account.Open(AccountNumber.FromSequence(1), "Gustavo Osterno Thé", Brl(limit), type);
        if (balance > 0m)
        {
            account.Deposit(Brl(balance), Now);
        }

        account.ClearDomainEvents();
        return account;
    }

    private static readonly CheckingAccountFee Fee = new(Brl(12.90m), Brl(5_000m));

    [Fact]
    public void Savings_CannotHaveOverdraftLimit()
    {
        var error = Assert.Throws<DomainException>(() => Open(AccountType.Savings, limit: 100m));

        Assert.Contains("Poupança", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Savings_CannotReceiveALimitLater()
    {
        var account = Open(AccountType.Savings);

        Assert.Throws<DomainException>(() => account.ChangeOverdraftLimit(Brl(100m)));
        Assert.Equal(Brl(0m), account.OverdraftLimit);
    }

    [Fact]
    public void Open_WithoutType_IsChecking()
    {
        var account = Account.Open(AccountNumber.FromSequence(1), "Ana Lima", Brl(0m));

        Assert.Equal(AccountType.Checking, account.Type);
    }

    [Fact]
    public void UnknownType_IsRejected()
    {
        Assert.Throws<DomainException>(() => Open((AccountType)99));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(4_999.99, true)]
    [InlineData(5_000, false)]
    [InlineData(20_000, false)]
    public void CheckingFee_IsWaivedFromTheExemptBalance(decimal balance, bool charged)
    {
        var charge = Fee.Calculate(Open(AccountType.Checking, balance));

        if (charged)
        {
            Assert.Equal(new MonthlyCharge(TransactionType.Fee, Brl(12.90m), charge!.Reason), charge);
        }
        else
        {
            Assert.Null(charge);
        }
    }

    [Fact]
    public void CheckingFee_MustBePositive()
    {
        Assert.Throws<DomainException>(() => new CheckingAccountFee(Brl(0m), Brl(5_000m)));
    }

    [Theory]
    [InlineData(1_000, 5.00)]
    [InlineData(1_234.56, 6.17)] // 6,1728 truncado, não arredondado
    [InlineData(199.99, 0.99)]   // 0,99995 ficaria 1,00 se arredondasse
    public void SavingsInterest_IsTruncatedToTheCent(decimal balance, decimal expected)
    {
        var charge = new SavingsInterest(0.005m).Calculate(Open(AccountType.Savings, balance));

        Assert.Equal(TransactionType.Interest, charge!.Type);
        Assert.Equal(Brl(expected), charge.Amount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1.99)] // 0,00995: menos de um centavo
    public void SavingsInterest_BelowOneCent_PostsNothing(decimal balance)
    {
        Assert.Null(new SavingsInterest(0.005m).Calculate(Open(AccountType.Savings, balance)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    [InlineData(1)]
    public void SavingsRate_MustBeBetweenZeroAndOne(decimal rate)
    {
        Assert.Throws<DomainException>(() => new SavingsInterest(rate));
    }

    [Fact]
    public void ChargeFee_RecordsAnOutflow()
    {
        var account = Open(AccountType.Checking, 100m);

        var result = account.ChargeFee(Brl(12.90m), Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(Brl(87.10m), account.Balance);
        Assert.Equal(TransactionType.Fee, account.Transactions[^1].Type);
    }

    [Fact]
    public void ChargeFee_UsesTheOverdraftLimit_ButNotBeyondIt()
    {
        var withLimit = Open(AccountType.Checking, limit: 50m);
        var withoutLimit = Open(AccountType.Checking);

        Assert.True(withLimit.ChargeFee(Brl(12.90m), Now).IsSuccess);
        Assert.Equal(Brl(-12.90m), withLimit.Balance);

        Assert.Equal("account.insufficient_funds", withoutLimit.ChargeFee(Brl(12.90m), Now).Error?.Code);
        Assert.Equal(Brl(0m), withoutLimit.Balance);
    }

    [Fact]
    public void CreditInterest_RecordsAnInflow()
    {
        var account = Open(AccountType.Savings, 1_000m);

        var result = account.CreditInterest(Brl(5m), Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(Brl(1_005m), account.Balance);
        Assert.Equal(TransactionType.Interest, account.Transactions[^1].Type);
    }

    [Fact]
    public void CreditInterest_OnBlockedAccount_IsRefused()
    {
        var account = Open(AccountType.Savings, 1_000m);
        account.Block();

        Assert.True(account.CreditInterest(Brl(5m), Now).IsFailure);
        Assert.Equal(Brl(1_000m), account.Balance);
    }
}
