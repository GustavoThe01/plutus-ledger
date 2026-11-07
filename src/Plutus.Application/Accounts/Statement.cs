using System.Globalization;
using Plutus.Domain.Accounts;

namespace Plutus.Application.Accounts;

/// <summary>Linha do extrato. Entradas positivas e saídas negativas, como no extrato de um banco.</summary>
public sealed record StatementLine(DateTimeOffset OccurredAt, string Description, decimal Amount, decimal BalanceAfter);

public sealed record Page<T>(IReadOnlyList<T> Items, int PageNumber, int PageSize, int TotalItems)
{
    public int TotalPages => TotalItems == 0 ? 0 : (int)Math.Ceiling(TotalItems / (double)PageSize);

    public bool HasNextPage => PageNumber < TotalPages;
}

/// <summary>Resumo de um mês: o que entrou, o que saiu e o saldo no fechamento.</summary>
public sealed record MonthlySummary(string Month, decimal Inflows, decimal Outflows, decimal Net, decimal ClosingBalance, int Transactions);

/// <summary>
/// Consultas do extrato escritas com LINQ, sobre lançamentos já carregados em memória.
/// </summary>
/// <remarks>
/// Os métodos recebem <see cref="IEnumerable{T}"/>: tudo aqui roda em memória, e nada é enviado ao banco.
/// Com <see cref="IQueryable{T}"/> e um ORM, o mesmo LINQ viraria SQL e rodaria no banco, mas só com operações
/// que o provedor sabe traduzir. Os métodos materializam o resultado com <c>ToList()</c> no fim para que a
/// sequência não seja percorrida de novo a cada leitura (execução adiada).
/// </remarks>
public static class StatementReport
{
    public static StatementLine ToLine(Transaction transaction) => new(
        transaction.OccurredAt,
        Describe(transaction.Type),
        IsInflow(transaction.Type) ? transaction.Amount.Amount : -transaction.Amount.Amount,
        transaction.BalanceAfter.Amount);

    public static Page<T> Paginate<T>(IEnumerable<T> source, int pageNumber, int pageSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        var all = source as IReadOnlyCollection<T> ?? source.ToList();
        var items = all.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList();
        return new Page<T>(items, pageNumber, pageSize, all.Count);
    }

    public static IReadOnlyList<MonthlySummary> ByMonth(IEnumerable<Transaction> transactions) =>
        transactions
            .OrderBy(t => t.OccurredAt)
            .GroupBy(t => t.OccurredAt.ToUniversalTime().ToString("yyyy-MM", CultureInfo.InvariantCulture))
            .Select(month =>
            {
                var inflows = month.Where(t => IsInflow(t.Type)).Sum(t => t.Amount.Amount);
                var outflows = month.Where(t => !IsInflow(t.Type)).Sum(t => t.Amount.Amount);
                return new MonthlySummary(
                    month.Key,
                    inflows,
                    outflows,
                    inflows - outflows,
                    month.Last().BalanceAfter.Amount,
                    month.Count());
            })
            .ToList();

    private static bool IsInflow(TransactionType type) => type is TransactionType.Deposit or TransactionType.TransferIn;

    private static string Describe(TransactionType type) => type switch
    {
        TransactionType.Deposit => "Depósito",
        TransactionType.Withdrawal => "Saque",
        TransactionType.TransferIn => "Transferência recebida",
        TransactionType.TransferOut => "Transferência enviada",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Tipo de lançamento desconhecido."),
    };
}
