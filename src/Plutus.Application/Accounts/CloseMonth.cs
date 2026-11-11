using System.Globalization;
using Microsoft.Extensions.Options;
using Plutus.Application.Abstractions;
using Plutus.Domain.Accounts;
using Plutus.Domain.Common;

namespace Plutus.Application.Accounts;

/// <summary>Escolhe a regra de fim de mês de cada tipo de conta, com os valores da configuração.</summary>
/// <remarks>É o único lugar que conhece todas as estratégias. O caso de uso só pede a regra do tipo da conta.</remarks>
public sealed class MonthlyPolicyCatalog(IOptions<LedgerOptions> options)
{
    public IMonthlyPolicy For(AccountType type)
    {
        var settings = options.Value;
        return type switch
        {
            AccountType.Checking => new CheckingAccountFee(
                Money.Create(settings.CheckingMonthlyFee, settings.DefaultCurrency),
                Money.Create(settings.CheckingFeeExemptBalance, settings.DefaultCurrency)),
            AccountType.Savings => new SavingsInterest(settings.SavingsMonthlyRate),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Tipo de conta sem regra de fim de mês."),
        };
    }
}

public enum ClosingOutcome
{
    /// <summary>Tarifa lançada.</summary>
    Charged,

    /// <summary>Rendimento lançado.</summary>
    Credited,

    /// <summary>Nada a lançar: conta corrente isenta ou poupança sem saldo.</summary>
    NothingToPost,

    /// <summary>A tarifa não coube no saldo nem no limite. Nada foi gravado; o mês pode ser fechado de novo depois.</summary>
    Rejected,

    /// <summary>Este mês já tinha sido fechado para a conta. Nada foi lançado de novo.</summary>
    AlreadyClosed,
}

/// <summary>Uma linha do fechamento. O tipo fica vazio só quando a conta não pôde ser lida (conflito).</summary>
public sealed record MonthClosingLine(string Number, AccountType? Type, ClosingOutcome Outcome, decimal Amount, string Detail);

public sealed record MonthClosing(string Month, IReadOnlyList<MonthClosingLine> Lines);

/// <summary>
/// Fecha um mês: aplica a regra de cada conta ativa (tarifa na corrente, rendimento na poupança).
/// </summary>
/// <remarks>
/// <para>
/// Idempotente: cada conta fechada no mês ganha um registro em <c>monthly_closing</c>, na mesma transação do
/// lançamento. Rodar de novo não cobra nem paga duas vezes; só fecha as contas que ficaram para trás.
/// </para>
/// <para>
/// Cada conta tem a própria transação: um conflito de concorrência numa conta não desfaz o fechamento das outras.
/// A regra usa o saldo do momento do fechamento, e só aceita meses que já terminaram.
/// </para>
/// </remarks>
public sealed class CloseMonth(
    IAccountRepository accounts,
    IUnitOfWork unitOfWork,
    MonthlyPolicyCatalog policies,
    TimeProvider clock)
{
    public async Task<Result<MonthClosing>> HandleAsync(int year, int month, CancellationToken cancellationToken = default)
    {
        if (year is < 2000 or > 9999 || month is < 1 or > 12)
        {
            return Result<MonthClosing>.Failure(ApplicationErrors.InvalidInput($"Mês inválido: {month:00}/{year}."));
        }

        var key = string.Create(CultureInfo.InvariantCulture, $"{year:0000}-{month:00}");
        var nextMonth = new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1);
        var now = clock.GetUtcNow();
        if (now < nextMonth)
        {
            return Result<MonthClosing>.Failure(ApplicationErrors.MonthNotFinished(key));
        }

        var lines = new List<MonthClosingLine>();
        foreach (var number in await accounts.GetActiveNumbersAsync(cancellationToken))
        {
            try
            {
                lines.Add(await unitOfWork.ExecuteAsync(token => CloseAccountAsync(number, key, now, token), cancellationToken));
            }
            catch (ConcurrencyConflictException)
            {
                lines.Add(new MonthClosingLine(number.Value, null, ClosingOutcome.Rejected, 0m,
                    ApplicationErrors.ConcurrencyConflict().Message));
            }
        }

        return Result<MonthClosing>.Success(new MonthClosing(key, lines));
    }

    private async Task<MonthClosingLine> CloseAccountAsync(
        AccountNumber number,
        string month,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var account = await accounts.GetByNumberAsync(number, cancellationToken)
            ?? throw new InvalidOperationException($"A conta {number} sumiu durante o fechamento.");

        if (await accounts.IsMonthClosedAsync(account.Id, month, cancellationToken))
        {
            return Line(account, ClosingOutcome.AlreadyClosed, 0m, "Mês já fechado para esta conta.");
        }

        var charge = policies.For(account.Type).Calculate(account);
        if (charge is null)
        {
            await accounts.RegisterClosingAsync(account.Id, month, ClosingOutcome.NothingToPost.ToString(), now, cancellationToken);
            return Line(account, ClosingOutcome.NothingToPost, 0m, "Nada a lançar.");
        }

        var applied = charge.Type == TransactionType.Fee
            ? account.ChargeFee(charge.Amount, now)
            : account.CreditInterest(charge.Amount, now);
        if (applied.IsFailure)
        {
            return Line(account, ClosingOutcome.Rejected, charge.Amount.Amount, applied.Error!.Message);
        }

        var outcome = charge.Type == TransactionType.Fee ? ClosingOutcome.Charged : ClosingOutcome.Credited;
        await accounts.UpdateAsync(account, cancellationToken);
        await accounts.RegisterClosingAsync(account.Id, month, outcome.ToString(), now, cancellationToken);
        return Line(account, outcome, charge.Amount.Amount, charge.Reason);
    }

    private static MonthClosingLine Line(Account account, ClosingOutcome outcome, decimal amount, string detail) =>
        new(account.Number.Value, account.Type, outcome, amount, detail);
}
