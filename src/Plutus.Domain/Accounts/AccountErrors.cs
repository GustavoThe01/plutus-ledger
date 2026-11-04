using Plutus.Domain.Common;

namespace Plutus.Domain.Accounts;

/// <summary>Falhas de negócio das operações de conta.</summary>
public static class AccountErrors
{
    public static Error InvalidAmount(Money amount) =>
        new("account.invalid_amount", $"O valor da operação deve ser maior que zero: {amount}.");

    public static Error CurrencyMismatch(string expected, string received) =>
        new("account.currency_mismatch", $"A conta opera em {expected}, mas o valor está em {received}.");

    public static Error NotActive(AccountNumber number, AccountStatus status) =>
        new("account.not_active", $"A conta {number} não está ativa. Situação atual: {status}.");

    public static Error InsufficientFunds(AccountNumber number, Money available, Money requested) =>
        new("account.insufficient_funds", $"Saldo insuficiente na conta {number}: disponível {available}, solicitado {requested}.");

    public static Error SameAccount(AccountNumber number) =>
        new("account.same_account", $"A origem e o destino da transferência são a mesma conta ({number}).");
}
