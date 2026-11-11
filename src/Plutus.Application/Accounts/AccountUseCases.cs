using Microsoft.Extensions.Options;
using Plutus.Application.Abstractions;
using Plutus.Domain.Accounts;
using Plutus.Domain.Common;

namespace Plutus.Application.Accounts;

/// <remarks>
/// Os casos de uso só orquestram: buscam as contas, delegam a decisão ao domínio e gravam dentro de uma transação.
/// Nenhuma regra de negócio mora aqui.
/// </remarks>
public sealed class OpenAccount(IAccountRepository accounts, IUnitOfWork unitOfWork, IOptions<LedgerOptions> options)
{
    /// <summary>Abre uma conta corrente.</summary>
    public Task<Result<AccountNumber>> HandleAsync(
        string holderName,
        decimal overdraftLimit,
        CancellationToken cancellationToken = default) =>
        HandleAsync(holderName, overdraftLimit, AccountType.Checking, cancellationToken);

    public async Task<Result<AccountNumber>> HandleAsync(
        string holderName,
        decimal overdraftLimit,
        AccountType type,
        CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        if (overdraftLimit > settings.MaxOverdraftLimit)
        {
            return Result<AccountNumber>.Failure(
                ApplicationErrors.OverdraftLimitTooHigh(overdraftLimit, settings.MaxOverdraftLimit));
        }

        return await unitOfWork.ExecuteAsync(
            async token =>
            {
                var sequence = await accounts.NextSequenceAsync(token);
                try
                {
                    var account = Account.Open(
                        AccountNumber.FromSequence(sequence),
                        holderName,
                        Money.Create(overdraftLimit, settings.DefaultCurrency),
                        type);

                    await accounts.AddAsync(account, token);
                    return Result<AccountNumber>.Success(account.Number);
                }
                catch (DomainException exception)
                {
                    return Result<AccountNumber>.Failure(ApplicationErrors.InvalidInput(exception.Message));
                }
            },
            cancellationToken);
    }
}

public sealed class DepositMoney(
    IAccountRepository accounts,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    IOptions<LedgerOptions> options)
{
    public Task<Result> HandleAsync(string accountNumber, decimal amount, CancellationToken cancellationToken = default) =>
        AccountMovement.ExecuteAsync(
            accounts,
            unitOfWork,
            options.Value.DefaultCurrency,
            accountNumber,
            amount,
            (account, money) => account.Deposit(money, clock.GetUtcNow()),
            cancellationToken);
}

public sealed class WithdrawMoney(
    IAccountRepository accounts,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    IOptions<LedgerOptions> options)
{
    public Task<Result> HandleAsync(string accountNumber, decimal amount, CancellationToken cancellationToken = default) =>
        AccountMovement.ExecuteAsync(
            accounts,
            unitOfWork,
            options.Value.DefaultCurrency,
            accountNumber,
            amount,
            (account, money) => account.Withdraw(money, clock.GetUtcNow()),
            cancellationToken);
}

