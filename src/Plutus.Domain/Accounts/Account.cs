using Plutus.Domain.Common;

namespace Plutus.Domain.Accounts;

/// <summary>
/// Conta bancária. É o agregado central do Plutus e a fronteira de consistência do saldo.
/// </summary>
/// <remarks>
/// Invariantes que a própria classe garante:
/// <list type="bullet">
/// <item>o saldo nunca fica abaixo de menos o limite (<c>Balance &gt;= -OverdraftLimit</c>);</item>
/// <item>o limite nunca é negativo e usa a mesma moeda da conta;</item>
/// <item>conta encerrada não muda mais; só é encerrada com saldo zero.</item>
/// </list>
/// Depósitos, saques e transferências chegam no Dia 03, já respeitando essas invariantes.
/// </remarks>
public sealed class Account
{
    private const int HolderNameMinLength = 3;
    private const int HolderNameMaxLength = 120;

    private Account(AccountId id, AccountNumber number, string holderName, Money overdraftLimit)
    {
        Id = id;
        Number = number;
        HolderName = holderName;
        Balance = Money.Zero(overdraftLimit.Currency);
        OverdraftLimit = overdraftLimit;
        Status = AccountStatus.Active;
    }

    public AccountId Id { get; }

    public AccountNumber Number { get; }

    public string HolderName { get; }

    public Money Balance { get; private set; }

    public Money OverdraftLimit { get; private set; }

    public AccountStatus Status { get; private set; }

    public string Currency => Balance.Currency;

    /// <summary>Quanto o titular ainda pode usar: saldo + limite.</summary>
    public Money AvailableBalance => Balance + OverdraftLimit;

    public static Account Open(AccountNumber number, string holderName, Money overdraftLimit)
    {
        if (string.IsNullOrEmpty(number.Value))
        {
            throw new DomainException("O número da conta é obrigatório.");
        }

        EnsureValidLimit(overdraftLimit);
        return new Account(AccountId.New(), number, ValidateHolderName(holderName), overdraftLimit);
    }

    public void ChangeOverdraftLimit(Money newLimit)
    {
        EnsureNotClosed();
        EnsureValidLimit(newLimit);

        if (newLimit.Currency != Currency)
        {
            throw new DomainException($"O limite deve estar em {Currency}.");
        }

        if (Balance + newLimit < Money.Zero(Currency))
        {
            throw new DomainException(
                $"O saldo atual ({Balance}) já usa mais do que o novo limite ({newLimit}). Cubra o saldo antes de reduzir o limite.");
        }

        OverdraftLimit = newLimit;
    }

    public void Block()
    {
        if (Status != AccountStatus.Active)
        {
            throw new DomainException($"Só uma conta ativa pode ser bloqueada. Situação atual: {Status}.");
        }

        Status = AccountStatus.Blocked;
    }

    public void Unblock()
    {
        if (Status != AccountStatus.Blocked)
        {
            throw new DomainException($"Só uma conta bloqueada pode ser desbloqueada. Situação atual: {Status}.");
        }

        Status = AccountStatus.Active;
    }

    public void Close()
    {
        EnsureNotClosed();

        if (!Balance.IsZero)
        {
            throw new DomainException($"A conta só pode ser encerrada com saldo zero. Saldo atual: {Balance}.");
        }

        Status = AccountStatus.Closed;
    }

    private void EnsureNotClosed()
    {
        if (Status == AccountStatus.Closed)
        {
            throw new DomainException("A conta está encerrada e não pode ser alterada.");
        }
    }

    private static void EnsureValidLimit(Money limit)
    {
        if (limit.IsNegative)
        {
            throw new DomainException($"O limite não pode ser negativo: {limit}.");
        }
    }

    private static string ValidateHolderName(string holderName)
    {
        var trimmed = holderName?.Trim() ?? string.Empty;

        if (trimmed.Length is < HolderNameMinLength or > HolderNameMaxLength)
        {
            throw new DomainException(
                $"O nome do titular deve ter entre {HolderNameMinLength} e {HolderNameMaxLength} caracteres.");
        }

        return trimmed;
    }
}
