using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Plutus.Application;
using Plutus.Application.Abstractions;
using Plutus.Application.Accounts;
using Plutus.Domain.Accounts;
using Plutus.Infrastructure;
using Plutus.Infrastructure.Persistence;

namespace Plutus.IntegrationTests.Persistence;

/// <summary>Casos de uso de verdade contra um SQLite em memória, com as migrations reais.</summary>
public sealed class LedgerPersistenceTests : IAsyncLifetime
{
    private readonly string _connectionString = $"Data Source=ledger-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly CancellationToken _token = TestContext.Current.CancellationToken;
    private SqliteConnection _keepAlive = null!;
    private ServiceProvider _provider = null!;

    public async ValueTask InitializeAsync()
    {
        // Um banco em memória só existe enquanto houver uma conexão aberta com ele.
        _keepAlive = new SqliteConnection(_connectionString);
        await _keepAlive.OpenAsync(_token);
        await DatabaseMigrator.MigrateAsync(_connectionString, _token);

        var services = new ServiceCollection();
        services.AddApplication();
        services.AddInfrastructure(_connectionString);
        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _keepAlive.DisposeAsync();
    }

    private async Task<T> InScope<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = _provider.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    private async Task<string> OpenAccountAsync(string holder = "Ana Lima", decimal limit = 0m)
    {
        var result = await InScope(s => s.GetRequiredService<OpenAccount>().HandleAsync(holder, limit, _token));
        return result.Value.Value;
    }

    private Task DepositAsync(string number, decimal amount) =>
        InScope(s => s.GetRequiredService<DepositMoney>().HandleAsync(number, amount, _token));

    private async Task<Money> BalanceAsync(string number)
    {
        var account = await InScope(s => s.GetRequiredService<IAccountRepository>().GetByNumberAsync(AccountNumber.Parse(number), _token));
        return account!.Balance;
    }

    private static Money Brl(decimal amount) => Money.Create(amount, "BRL");

    [Fact]
    public async Task Migrator_RunsOnlyOnce()
    {
        var secondRun = await DatabaseMigrator.MigrateAsync(_connectionString, _token);

        Assert.Empty(secondRun);
    }

    [Fact]
    public async Task OpenAccount_GeneratesSequentialNumbers()
    {
        var first = await OpenAccountAsync();
        var second = await OpenAccountAsync();

        Assert.Equal(AccountNumber.FromSequence(1).Value, first);
        Assert.Equal(AccountNumber.FromSequence(2).Value, second);
    }

    [Fact]
    public async Task Account_RoundTrip_KeepsAllFields()
    {
        var number = await OpenAccountAsync("Bruno Souza", limit: 250.50m);

        var account = await InScope(s => s.GetRequiredService<IAccountRepository>().GetByNumberAsync(AccountNumber.Parse(number), _token));

        Assert.Equal("Bruno Souza", account!.HolderName);
        Assert.Equal(Brl(250.50m), account.OverdraftLimit);
        Assert.Equal(AccountStatus.Active, account.Status);
        Assert.Empty(account.Transactions);
    }

    [Fact]
    public async Task DepositAndWithdraw_AreExact_AndStoredInCents()
    {
        var number = await OpenAccountAsync();

        await DepositAsync(number, 0.10m);
        await DepositAsync(number, 0.20m);

        Assert.Equal(Brl(0.30m), await BalanceAsync(number));
        Assert.Equal(30L, await _keepAlive.ExecuteScalarAsync<long>("SELECT balance_cents FROM account"));
    }

    [Fact]
    public async Task Transfer_UpdatesBothAccounts_AndWritesBothEntries()
    {
        var source = await OpenAccountAsync();
        var destination = await OpenAccountAsync("Carla Nunes");
        await DepositAsync(source, 100m);

        var result = await InScope(s => s.GetRequiredService<TransferMoney>().HandleAsync(source, destination, 40m, _token));

        Assert.True(result.IsSuccess);
        Assert.Equal(Brl(60m), await BalanceAsync(source));
        Assert.Equal(Brl(40m), await BalanceAsync(destination));
        Assert.Equal(3L, await _keepAlive.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM ledger_transaction"));
    }

    [Fact]
    public async Task Transfer_WithInsufficientFunds_WritesNothing()
    {
        var source = await OpenAccountAsync();
        var destination = await OpenAccountAsync("Carla Nunes");

        var result = await InScope(s => s.GetRequiredService<TransferMoney>().HandleAsync(source, destination, 40m, _token));

        Assert.Equal("account.insufficient_funds", result.Error?.Code);
        Assert.Equal(0L, await _keepAlive.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM ledger_transaction"));
    }

    [Fact]
    public async Task Exception_InTheMiddle_RollsBackEverything()
    {
        var number = await OpenAccountAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => InScope<int>(async services =>
        {
            var accounts = services.GetRequiredService<IAccountRepository>();
            return await services.GetRequiredService<IUnitOfWork>().ExecuteAsync<int>(
                async token =>
                {
                    var account = await accounts.GetByNumberAsync(AccountNumber.Parse(number), token);
                    account!.Deposit(Brl(50m), DateTimeOffset.UtcNow);
                    await accounts.UpdateAsync(account, token);
                    throw new InvalidOperationException("Falha depois de gravar.");
                },
                _token);
        }));

        Assert.True((await BalanceAsync(number)).IsZero);
        Assert.Equal(0L, await _keepAlive.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM ledger_transaction"));
    }

    [Fact]
    public async Task FailureResult_RollsBackWhatWasWritten()
    {
        var number = await OpenAccountAsync();

        var result = await InScope(async services =>
        {
            var accounts = services.GetRequiredService<IAccountRepository>();
            return await services.GetRequiredService<IUnitOfWork>().ExecuteAsync(
                async token =>
                {
                    var account = await accounts.GetByNumberAsync(AccountNumber.Parse(number), token);
                    account!.Deposit(Brl(50m), DateTimeOffset.UtcNow);
                    await accounts.UpdateAsync(account, token);
                    return Domain.Common.Result.Failure(new Domain.Common.Error("teste", "Falha de negócio."));
                },
                _token);
        });

        Assert.True(result.IsFailure);
        Assert.True((await BalanceAsync(number)).IsZero);
    }

    [Fact]
    public async Task Statement_FiltersByPeriod_InOrder()
    {
        var number = await OpenAccountAsync();
        await DepositAsync(number, 10m);
        await DepositAsync(number, 20m);
        var now = DateTimeOffset.UtcNow;

        var inside = await InScope(s => s.GetRequiredService<GetStatement>().HandleAsync(number, now.AddMinutes(-5), now.AddMinutes(5), _token));
        var before = await InScope(s => s.GetRequiredService<GetStatement>().HandleAsync(number, now.AddDays(-2), now.AddDays(-1), _token));

        Assert.Equal([Brl(10m), Brl(30m)], inside.Value.Select(t => t.BalanceAfter));
        Assert.Equal(TransactionType.Deposit, inside.Value[0].Type);
        Assert.Empty(before.Value);
    }

    [Fact]
    public async Task Database_RejectsBalanceBeyondTheLimit()
    {
        await OpenAccountAsync(limit: 100m);

        await Assert.ThrowsAsync<SqliteException>(() => _keepAlive.ExecuteAsync("UPDATE account SET balance_cents = -10001"));
    }

    [Fact]
    public void Restore_RejectsCorruptedData()
    {
        Assert.Throws<Domain.Common.DomainException>(() => Account.Restore(
            AccountId.New(), AccountNumber.FromSequence(1), "Ana Lima", Brl(-10m), Brl(5m), AccountStatus.Active));
    }
}
