namespace Plutus.Domain.Accounts;

public enum TransactionType
{
    Deposit = 1,
    Withdrawal = 2,
    TransferIn = 3,
    TransferOut = 4,
}

/// <summary>
/// Lançamento do livro-razão. É imutável: um lançamento errado se corrige com outro lançamento, nunca editando.
/// </summary>
/// <param name="Amount">Sempre positivo; o <paramref name="Type"/> diz se é entrada ou saída.</param>
/// <param name="BalanceAfter">Saldo da conta logo depois do lançamento.</param>
/// <param name="CounterpartyId">A outra conta, em transferências.</param>
public sealed record Transaction(
    Guid Id,
    TransactionType Type,
    Money Amount,
    Money BalanceAfter,
    DateTimeOffset OccurredAt,
    AccountId? CounterpartyId = null);
