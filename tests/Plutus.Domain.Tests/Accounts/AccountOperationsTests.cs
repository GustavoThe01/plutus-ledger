using Plutus.Domain.Accounts;
using Plutus.Domain.Common;

namespace Plutus.Domain.Tests.Accounts;

public class AccountOperationsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    private static Money Brl(decimal amount) => Money.Create(amount, "BRL");

    private static Account OpenAccount(decimal limit = 0m, int sequence = 1) =>
        Account.Open(AccountNumber.FromSequence(sequence), "Gustavo Osterno Thé", Brl(limit));

    [Fact]
    public void Deposit_IncreasesBalance_AndRecordsTransaction()
    {
        var account = OpenAccount();

        var result = account.Deposit(Brl(100m), Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(Brl(100m), account.Balance);
        var transaction = Assert.Single(account.Transactions);
        Assert.Equal(TransactionType.Deposit, transaction.Type);
        Assert.Equal(Brl(100m), transaction.BalanceAfter);
        Assert.Equal(Now, transaction.OccurredAt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void Deposit_RejectsNonPositiveAmount(decimal amount)
    {
        var account = OpenAccount();

        var result = account.Deposit(Brl(amount), Now);

        Assert.Equal("account.invalid_amount", result.Error?.Code);
        Assert.Empty(account.Transactions);
    }

    [Fact]
    public void Deposit_RejectsOtherCurrency()
    {
        var result = OpenAccount().Deposit(Money.Create(10m, "USD"), Now);

        Assert.Equal("account.currency_mismatch", result.Error?.Code);
    }

    [Fact]
    public void Deposit_RejectsBlockedAccount()
    {
        var account = OpenAccount();
        account.Block();

        var result = account.Deposit(Brl(10m), Now);

        Assert.Equal("account.not_active", result.Error?.Code);
    }

    [Fact]
    public void Withdraw_DecreasesBalance()
    {
        var account = OpenAccount();
        account.Deposit(Brl(100m), Now);

        var result = account.Withdraw(Brl(30m), Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(Brl(70m), account.Balance);
        Assert.Equal(TransactionType.Withdrawal, account.Transactions[^1].Type);
    }

    [Fact]
    public void Withdraw_CanUseOverdraftLimit_UpToTheExactValue()
    {
        var account = OpenAccount(limit: 50m);
        account.Deposit(Brl(100m), Now);

        var result = account.Withdraw(Brl(150m), Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(Brl(-50m), account.Balance);
        Assert.True(account.AvailableBalance.IsZero);
    }

    [Fact]
    public void Withdraw_AboveAvailable_FailsWithoutChangingBalance_AndRaisesEvent()
    {
        var account = OpenAccount(limit: 50m);
        account.Deposit(Brl(100m), Now);

        var result = account.Withdraw(Brl(150.01m), Now);

        Assert.Equal("account.insufficient_funds", result.Error?.Code);
        Assert.Equal(Brl(100m), account.Balance);
        var rejected = Assert.IsType<WithdrawalRejected>(Assert.Single(account.DomainEvents));
        Assert.Equal(Brl(150m), rejected.Available);
    }

    [Fact]
    public void ChangeOverdraftLimit_CannotGoBelowWhatIsAlreadyInUse()
    {
        var account = OpenAccount(limit: 100m);
        account.Withdraw(Brl(80m), Now);

        Assert.Throws<DomainException>(() => account.ChangeOverdraftLimit(Brl(50m)));

        account.ChangeOverdraftLimit(Brl(80m));
        Assert.True(account.AvailableBalance.IsZero);
    }

    [Fact]
    public void Close_WithBalance_IsRejected()
    {
        var account = OpenAccount();
        account.Deposit(Brl(1m), Now);

        Assert.Throws<DomainException>(account.Close);
    }

    [Fact]
    public void Value_OfFailedResult_ThrowsInvalidOperation()
    {
        var result = Result<int>.Failure(new Error("x", "falhou"));

        Assert.Throws<InvalidOperationException>(() => result.Value);
    }
}
