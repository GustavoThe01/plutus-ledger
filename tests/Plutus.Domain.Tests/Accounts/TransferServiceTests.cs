using Plutus.Domain.Accounts;

namespace Plutus.Domain.Tests.Accounts;

public class TransferServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    private static Money Brl(decimal amount) => Money.Create(amount, "BRL");

    private static Account Funded(int sequence, decimal balance, string currency = "BRL")
    {
        var account = Account.Open(AccountNumber.FromSequence(sequence), "Titular " + sequence, Money.Zero(currency));
        if (balance > 0m)
        {
            account.Deposit(Money.Create(balance, currency), Now);
        }

        return account;
    }

    [Fact]
    public void Transfer_MovesMoney_AndRecordsBothSides()
    {
        var source = Funded(1, 100m);
        var destination = Funded(2, 0m);

        var result = TransferService.Transfer(source, destination, Brl(40m), Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(Brl(60m), source.Balance);
        Assert.Equal(Brl(40m), destination.Balance);

        Assert.Equal(TransactionType.TransferOut, source.Transactions[^1].Type);
        Assert.Equal(destination.Id, source.Transactions[^1].CounterpartyId);
        Assert.Equal(TransactionType.TransferIn, destination.Transactions[^1].Type);
        Assert.Equal(source.Id, destination.Transactions[^1].CounterpartyId);
    }

    [Fact]
    public void Transfer_RaisesSingleEvent_OnSource()
    {
        var source = Funded(1, 100m);
        var destination = Funded(2, 0m);

        var result = TransferService.Transfer(source, destination, Brl(40m), Now);

        var transferred = Assert.IsType<MoneyTransferred>(Assert.Single(source.DomainEvents));
        Assert.Equal(result.Value, transferred);
        Assert.Empty(destination.DomainEvents);
    }

    [Fact]
    public void Transfer_WithInsufficientFunds_ChangesNothing()
    {
        var source = Funded(1, 10m);
        var destination = Funded(2, 0m);

        var result = TransferService.Transfer(source, destination, Brl(40m), Now);

        Assert.Equal("account.insufficient_funds", result.Error?.Code);
        Assert.Equal(Brl(10m), source.Balance);
        Assert.True(destination.Balance.IsZero);
        Assert.Empty(destination.Transactions);
    }

    [Fact]
    public void Transfer_ToBlockedAccount_DoesNotDebitSource()
    {
        var source = Funded(1, 100m);
        var destination = Funded(2, 0m);
        destination.Block();

        var result = TransferService.Transfer(source, destination, Brl(40m), Now);

        Assert.Equal("account.not_active", result.Error?.Code);
        Assert.Equal(Brl(100m), source.Balance);
    }

    [Fact]
    public void Transfer_ToAccountInOtherCurrency_DoesNotDebitSource()
    {
        var source = Funded(1, 100m);
        var destination = Funded(2, 0m, "USD");

        var result = TransferService.Transfer(source, destination, Brl(40m), Now);

        Assert.Equal("account.currency_mismatch", result.Error?.Code);
        Assert.Equal(Brl(100m), source.Balance);
    }

    [Fact]
    public void Transfer_ToSameAccount_IsRejected()
    {
        var account = Funded(1, 100m);

        var result = TransferService.Transfer(account, account, Brl(10m), Now);

        Assert.Equal("account.same_account", result.Error?.Code);
        Assert.Equal(Brl(100m), account.Balance);
    }
}
