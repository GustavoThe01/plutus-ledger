using Microsoft.Extensions.Options;
using NSubstitute;
using Plutus.Application.Abstractions;
using Plutus.Application.Accounts;
using Plutus.Domain.Accounts;

namespace Plutus.Application.Tests.Accounts;

public class CloseMonthTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 3, 0, 0, TimeSpan.Zero);

    private readonly IAccountRepository _accounts = Substitute.For<IAccountRepository>();
    private readonly CancellationToken _token = TestContext.Current.CancellationToken;
    private readonly CloseMonth _useCase;

    public CloseMonthTests()
    {
        var catalog = new MonthlyPolicyCatalog(Options.Create(new LedgerOptions
        {
            CheckingMonthlyFee = 10m,
            CheckingFeeExemptBalance = 1_000m,
            SavingsMonthlyRate = 0.01m,
        }));
        _useCase = new CloseMonth(_accounts, new PassThrough(), catalog, new Clock(Now));
    }

    private Account Existing(int sequence, AccountType type, decimal balance)
    {
        var account = Account.Open(AccountNumber.FromSequence(sequence), "Titular " + sequence, Money.Zero("BRL"), type);
        if (balance > 0m)
        {
            account.Deposit(Money.Create(balance, "BRL"), Now.AddDays(-10));
        }

        _accounts.GetByNumberAsync(account.Number, Arg.Any<CancellationToken>()).Returns(account);
        return account;
    }

    [Theory]
    [InlineData(2026, 11)] // mês corrente
    [InlineData(2026, 12)] // mês futuro
    public async Task MonthThatHasNotEnded_IsRefused(int year, int month)
    {
        var result = await _useCase.HandleAsync(year, month, _token);

        Assert.Equal("application.month_not_finished", result.Error?.Code);
        await _accounts.DidNotReceive().GetActiveNumbersAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(2026, 0)]
    [InlineData(2026, 13)]
    [InlineData(1999, 5)]
    public async Task InvalidMonth_IsRefused(int year, int month)
    {
        var result = await _useCase.HandleAsync(year, month, _token);

        Assert.Equal("application.invalid_input", result.Error?.Code);
    }

    [Fact]
    public async Task EachTypeGetsItsOwnRule()
    {
        var checking = Existing(1, AccountType.Checking, 100m);
        var richChecking = Existing(2, AccountType.Checking, 2_000m);
        var savings = Existing(3, AccountType.Savings, 500m);
        _accounts.GetActiveNumbersAsync(Arg.Any<CancellationToken>()).Returns([checking.Number, richChecking.Number, savings.Number]);

        var result = await _useCase.HandleAsync(2026, 10, _token);

        Assert.Equal("2026-10", result.Value.Month);
        Assert.Equal(
            [ClosingOutcome.Charged, ClosingOutcome.NothingToPost, ClosingOutcome.Credited],
            result.Value.Lines.Select(l => l.Outcome));
        Assert.Equal(90m, checking.Balance.Amount);
        Assert.Equal(2_000m, richChecking.Balance.Amount);
        Assert.Equal(505m, savings.Balance.Amount);
        await _accounts.Received(2).UpdateAsync(Arg.Any<Account>(), Arg.Any<CancellationToken>());
        await _accounts.Received(3).RegisterClosingAsync(Arg.Any<AccountId>(), "2026-10", Arg.Any<string>(), Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AlreadyClosed_PostsNothing()
    {
        var checking = Existing(1, AccountType.Checking, 100m);
        _accounts.GetActiveNumbersAsync(Arg.Any<CancellationToken>()).Returns([checking.Number]);
        _accounts.IsMonthClosedAsync(checking.Id, "2026-10", Arg.Any<CancellationToken>()).Returns(true);

        var result = await _useCase.HandleAsync(2026, 10, _token);

        Assert.Equal(ClosingOutcome.AlreadyClosed, Assert.Single(result.Value.Lines).Outcome);
        Assert.Equal(100m, checking.Balance.Amount);
        await _accounts.DidNotReceive().UpdateAsync(Arg.Any<Account>(), Arg.Any<CancellationToken>());
        await _accounts.DidNotReceive().RegisterClosingAsync(
            Arg.Any<AccountId>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FeeWithoutFunds_IsRejected_AndTheMonthStaysOpenForThatAccount()
    {
        var broke = Existing(1, AccountType.Checking, 0m);
        _accounts.GetActiveNumbersAsync(Arg.Any<CancellationToken>()).Returns([broke.Number]);

        var result = await _useCase.HandleAsync(2026, 10, _token);

        var line = Assert.Single(result.Value.Lines);
        Assert.Equal(ClosingOutcome.Rejected, line.Outcome);
        Assert.Contains("Saldo insuficiente", line.Detail, StringComparison.Ordinal);
        await _accounts.DidNotReceive().RegisterClosingAsync(
            Arg.Any<AccountId>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConflictOnOneAccount_DoesNotStopTheOthers()
    {
        var first = Existing(1, AccountType.Checking, 100m);
        var second = Existing(2, AccountType.Savings, 100m);
        _accounts.GetActiveNumbersAsync(Arg.Any<CancellationToken>()).Returns([first.Number, second.Number]);
        _accounts.UpdateAsync(first, Arg.Any<CancellationToken>()).Returns(_ => throw new ConcurrencyConflictException());

        var result = await _useCase.HandleAsync(2026, 10, _token);

        Assert.Equal([ClosingOutcome.Rejected, ClosingOutcome.Credited], result.Value.Lines.Select(l => l.Outcome));
        Assert.Null(result.Value.Lines[0].Type);
    }

    private sealed class PassThrough : IUnitOfWork
    {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken) =>
            work(cancellationToken);
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
