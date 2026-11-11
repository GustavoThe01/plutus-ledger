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

/// <summary>Fechamento do mês contra o SQLite de verdade, e a migration V003 sobre um banco já em uso.</summary>
public sealed class MonthlyClosingTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 3, 0, 0, TimeSpan.Zero);

    private readonly string _connectionString = $"Data Source=closing-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly CancellationToken _token = TestContext.Current.CancellationToken;
    private SqliteConnection _keepAlive = null!;
    private ServiceProvider _provider = null!;

    public async ValueTask InitializeAsync()
    {
        _keepAlive = new SqliteConnection(_connectionString);
        await _keepAlive.OpenAsync(_token);
    }

    public async ValueTask DisposeAsync()
    {
        if (_provider is not null)
        {
            await _provider.DisposeAsync();
        }

        await _keepAlive.DisposeAsync();
    }

    private async Task StartAsync()
    {
        await DatabaseMigrator.MigrateAsync(_connectionString, _token);

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new Clock(Now)); // o AddApplication não troca um relógio já registrado
        services.AddApplication();
        services.AddInfrastructure(_connectionString);
        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private async Task<T> InScope<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = _provider.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    private async Task<string> OpenAsync(AccountType type, decimal deposit)
    {
        var number = (await InScope(s => s.GetRequiredService<OpenAccount>().HandleAsync("Ana Lima", 0m, type, _token))).Value.Value;
        if (deposit > 0m)
        {
            await InScope(s => s.GetRequiredService<DepositMoney>().HandleAsync(number, deposit, _token));
        }

        return number;
    }

    private Task<MonthClosing> CloseAsync(int year = 2026, int month = 10) =>
        InScope(async s => (await s.GetRequiredService<CloseMonth>().HandleAsync(year, month, _token)).Value);

    private async Task<Account> LoadAsync(string number) =>
        (await InScope(s => s.GetRequiredService<IAccountRepository>().GetByNumberAsync(AccountNumber.Parse(number), _token)))!;

    [Fact]
    public async Task ClosingTwice_ChargesAndPaysOnlyOnce()
    {
        await StartAsync();
        var checking = await OpenAsync(AccountType.Checking, 100m);
        var savings = await OpenAsync(AccountType.Savings, 1_000m);

        var first = await CloseAsync();
        var second = await CloseAsync();

        Assert.Equal([ClosingOutcome.Charged, ClosingOutcome.Credited], first.Lines.Select(l => l.Outcome));
        Assert.All(second.Lines, l => Assert.Equal(ClosingOutcome.AlreadyClosed, l.Outcome));
        Assert.Equal(87.10m, (await LoadAsync(checking)).Balance.Amount);
        Assert.Equal(1_005m, (await LoadAsync(savings)).Balance.Amount);
    }

    [Fact]
    public async Task FeeAndInterest_ShowUpInTheStatement()
    {
        await StartAsync();
        var savings = await OpenAsync(AccountType.Savings, 1_000m);
        await CloseAsync();

        var statement = await InScope(s => s.GetRequiredService<GetStatement>()
            .HandleAsync(savings, Now.AddDays(-1), Now.AddDays(1), cancellationToken: _token));

        var interest = statement.Value.Items[^1];
        Assert.Equal("Rendimento da poupança", interest.Description);
        Assert.Equal(5m, interest.Amount);
    }

    [Fact]
    public async Task RejectedFee_CanBeChargedWhenTheMonthIsClosedAgain()
    {
        await StartAsync();
        var broke = await OpenAsync(AccountType.Checking, 0m);

        Assert.Equal(ClosingOutcome.Rejected, Assert.Single((await CloseAsync()).Lines).Outcome);

        await InScope(s => s.GetRequiredService<DepositMoney>().HandleAsync(broke, 50m, _token));
        Assert.Equal(ClosingOutcome.Charged, Assert.Single((await CloseAsync()).Lines).Outcome);
        Assert.Equal(37.10m, (await LoadAsync(broke)).Balance.Amount);
    }

    [Fact]
    public async Task OnlyActiveAccountsAreClosed()
    {
        await StartAsync();
        var active = await OpenAsync(AccountType.Savings, 100m);
        var blocked = await OpenAsync(AccountType.Savings, 100m);
        await InScope(async s =>
        {
            var repository = s.GetRequiredService<IAccountRepository>();
            return await s.GetRequiredService<IUnitOfWork>().ExecuteAsync(
                async token =>
                {
                    var account = (await repository.GetByNumberAsync(AccountNumber.Parse(blocked), token))!;
                    account.Block();
                    await repository.UpdateAsync(account, token);
                    return true;
                },
                _token);
        });

        var closing = await CloseAsync();

        Assert.Equal(active, Assert.Single(closing.Lines).Number);
    }

    [Fact]
    public async Task Database_RejectsSavingsWithLimit_AndUnknownTransactionTypes()
    {
        await StartAsync();
        var savings = await OpenAsync(AccountType.Savings, 0m);

        var limit = await Assert.ThrowsAsync<SqliteException>(() =>
            _keepAlive.ExecuteAsync("UPDATE account SET overdraft_limit_cents = 100 WHERE number = @savings", new { savings }));
        var type = await Assert.ThrowsAsync<SqliteException>(() => _keepAlive.ExecuteAsync(
            "INSERT INTO ledger_transaction SELECT 'x', id, 'Bonus', 1, 1, '2026-10-01', NULL FROM account LIMIT 1"));

        Assert.Contains("CHECK", limit.Message, StringComparison.Ordinal);
        Assert.Contains("CHECK", type.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task V003_KeepsTheDataOfADatabaseCreatedBeforeIt()
    {
        // Um banco como ele estava no Dia 09: só V001 e V002 aplicadas, uma conta e dois lançamentos.
        await _keepAlive.ExecuteAsync("CREATE TABLE schema_migration (name TEXT PRIMARY KEY, applied_at TEXT NOT NULL DEFAULT 'antes') STRICT");
        foreach (var name in new[] { "V001__create_schema.sql", "V002__account_version.sql" })
        {
            await _keepAlive.ExecuteAsync(await ReadMigrationAsync(name));
            await _keepAlive.ExecuteAsync("INSERT INTO schema_migration (name) VALUES (@name)", new { name });
        }

        var number = AccountNumber.FromSequence(1).Value;
        await _keepAlive.ExecuteAsync("""
            INSERT INTO account (id, number, holder_name, balance_cents, overdraft_limit_cents, currency, status, version)
            VALUES ('8c1f6c1e-0000-4000-8000-000000000001', @number, 'Conta antiga', 7000, 50000, 'BRL', 'Active', 2);
            INSERT INTO ledger_transaction VALUES ('8c1f6c1e-0000-4000-8000-0000000000a1', '8c1f6c1e-0000-4000-8000-000000000001', 'Deposit', 10000, 10000, '2026-10-01T10:00:00.0000000+00:00', NULL);
            INSERT INTO ledger_transaction VALUES ('8c1f6c1e-0000-4000-8000-0000000000a2', '8c1f6c1e-0000-4000-8000-000000000001', 'Withdrawal', 3000, 7000, '2026-10-02T10:00:00.0000000+00:00', NULL);
            """, new { number });

        var applied = await DatabaseMigrator.MigrateAsync(_connectionString, _token);
        await StartAsync();

        Assert.Equal(["V003__account_type_and_monthly_closing.sql"], applied);
        var account = await LoadAsync(number);
        Assert.Equal(AccountType.Checking, account.Type);
        Assert.Equal(70m, account.Balance.Amount);
        Assert.Equal(500m, account.OverdraftLimit.Amount);
        var types = await _keepAlive.QueryAsync<string>("SELECT type FROM ledger_transaction ORDER BY rowid");
        Assert.Equal(["Deposit", "Withdrawal"], types);
    }

    private static async Task<string> ReadMigrationAsync(string name)
    {
        var assembly = typeof(DatabaseMigrator).Assembly;
        var resource = assembly.GetManifestResourceNames().Single(r => r.EndsWith(".Migrations." + name, StringComparison.Ordinal));
        await using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
