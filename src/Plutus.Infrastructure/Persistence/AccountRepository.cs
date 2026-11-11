using System.Globalization;
using Dapper;
using Plutus.Application.Abstractions;
using Plutus.Domain.Accounts;

namespace Plutus.Infrastructure.Persistence;

/// <summary>
/// Repositório com Dapper: o SQL é escrito à mão e o mapeamento é explícito, sem exigir nada do domínio.
/// </summary>
internal sealed class AccountRepository(DbSession session) : IAccountRepository
{
    private const string AccountColumns = """
        id AS Id, number AS Number, holder_name AS HolderName, balance_cents AS BalanceCents,
        overdraft_limit_cents AS OverdraftLimitCents, currency AS Currency, status AS Status, version AS Version,
        type AS Type
        """;

    public async Task<Account?> GetByNumberAsync(AccountNumber number, CancellationToken cancellationToken)
    {
        var connection = await session.GetConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<AccountRow>(Command(
            $"SELECT {AccountColumns} FROM account WHERE number = @number",
            new { number = number.Value },
            cancellationToken));

        return row?.ToDomain();
    }

    public async Task<int> NextSequenceAsync(CancellationToken cancellationToken)
    {
        var connection = await session.GetConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<int>(Command(
            "SELECT COALESCE(MAX(CAST(substr(number, 1, 6) AS INTEGER)), 0) + 1 FROM account",
            null,
            cancellationToken));
    }

    public async Task AddAsync(Account account, CancellationToken cancellationToken)
    {
        var connection = await session.GetConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(Command(
            """
            INSERT INTO account (id, number, holder_name, balance_cents, overdraft_limit_cents, currency, status, type)
            VALUES (@Id, @Number, @HolderName, @BalanceCents, @OverdraftLimitCents, @Currency, @Status, @Type)
            """,
            AccountRow.FromDomain(account),
            cancellationToken));

        await InsertTransactionsAsync(account, cancellationToken);
    }

    public async Task UpdateAsync(Account account, CancellationToken cancellationToken)
    {
        var connection = await session.GetConnectionAsync(cancellationToken);

        // Concorrência otimista: só grava se ninguém mudou a conta desde a leitura.
        // Zero linhas afetadas quer dizer que a versão no banco já é outra.
        var updated = await connection.ExecuteAsync(Command(
            """
            UPDATE account
            SET balance_cents = @BalanceCents, overdraft_limit_cents = @OverdraftLimitCents, status = @Status,
                version = version + 1
            WHERE id = @Id AND version = @Version
            """,
            AccountRow.FromDomain(account),
            cancellationToken));

        if (updated == 0)
        {
            throw new ConcurrencyConflictException(
                $"A conta {account.Number} mudou depois de ser lida (versão {account.Version}).");
        }

        await InsertTransactionsAsync(account, cancellationToken);
    }

    public async Task<IReadOnlyList<AccountNumber>> GetActiveNumbersAsync(CancellationToken cancellationToken)
    {
        var connection = await session.GetConnectionAsync(cancellationToken);
        var numbers = await connection.QueryAsync<string>(Command(
            "SELECT number FROM account WHERE status = 'Active' ORDER BY number",
            null,
            cancellationToken));

        return numbers.Select(AccountNumber.Parse).ToList();
    }

    public async Task<bool> IsMonthClosedAsync(AccountId accountId, string month, CancellationToken cancellationToken)
    {
        var connection = await session.GetConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(Command(
            "SELECT EXISTS (SELECT 1 FROM monthly_closing WHERE account_id = @accountId AND month = @month)",
            new { accountId = accountId.ToString(), month },
            cancellationToken));
    }

    public async Task RegisterClosingAsync(
        AccountId accountId,
        string month,
        string outcome,
        DateTimeOffset closedAt,
        CancellationToken cancellationToken)
    {
        var connection = await session.GetConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(Command(
            "INSERT INTO monthly_closing (account_id, month, outcome, closed_at) VALUES (@accountId, @month, @outcome, @closedAt)",
            new { accountId = accountId.ToString(), month, outcome, closedAt = PersistenceFormat.Date(closedAt) },
            cancellationToken));
    }

