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

/// <summary>
/// Concorrência de verdade: um arquivo SQLite (o banco em memória compartilhado usa outro tipo de trava)
/// e várias operações rodando ao mesmo tempo, cada uma no próprio escopo e na própria conexão.
/// </summary>
public sealed class ConcurrencyTests : IAsyncLifetime
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"plutus-concorrencia-{Guid.NewGuid():N}.db");
    private readonly CancellationToken _token = TestContext.Current.CancellationToken;
    private string _connectionString = null!;
    private ServiceProvider _provider = null!;

    public async ValueTask InitializeAsync()
    {
        // Pooling desligado: cada operação abre a própria conexão, e o arquivo pode ser apagado no fim.
        _connectionString = new SqliteConnectionStringBuilder { DataSource = _file, Pooling = false }.ToString();
        await DatabaseMigrator.MigrateAsync(_connectionString, _token);

        var services = new ServiceCollection();
        services.AddApplication();
        services.AddInfrastructure(_connectionString);
        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        File.Delete(_file);
    }

    private async Task<T> InScope<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = _provider.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    private async Task<string> OpenWithAsync(string holder, decimal deposit)
    {
        var number = (await InScope(s => s.GetRequiredService<OpenAccount>().HandleAsync(holder, 0m, _token))).Value.Value;
        await InScope(s => s.GetRequiredService<DepositMoney>().HandleAsync(number, deposit, _token));
        return number;
    }

    private async Task<T> QueryAsync<T>(string sql, object? parameters = null)
    {
        await using var connection = new SqliteConnection(_connectionString);
        return await connection.ExecuteScalarAsync<T>(sql, parameters) ?? throw new InvalidOperationException(sql);
    }

    [Fact]
    public async Task ParallelTransfers_NeverCreateNorLoseMoney()
    {
        string[] accounts = [await OpenWithAsync("Ana", 1_000m), await OpenWithAsync("Bruno", 1_000m), await OpenWithAsync("Carla", 1_000m)];
        var random = new Random(2026);
        var transfers = Enumerable.Range(0, 300)
            .Select(_ =>
            {
                var from = random.Next(3);
                var to = (from + 1 + random.Next(2)) % 3;
                return (From: accounts[from], To: accounts[to], Amount: random.Next(1, 120));
            })
            .ToList();

        var results = await Task.WhenAll(transfers.Select(t => Task.Run(
            () => InScope(s => s.GetRequiredService<TransferMoney>().HandleAsync(t.From, t.To, t.Amount, _token)),
            _token)));

        // Toda transferência termina: ou concluída, ou recusada por falta de saldo. Nenhuma se perde no meio.
        var succeeded = results.Count(r => r.IsSuccess);
        Assert.All(results.Where(r => r.IsFailure), r => Assert.Equal("account.insufficient_funds", r.Error!.Code));
        Assert.True(succeeded > 200, $"Só {succeeded} de 300 transferências passaram.");

        // O dinheiro total não muda: transferência só move de uma conta para outra.
        Assert.Equal(300_000, await QueryAsync<long>("SELECT SUM(balance_cents) FROM account"));

        // O saldo de cada conta é exatamente a soma dos seus lançamentos: nenhuma atualização foi perdida.
        Assert.Equal(0, await QueryAsync<long>("""
            SELECT COUNT(*) FROM account a
            WHERE a.balance_cents <> (
                SELECT COALESCE(SUM(CASE WHEN t.type IN ('Deposit', 'TransferIn') THEN t.amount_cents ELSE -t.amount_cents END), 0)
                FROM ledger_transaction t WHERE t.account_id = a.id)
            """));

        // Cada transferência concluída gerou dois lançamentos, e cada uma mudou a versão de duas contas.
        Assert.Equal(3 + succeeded * 2, await QueryAsync<long>("SELECT COUNT(*) FROM ledger_transaction"));
        Assert.Equal(3 + succeeded * 2, await QueryAsync<long>("SELECT SUM(version) FROM account"));
    }

    [Fact]
    public async Task StaleWrite_IsRejected_AndTheNewerBalanceSurvives()
    {
        var number = await OpenWithAsync("Ana", 100m);

        // Uma operação lê a conta e demora a gravar...
        await using var slow = _provider.CreateAsyncScope();
        var repository = slow.ServiceProvider.GetRequiredService<IAccountRepository>();
        var stale = (await repository.GetByNumberAsync(AccountNumber.Parse(number), _token))!;

        // ...enquanto outra deposita e confirma antes dela.
        await InScope(s => s.GetRequiredService<DepositMoney>().HandleAsync(number, 50m, _token));

        stale.Deposit(Money.Create(10m, "BRL"), DateTimeOffset.UtcNow);
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => repository.UpdateAsync(stale, _token));

        // Sem a versão, o saldo voltaria para 110 e o depósito de 50 sumiria (lost update).
        var account = await InScope(s => s.GetRequiredService<GetAccount>().HandleAsync(number, _token));
        Assert.Equal(150m, account.Value.Balance);
    }

    [Fact]
    public async Task EveryUpdate_IncrementsTheVersion()
    {
        var number = await OpenWithAsync("Ana", 100m);
        await InScope(s => s.GetRequiredService<WithdrawMoney>().HandleAsync(number, 30m, _token));

        Assert.Equal(2, await QueryAsync<long>("SELECT version FROM account WHERE number = @number", new { number }));
    }

    [Fact]
    public async Task CancelledOperation_WritesNothing()
    {
        var number = await OpenWithAsync("Ana", 100m);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            InScope(s => s.GetRequiredService<DepositMoney>().HandleAsync(number, 999m, cancelled.Token)));

        Assert.Equal(10_000, await QueryAsync<long>("SELECT balance_cents FROM account WHERE number = @number", new { number }));
    }
}
