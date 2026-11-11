using Plutus.Application.Abstractions;
using Plutus.Domain.Accounts;
using Plutus.Domain.Common;

namespace Plutus.Application.Accounts;

/// <summary>O que a interface mostra de uma conta. Disponível é o saldo somado ao limite.</summary>
public sealed record AccountSummary(
    string Number,
    string HolderName,
    decimal Balance,
    decimal OverdraftLimit,
    decimal Available,
    string Currency,
    AccountStatus Status,
    AccountType Type = AccountType.Checking);

/// <summary>Consulta o saldo e a situação de uma conta.</summary>
public sealed class GetAccount(IAccountRepository accounts)
{
    public async Task<Result<AccountSummary>> HandleAsync(string accountNumber, CancellationToken cancellationToken = default)
    {
        if (!AccountNumber.TryParse(accountNumber, out var number))
        {
            return Result<AccountSummary>.Failure(ApplicationErrors.InvalidAccountNumber(accountNumber));
        }

        var account = await accounts.GetByNumberAsync(number, cancellationToken);
        return account is null
            ? Result<AccountSummary>.Failure(ApplicationErrors.AccountNotFound(accountNumber))
            : Result<AccountSummary>.Success(new AccountSummary(
                account.Number.Value,
                account.HolderName,
                account.Balance.Amount,
                account.OverdraftLimit.Amount,
                account.Balance.Amount + account.OverdraftLimit.Amount,
                account.Balance.Currency,
                account.Status,
                account.Type));
    }
}