    public async Task<IReadOnlyList<Transaction>> GetTransactionsAsync(
        AccountId accountId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var connection = await session.GetConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<TransactionRow>(Command(
            """
            SELECT t.id AS Id, t.type AS Type, t.amount_cents AS AmountCents, t.balance_after_cents AS BalanceAfterCents,
                   t.occurred_at AS OccurredAt, t.counterparty_id AS CounterpartyId, a.currency AS Currency
            FROM ledger_transaction AS t
            JOIN account AS a ON a.id = t.account_id
            WHERE t.account_id = @accountId AND t.occurred_at BETWEEN @from AND @to
            ORDER BY t.occurred_at, t.rowid
            """,
            new { accountId = accountId.ToString(), from = PersistenceFormat.Date(from), to = PersistenceFormat.Date(to) },
            cancellationToken));

        return rows.Select(row => row.ToDomain()).ToList();
    }

    private async Task InsertTransactionsAsync(Account account, CancellationToken cancellationToken)
    {
        if (account.Transactions.Count == 0)
        {
            return;
        }

        var connection = await session.GetConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(Command(
            """
            INSERT INTO ledger_transaction (id, account_id, type, amount_cents, balance_after_cents, occurred_at, counterparty_id)
            VALUES (@Id, @AccountId, @Type, @AmountCents, @BalanceAfterCents, @OccurredAt, @CounterpartyId)
            """,
            account.Transactions.Select(t => new
            {
                Id = t.Id.ToString(),
                AccountId = account.Id.ToString(),
                Type = t.Type.ToString(),
                AmountCents = PersistenceFormat.Cents(t.Amount),
                BalanceAfterCents = PersistenceFormat.Cents(t.BalanceAfter),
                OccurredAt = PersistenceFormat.Date(t.OccurredAt),
                CounterpartyId = t.CounterpartyId?.ToString(),
            }),
            cancellationToken));
    }

    private CommandDefinition Command(string sql, object? parameters, CancellationToken cancellationToken) =>
        new(sql, parameters, session.Transaction, cancellationToken: cancellationToken);

    private sealed class AccountRow
    {
        public string Id { get; init; } = string.Empty;

        public string Number { get; init; } = string.Empty;

        public string HolderName { get; init; } = string.Empty;

        public long BalanceCents { get; init; }

        public long OverdraftLimitCents { get; init; }

        public string Currency { get; init; } = string.Empty;

        public string Status { get; init; } = string.Empty;

        public long Version { get; init; }

        public string Type { get; init; } = nameof(AccountType.Checking);

        public static AccountRow FromDomain(Account account) => new()
        {
            Id = account.Id.ToString(),
            Number = account.Number.Value,
            HolderName = account.HolderName,
            BalanceCents = PersistenceFormat.Cents(account.Balance),
            OverdraftLimitCents = PersistenceFormat.Cents(account.OverdraftLimit),
            Currency = account.Currency,
            Status = account.Status.ToString(),
            Version = account.Version,
            Type = account.Type.ToString(),
        };

        public Account ToDomain() => Account.Restore(
            new AccountId(Guid.Parse(Id, CultureInfo.InvariantCulture)),
            AccountNumber.Parse(Number),
            HolderName,
            PersistenceFormat.Money(BalanceCents, Currency),
            PersistenceFormat.Money(OverdraftLimitCents, Currency),
            Enum.Parse<AccountStatus>(Status),
            checked((int)Version),
            Enum.Parse<AccountType>(Type));
    }

    private sealed class TransactionRow
    {
        public string Id { get; init; } = string.Empty;

        public string Type { get; init; } = string.Empty;

        public long AmountCents { get; init; }

        public long BalanceAfterCents { get; init; }

        public string OccurredAt { get; init; } = string.Empty;

        public string? CounterpartyId { get; init; }

        public string Currency { get; init; } = string.Empty;

        public Transaction ToDomain() => new(
            Guid.Parse(Id, CultureInfo.InvariantCulture),
            Enum.Parse<TransactionType>(Type),
            PersistenceFormat.Money(AmountCents, Currency),
            PersistenceFormat.Money(BalanceAfterCents, Currency),
            DateTimeOffset.Parse(OccurredAt, CultureInfo.InvariantCulture),
            CounterpartyId is null ? null : new AccountId(Guid.Parse(CounterpartyId, CultureInfo.InvariantCulture)));
    }
}

internal static class PersistenceFormat
{
    public static long Cents(Money money) => decimal.ToInt64(money.Amount * 100m);

    public static Money Money(long cents, string currency) => Domain.Accounts.Money.Create(cents / 100m, currency);

    /// <summary>Sempre em UTC e com tamanho fixo: assim, a ordem do texto é a ordem do tempo.</summary>
    public static string Date(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
