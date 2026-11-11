using Plutus.Domain.Common;

namespace Plutus.Application.Accounts;

/// <summary>Falhas que nascem na orquestração, antes de chegar às regras do domínio.</summary>
public static class ApplicationErrors
{
    public static Error InvalidAccountNumber(string number) =>
        new("application.invalid_account_number", $"Número de conta inválido: '{number}'.");

    public static Error AccountNotFound(string number) =>
        new("application.account_not_found", $"Conta {number} não encontrada.");

    public static Error InvalidInput(string message) =>
        new("application.invalid_input", message);

    public static Error OverdraftLimitTooHigh(decimal limit, decimal maximum) =>
        new("application.overdraft_limit_too_high", $"O limite {limit} passa do máximo permitido ({maximum}).");

    public static Error ConcurrencyConflict() =>
        new("application.concurrency_conflict", "A conta foi alterada por outra operação ao mesmo tempo. Nada foi gravado; tente de novo.");

    public static Error MonthNotFinished(string month) =>
        new("application.month_not_finished", $"O mês {month} ainda não terminou; só dá para fechar um mês encerrado.");

    public static Error StatementPeriodTooLong(int maximumDays) =>
        new("application.statement_period_too_long", $"O extrato pode cobrir no máximo {maximumDays} dias.");
}
