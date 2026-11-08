using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Plutus.Application;
using Plutus.Application.Accounts;
using Plutus.Infrastructure;
using Plutus.Infrastructure.Persistence;
using Spectre.Console.Testing;

namespace Plutus.Cli.Tests;

/// <summary>
/// Sessões completas no console: o teclado é simulado pelo <see cref="TestConsole"/>, e os casos de uso rodam
/// de verdade contra um SQLite em memória com as migrations reais.
/// </summary>
public sealed class LedgerConsoleTests : IAsyncLifetime
{
    private readonly string _connectionString = $"Data Source=cli-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly CancellationToken _token = TestContext.Current.CancellationToken;
    private readonly TestConsole _console = new();
    private SqliteConnection _keepAlive = null!;
    private ServiceProvider _provider = null!;

    public async ValueTask InitializeAsync()
    {
        _keepAlive = new SqliteConnection(_connectionString);
        await _keepAlive.OpenAsync(_token);
        await DatabaseMigrator.MigrateAsync(_connectionString, _token);

        var services = new ServiceCollection();
        services.AddApplication();
        services.AddInfrastructure(_connectionString);
        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        _console.Interactive();
        _console.Profile.Width = 160;
    }

    public async ValueTask DisposeAsync()
    {
        _console.Dispose();
        await _provider.DisposeAsync();
        await _keepAlive.DisposeAsync();
    }

    private Task RunAsync() =>
        new LedgerConsole(_console, _provider.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System,
            NullLogger<LedgerConsole>.Instance).RunAsync(_token);

    /// <summary>Escolhe uma opção do menu descendo com a seta, na ordem do enum.</summary>
    private void Choose(LedgerConsole.MenuOption option)
    {
        for (var i = 0; i < (int)option; i++)
        {
            _console.Input.PushKey(ConsoleKey.DownArrow);
        }

        _console.Input.PushKey(ConsoleKey.Enter);
    }

    private void Type(params string[] lines)
    {
        foreach (var line in lines)
        {
            _console.Input.PushTextWithEnter(line);
        }
    }

    private async Task<string> OpenAccountAsync(string holder, decimal limit = 0m)
    {
        await using var scope = _provider.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<OpenAccount>().HandleAsync(holder, limit, _token);
        return result.Value.Value;
    }

    [Fact]
    public async Task OpenAccount_ShowsTheNewNumber()
    {
        Choose(LedgerConsole.MenuOption.OpenAccount);
        Type("Ana Lima", "500");
        Choose(LedgerConsole.MenuOption.Exit);

        await RunAsync();

        Assert.Contains("✓ Conta 000001-", _console.Output, StringComparison.Ordinal);
        Assert.Contains("aberta para Ana Lima", _console.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deposit_AcceptsCommaAsDecimalSeparator_AndShowsTheBalance()
    {
        var number = await OpenAccountAsync("Ana Lima", 100m);

        Choose(LedgerConsole.MenuOption.Deposit);
        Type(number, "150,50");
        Choose(LedgerConsole.MenuOption.Exit);

        await RunAsync();

        Assert.Contains("✓ Depósito de R$ 150,50 concluído.", _console.Output, StringComparison.Ordinal);
        Assert.Contains("R$ 250,50", _console.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidAmount_IsAskedAgain()
    {
        var number = await OpenAccountAsync("Ana Lima");

        Choose(LedgerConsole.MenuOption.Deposit);
        Type(number, "abc", "-10", "10");
        Choose(LedgerConsole.MenuOption.Exit);

        await RunAsync();

        Assert.Contains("Digite um valor como 150,00.", _console.Output, StringComparison.Ordinal);
        Assert.Contains("O valor precisa ser maior que zero.", _console.Output, StringComparison.Ordinal);
        Assert.Contains("✓ Depósito de R$ 10,00 concluído.", _console.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BusinessRuleFailure_ShowsTheMessage_AndTheMenuContinues()
    {
        var number = await OpenAccountAsync("Ana Lima");

        Choose(LedgerConsole.MenuOption.Withdraw);
        Type(number, "50");
        Choose(LedgerConsole.MenuOption.ShowAccount);
        Type(Plutus.Domain.Accounts.AccountNumber.FromSequence(999).Value);
        Choose(LedgerConsole.MenuOption.Exit);

        await RunAsync();

        Assert.Contains("✗ Saldo insuficiente", _console.Output, StringComparison.Ordinal);
        Assert.Contains("✗ Conta 000999-", _console.Output, StringComparison.Ordinal);
        Assert.Contains("Até logo.", _console.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Transfer_AsksForConfirmation()
    {
        var ana = await OpenAccountAsync("Ana Lima");
        var bruno = await OpenAccountAsync("Bruno Souza");

        Choose(LedgerConsole.MenuOption.Deposit);
        Type(ana, "100");
        Choose(LedgerConsole.MenuOption.Transfer);
        Type(ana, bruno, "30", "n");
        Choose(LedgerConsole.MenuOption.Transfer);
        Type(ana, bruno, "30", "s");
        Choose(LedgerConsole.MenuOption.Exit);

        await RunAsync();

        Assert.Contains("Transferência cancelada.", _console.Output, StringComparison.Ordinal);
        Assert.Contains("✓ Transferência de R$ 30,00 concluída.", _console.Output, StringComparison.Ordinal);
        Assert.Contains("R$ 70,00", _console.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Statement_And_MonthlySummary_ShowTheEntries()
    {
        var number = await OpenAccountAsync("Ana Lima");

        Choose(LedgerConsole.MenuOption.Deposit);
        Type(number, "200");
        Choose(LedgerConsole.MenuOption.Withdraw);
        Type(number, "50");
        Choose(LedgerConsole.MenuOption.Statement);
        Type(number, "30");
        Choose(LedgerConsole.MenuOption.MonthlySummary);
        Type(number);
        Choose(LedgerConsole.MenuOption.Exit);

        await RunAsync();

        Assert.Contains("Depósito", _console.Output, StringComparison.Ordinal);
        Assert.Contains("Saque", _console.Output, StringComparison.Ordinal);
        Assert.Contains("-R$ 50,00", _console.Output, StringComparison.Ordinal);
        Assert.Contains("Página 1 de 1 · 2 lançamentos", _console.Output, StringComparison.Ordinal);
        Assert.Contains("R$ 150,00", _console.Output, StringComparison.Ordinal);
    }
}
