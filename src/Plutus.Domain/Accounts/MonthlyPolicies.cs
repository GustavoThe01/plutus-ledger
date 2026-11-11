using Plutus.Domain.Common;

namespace Plutus.Domain.Accounts;

public enum AccountType
{
    /// <summary>Conta corrente: pode ter limite e paga tarifa mensal.</summary>
    Checking = 1,

    /// <summary>Poupança: sem limite, rende juros todo mês.</summary>
    Savings = 2,
}

/// <summary>O lançamento que o fechamento do mês gera: uma tarifa (saída) ou um rendimento (entrada).</summary>
public sealed record MonthlyCharge(TransactionType Type, Money Amount, string Reason);

/// <summary>
/// Strategy: a regra do fim do mês muda conforme o tipo de conta, e cada regra é uma classe.
/// </summary>
/// <remarks>
/// Um tipo de conta novo (conta salário, por exemplo) é uma implementação nova, sem mexer nas que existem
/// nem no caso de uso que fecha o mês. Devolve <c>null</c> quando não há nada a lançar.
/// </remarks>
public interface IMonthlyPolicy
{
    MonthlyCharge? Calculate(Account account);
}

/// <summary>Conta corrente: tarifa fixa por mês, isenta para quem mantém um saldo mínimo.</summary>
public sealed class CheckingAccountFee : IMonthlyPolicy
{
    private readonly Money _fee;
    private readonly Money _exemptFrom;

    public CheckingAccountFee(Money fee, Money exemptFrom)
    {
        if (!fee.IsPositive)
        {
            throw new DomainException("A tarifa mensal precisa ser maior que zero.");
        }

        _fee = fee;
        _exemptFrom = exemptFrom;
    }

    public MonthlyCharge? Calculate(Account account)
    {
        ArgumentNullException.ThrowIfNull(account);
        return account.Balance >= _exemptFrom
            ? null
            : new MonthlyCharge(TransactionType.Fee, _fee, $"Tarifa mensal (isenta com saldo a partir de {_exemptFrom})");
    }
}

/// <summary>Poupança: rendimento mensal sobre o saldo, truncado no centavo.</summary>
/// <remarks>
/// Truncar (e não arredondar) garante que a soma dos rendimentos nunca passe da taxa contratada.
/// Saldo zero ou rendimento abaixo de um centavo não gera lançamento.
/// </remarks>
public sealed class SavingsInterest : IMonthlyPolicy
{
    private readonly decimal _monthlyRate;

    public SavingsInterest(decimal monthlyRate)
    {
        if (monthlyRate is <= 0 or >= 1)
        {
            throw new DomainException($"A taxa mensal deve ficar entre 0 e 1 (ex.: 0,005 para 0,5%): {monthlyRate}.");
        }

        _monthlyRate = monthlyRate;
    }

    public MonthlyCharge? Calculate(Account account)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (!account.Balance.IsPositive)
        {
            return null;
        }

        var interest = decimal.Round(account.Balance.Amount * _monthlyRate, 2, MidpointRounding.ToZero);
        return interest <= 0
            ? null
            : new MonthlyCharge(TransactionType.Interest, Money.Create(interest, account.Currency), $"Rendimento de {_monthlyRate:P2} ao mês");
    }
}