public sealed class TransferMoney(
    IAccountRepository accounts,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    IOptions<LedgerOptions> options)
{
    public async Task<Result> HandleAsync(
        string sourceNumber,
        string destinationNumber,
        decimal amount,
        CancellationToken cancellationToken = default)
    {
        if (!AccountNumber.TryParse(sourceNumber, out var source))
        {
            return Result.Failure(ApplicationErrors.InvalidAccountNumber(sourceNumber));
        }

        if (!AccountNumber.TryParse(destinationNumber, out var destination))
        {
            return Result.Failure(ApplicationErrors.InvalidAccountNumber(destinationNumber));
        }

        if (!AccountMovement.TryCreateMoney(amount, options.Value.DefaultCurrency, out var money, out var invalid))
        {
            return Result.Failure(invalid);
        }

        try
        {
            return await unitOfWork.ExecuteAsync(
                async token =>
                {
                    var from = await accounts.GetByNumberAsync(source, token);
                    if (from is null)
                    {
                        return Result.Failure(ApplicationErrors.AccountNotFound(sourceNumber));
                    }

                    var to = await accounts.GetByNumberAsync(destination, token);
                    if (to is null)
                    {
                        return Result.Failure(ApplicationErrors.AccountNotFound(destinationNumber));
                    }

                    var transfer = TransferService.Transfer(from, to, money, clock.GetUtcNow());
                    if (transfer.IsFailure)
                    {
                        return Result.Failure(transfer.Error!);
                    }

                    await accounts.UpdateAsync(from, token);
                    await accounts.UpdateAsync(to, token);
                    return Result.Success();
                },
                cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            return Result.Failure(ApplicationErrors.ConcurrencyConflict());
        }
    }
}

/// <summary>Extrato paginado, do lançamento mais antigo para o mais recente.</summary>
public sealed class GetStatement(IAccountRepository accounts, IOptions<LedgerOptions> options)
{
    public async Task<Result<Page<StatementLine>>> HandleAsync(
        string accountNumber,
        DateTimeOffset from,
        DateTimeOffset to,
        int pageNumber = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (pageNumber < 1 || pageSize < 1 || pageSize > options.Value.MaxPageSize)
        {
            return Result<Page<StatementLine>>.Failure(ApplicationErrors.InvalidInput(
                $"Use página a partir de 1 e tamanho entre 1 e {options.Value.MaxPageSize}."));
        }

        var period = await StatementPeriod.LoadAsync(accounts, options.Value, accountNumber, from, to, cancellationToken);
        return period.IsFailure
            ? Result<Page<StatementLine>>.Failure(period.Error!)
            : Result<Page<StatementLine>>.Success(
                StatementReport.Paginate(period.Value.Select(StatementReport.ToLine), pageNumber, pageSize));
    }
}

/// <summary>Entradas, saídas e saldo de fechamento de cada mês do período.</summary>
public sealed class GetMonthlySummary(IAccountRepository accounts, IOptions<LedgerOptions> options)
{
    public async Task<Result<IReadOnlyList<MonthlySummary>>> HandleAsync(
        string accountNumber,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var period = await StatementPeriod.LoadAsync(accounts, options.Value, accountNumber, from, to, cancellationToken);
        return period.IsFailure
            ? Result<IReadOnlyList<MonthlySummary>>.Failure(period.Error!)
            : Result<IReadOnlyList<MonthlySummary>>.Success(StatementReport.ByMonth(period.Value));
    }
}

internal static class StatementPeriod
{
    public static async Task<Result<IReadOnlyList<Transaction>>> LoadAsync(
        IAccountRepository accounts,
        LedgerOptions options,
        string accountNumber,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (!AccountNumber.TryParse(accountNumber, out var number))
        {
            return Result<IReadOnlyList<Transaction>>.Failure(ApplicationErrors.InvalidAccountNumber(accountNumber));
        }

        if (to < from)
        {
            return Result<IReadOnlyList<Transaction>>.Failure(
                ApplicationErrors.InvalidInput("O fim do período não pode ser antes do início."));
        }

        if ((to - from).TotalDays > options.MaxStatementDays)
        {
            return Result<IReadOnlyList<Transaction>>.Failure(ApplicationErrors.StatementPeriodTooLong(options.MaxStatementDays));
        }

        var account = await accounts.GetByNumberAsync(number, cancellationToken);
        if (account is null)
        {
            return Result<IReadOnlyList<Transaction>>.Failure(ApplicationErrors.AccountNotFound(accountNumber));
        }

        var transactions = await accounts.GetTransactionsAsync(account.Id, from, to, cancellationToken);
        return Result<IReadOnlyList<Transaction>>.Success(transactions);
    }
}

internal static class AccountMovement
{
    public static async Task<Result> ExecuteAsync(
        IAccountRepository accounts,
        IUnitOfWork unitOfWork,
        string currency,
        string accountNumber,
        decimal amount,
        Func<Account, Money, Result> move,
        CancellationToken cancellationToken)
    {
        if (!AccountNumber.TryParse(accountNumber, out var number))
        {
            return Result.Failure(ApplicationErrors.InvalidAccountNumber(accountNumber));
        }

        if (!TryCreateMoney(amount, currency, out var money, out var invalid))
        {
            return Result.Failure(invalid);
        }

        try
        {
            return await unitOfWork.ExecuteAsync(
                async token =>
                {
                    var account = await accounts.GetByNumberAsync(number, token);
                    if (account is null)
                    {
                        return Result.Failure(ApplicationErrors.AccountNotFound(accountNumber));
                    }

                    var result = move(account, money);
                    if (result.IsSuccess)
                    {
                        await accounts.UpdateAsync(account, token);
                    }

                    return result;
                },
                cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            return Result.Failure(ApplicationErrors.ConcurrencyConflict());
        }
    }

    public static bool TryCreateMoney(decimal amount, string currency, out Money money, out Error error)
    {
        try
        {
            money = Money.Create(amount, currency);
            error = null!;
            return true;
        }
        catch (DomainException exception)
        {
            money = default;
            error = ApplicationErrors.InvalidInput(exception.Message);
            return false;
        }
    }
}
