using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Plutus.Application.Abstractions;
using Plutus.Application.Accounts;
using Plutus.Domain.Accounts;

namespace Plutus.Application.Tests.Accounts;

public class AccountUseCasesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    private readonly IAccountRepository _accounts = Substitute.For<IAccountRepository>();
    private readonly PassThroughUnitOfWork _unitOfWork = new();
    private readonly FixedClock _clock = new(Now);
    private readonly IOptions<LedgerOptions> _options = Options.Create(new LedgerOptions { MaxOverdraftLimit = 1_000m, MaxStatementDays = 31 });
    private readonly CancellationToken _token = TestContext.Current.CancellationToken;

    private Account ExistingAccount(int sequence, decimal balance = 0m)
    {
        var account = Account.Open(AccountNumber.FromSequence(sequence), "Titular " + sequence, Money.Zero("BRL"));
        if (balance > 0m)
        {
            account.Deposit(Money.Create(balance, "BRL"), Now);
        }

        _accounts.GetByNumberAsync(account.Number, Arg.Any<CancellationToken>()).Returns(account);
        return account;
    }

    [Fact]
    public async Task OpenAccount_UsesNextSequence_AndSaves()
    {
        _accounts.NextSequenceAsync(Arg.Any<CancellationToken>()).Returns(42);
        var useCase = new OpenAccount(_accounts, _unitOfWork, _options);

        var result = await useCase.HandleAsync("Ana Lima", 100m, _token);

        Assert.Equal(AccountNumber.FromSequence(42), result.Value);
        await _accounts.Received(1).AddAsync(
            Arg.Is<Account>(a => a.HolderName == "Ana Lima" && a.Currency == "BRL"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OpenAccount_AboveMaximumLimit_DoesNotTouchTheDatabase()
    {
        var useCase = new OpenAccount(_accounts, _unitOfWork, _options);

        var result = await useCase.HandleAsync("Ana Lima", 1_000.01m, _token);

        Assert.Equal("application.overdraft_limit_too_high", result.Error?.Code);
        Assert.Equal(0, _unitOfWork.Executions);
    }

    [Fact]
    public async Task OpenAccount_InvalidHolderName_BecomesFailureResult()
    {
        _accounts.NextSequenceAsync(Arg.Any<CancellationToken>()).Returns(1);
        var useCase = new OpenAccount(_accounts, _unitOfWork, _options);

        var result = await useCase.HandleAsync("Al", 0m, _token);

        Assert.Equal("application.invalid_input", result.Error?.Code);
        await _accounts.DidNotReceive().AddAsync(Arg.Any<Account>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Deposit_UpdatesAccount_UsingTheClock()
    {
        var account = ExistingAccount(1);
        var useCase = new DepositMoney(_accounts, _unitOfWork, _clock, _options);

        var result = await useCase.HandleAsync(account.Number.Value, 50m, _token);

        Assert.True(result.IsSuccess);
        Assert.Equal(Money.Create(50m, "BRL"), account.Balance);
        Assert.Equal(Now, account.Transactions[^1].OccurredAt);
        await _accounts.Received(1).UpdateAsync(account, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("123")]
    [InlineData("123456-0")]
    public async Task Deposit_InvalidNumber_IsRejectedBeforeTheTransaction(string number)
    {
        var useCase = new DepositMoney(_accounts, _unitOfWork, _clock, _options);

        var result = await useCase.HandleAsync(number, 50m, _token);

        Assert.Equal("application.invalid_account_number", result.Error?.Code);
        Assert.Equal(0, _unitOfWork.Executions);
    }

    [Fact]
    public async Task Deposit_MoreThanTwoDecimals_BecomesFailureResult()
    {
        var account = ExistingAccount(1);
        var useCase = new DepositMoney(_accounts, _unitOfWork, _clock, _options);

        var result = await useCase.HandleAsync(account.Number.Value, 1.005m, _token);

        Assert.Equal("application.invalid_input", result.Error?.Code);
    }

    [Fact]
    public async Task Deposit_UnknownAccount_IsNotFound()
    {
        var useCase = new DepositMoney(_accounts, _unitOfWork, _clock, _options);

        var result = await useCase.HandleAsync(AccountNumber.FromSequence(9).Value, 50m, _token);

        Assert.Equal("application.account_not_found", result.Error?.Code);
    }

    [Fact]
    public async Task Withdraw_InsufficientFunds_DoesNotSave()
    {
        var account = ExistingAccount(1, balance: 10m);
        var useCase = new WithdrawMoney(_accounts, _unitOfWork, _clock, _options);

        var result = await useCase.HandleAsync(account.Number.Value, 50m, _token);

        Assert.Equal("account.insufficient_funds", result.Error?.Code);
        await _accounts.DidNotReceive().UpdateAsync(Arg.Any<Account>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Transfer_SavesBothAccounts_InOneTransaction()
    {
        var source = ExistingAccount(1, balance: 100m);
        var destination = ExistingAccount(2);
        var useCase = new TransferMoney(_accounts, _unitOfWork, _clock, _options);

        var result = await useCase.HandleAsync(source.Number.Value, destination.Number.Value, 40m, _token);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, _unitOfWork.Executions);
        await _accounts.Received(1).UpdateAsync(source, Arg.Any<CancellationToken>());
        await _accounts.Received(1).UpdateAsync(destination, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Transfer_DomainFailure_SavesNothing()
    {
        var source = ExistingAccount(1, balance: 10m);
        var destination = ExistingAccount(2);
        var useCase = new TransferMoney(_accounts, _unitOfWork, _clock, _options);

        var result = await useCase.HandleAsync(source.Number.Value, destination.Number.Value, 40m, _token);

        Assert.Equal("account.insufficient_funds", result.Error?.Code);
        await _accounts.DidNotReceive().UpdateAsync(Arg.Any<Account>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Transfer_UnknownDestination_IsNotFound()
    {
        var source = ExistingAccount(1, balance: 100m);
        var useCase = new TransferMoney(_accounts, _unitOfWork, _clock, _options);

        var result = await useCase.HandleAsync(source.Number.Value, AccountNumber.FromSequence(9).Value, 40m, _token);

        Assert.Equal("application.account_not_found", result.Error?.Code);
        Assert.Equal(Money.Create(100m, "BRL"), source.Balance);
    }

    [Fact]
    public async Task GetStatement_ReturnsTransactionsOfThePeriod()
    {
        var account = ExistingAccount(1, balance: 100m);
        _accounts.GetTransactionsAsync(account.Id, Now.AddDays(-7), Now, Arg.Any<CancellationToken>())
            .Returns(account.Transactions);
        var useCase = new GetStatement(_accounts, _options);

        var result = await useCase.HandleAsync(account.Number.Value, Now.AddDays(-7), Now, _token);

        Assert.Single(result.Value);
    }

    [Fact]
    public async Task GetStatement_RejectsInvertedOrTooLongPeriod()
    {
        var account = ExistingAccount(1);
        var useCase = new GetStatement(_accounts, _options);

        var inverted = await useCase.HandleAsync(account.Number.Value, Now, Now.AddDays(-1), _token);
        var tooLong = await useCase.HandleAsync(account.Number.Value, Now.AddDays(-32), Now, _token);

        Assert.Equal("application.invalid_input", inverted.Error?.Code);
        Assert.Equal("application.statement_period_too_long", tooLong.Error?.Code);
    }

    [Fact]
    public async Task AddApplication_ResolvesAllUseCases_WithScopedLifetime()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_accounts);
        services.AddSingleton<IUnitOfWork>(_unitOfWork);
        services.AddApplication();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using var scope = provider.CreateAsyncScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<OpenAccount>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<DepositMoney>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<WithdrawMoney>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<TransferMoney>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<GetStatement>());
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<TransferMoney>());
    }

    [Fact]
    public void InvalidOptions_AreRejected()
    {
        var services = new ServiceCollection();
        services.AddApplication();
        services.Configure<LedgerOptions>(options => options.DefaultCurrency = "REAL");
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<LedgerOptions>>().Value);
    }

    private sealed class PassThroughUnitOfWork : IUnitOfWork
    {
        public int Executions { get; private set; }

        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken)
        {
            Executions++;
            return work(cancellationToken);
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
