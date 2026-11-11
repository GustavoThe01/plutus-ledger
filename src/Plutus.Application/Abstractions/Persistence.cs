using Plutus.Domain.Accounts;

namespace Plutus.Application.Abstractions;

/// <summary>Como a aplicação guarda e recupera contas. A implementação com Dapper chega no Dia 05.</summary>
public interface IAccountRepository
{
    Task<Account?> GetByNumberAsync(AccountNumber number, CancellationToken cancellationToken);

    /// <summary>Próximo sequencial livre, usado para gerar o número de uma conta nova.</summary>
    Task<int> NextSequenceAsync(CancellationToken cancellationToken);

    Task AddAsync(Account account, CancellationToken cancellationToken);

    /// <summary>
    /// Grava o saldo, a situação e os lançamentos novos da conta, se a versão no banco ainda for a lida.
    /// Caso contrário, lança <see cref="ConcurrencyConflictException"/>.
    /// </summary>
    Task UpdateAsync(Account account, CancellationToken cancellationToken);

    /// <summary>Números das contas ativas, na ordem de abertura. É a lista que o fechamento do mês percorre.</summary>
    Task<IReadOnlyList<AccountNumber>> GetActiveNumbersAsync(CancellationToken cancellationToken);

    Task<bool> IsMonthClosedAsync(AccountId accountId, string month, CancellationToken cancellationToken);

    /// <summary>Marca o mês (aaaa-mm) como fechado para a conta. Deve rodar na mesma transação do lançamento.</summary>
    Task RegisterClosingAsync(AccountId accountId, string month, string outcome, DateTimeOffset closedAt, CancellationToken cancellationToken);

    Task<IReadOnlyList<Transaction>> GetTransactionsAsync(
        AccountId accountId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);
}

/// <summary>
/// Outra operação alterou a conta entre a leitura e a gravação. Nada foi gravado, e a operação pode ser repetida.
/// </summary>
public sealed class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException(string message)
        : base(message)
    {
    }

    public ConcurrencyConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public ConcurrencyConflictException()
    {
    }
}

/// <summary>Fronteira da transação de banco: ou tudo o que <c>work</c> gravar é confirmado, ou nada é.</summary>
public interface IUnitOfWork
{
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken);
}
