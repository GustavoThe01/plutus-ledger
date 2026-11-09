using Microsoft.Data.Sqlite;
using Plutus.Application.Abstractions;
using Plutus.Domain.Common;

namespace Plutus.Infrastructure.Persistence;

/// <summary>A conexão e a transação de uma operação. É Scoped: repositórios do mesmo escopo compartilham ambas.</summary>
internal sealed class DbSession(string connectionString) : IAsyncDisposable
{
    private SqliteConnection? _connection;

    public SqliteTransaction? Transaction { get; set; }

    public async Task<SqliteConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        if (_connection is null)
        {
            _connection = new SqliteConnection(connectionString);
            await _connection.OpenAsync(cancellationToken);
        }

        return _connection;
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
    }
}

/// <summary>
/// Abre a transação com <c>BEGIN IMMEDIATE</c>, o padrão do Microsoft.Data.Sqlite: a escrita fica reservada desde o início,
/// e duas operações não leem o mesmo saldo para depois disputarem a gravação.
/// </summary>
/// <remarks>
/// No SQLite, isso já serializa as escritas: quem chega depois espera (até o timeout do comando) e lê o saldo atualizado.
/// A coluna <c>version</c> é a segunda garantia, a que não depende do banco: se uma conta lida fora desta transação
/// for gravada depois que outra operação a alterou, o UPDATE não acha a versão e a operação é recusada (ADR 0002).
/// </remarks>
internal sealed class UnitOfWork(DbSession session) : IUnitOfWork
{
    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken)
    {
        var connection = await session.GetConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        session.Transaction = transaction;

        try
        {
            var result = await work(cancellationToken);

            // Uma falha de negócio não é exceção, mas também não pode confirmar o que tiver sido gravado.
            if (result is Result { IsFailure: true })
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            else
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return result;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            session.Transaction = null;
        }
    }
}
