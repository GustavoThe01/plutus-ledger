using Plutus.Domain.Accounts;

namespace Plutus.Application.Abstractions;

/// <summary>Como a aplicação guarda e recupera contas. A implementação com Dapper chega no Dia 05.</summary>
public interface IAccountRepository
{
    Task<Account?> GetByNumberAsync(AccountNumber number, CancellationToken cancellationToken);

    /// <summary>Próximo sequencial livre, usado para gerar o número de uma conta nova.</summary>
    Task<int> NextSequenceAsync(CancellationToken cancellationToken);

    Task AddAsync(Account account, CancellationToken cancellationToken);

    /// <summary>Grava o saldo, a situação e os lançamentos novos da conta.</summary>
    Task UpdateAsync(Account account, CancellationToken cancellationToken);

    Task<IReadOnlyList<Transaction>> GetTransactionsAsync(
        AccountId accountId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);
}

/// <summary>Fronteira da transação de banco: ou tudo o que <c>work</c> gravar é confirmado, ou nada é.</summary>
public interface IUnitOfWork
{
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken);
}
