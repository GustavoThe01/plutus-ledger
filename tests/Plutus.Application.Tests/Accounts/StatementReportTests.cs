using System.Globalization;
using Plutus.Application.Accounts;
using Plutus.Domain.Accounts;

namespace Plutus.Application.Tests.Accounts;

public class StatementReportTests
{
    private static readonly Transaction[] Ledger =
    [
        Entry(TransactionType.Deposit, 1000m, 1000m, "2026-09-05T10:00:00Z"),
        Entry(TransactionType.Withdrawal, 200m, 800m, "2026-09-20T10:00:00Z"),
        Entry(TransactionType.TransferOut, 300m, 500m, "2026-10-02T10:00:00Z"),
        Entry(TransactionType.TransferIn, 150m, 650m, "2026-10-15T10:00:00Z"),
        Entry(TransactionType.Deposit, 50m, 700m, "2026-11-01T10:00:00Z"),
    ];

    private static Money Brl(decimal amount) => Money.Create(amount, "BRL");

    private static Transaction Entry(TransactionType type, decimal amount, decimal balanceAfter, string date) =>
        new(Guid.NewGuid(), type, Brl(amount), Brl(balanceAfter), DateTimeOffset.Parse(date, CultureInfo.InvariantCulture));

    [Fact]
    public void ToLine_UsesSignedAmounts()
    {
        var lines = Ledger.Select(StatementReport.ToLine).ToList();

        Assert.Equal([1000m, -200m, -300m, 150m, 50m], lines.Select(l => l.Amount));
        Assert.Equal("Transferência enviada", lines[2].Description);
    }

    [Fact]
    public void Paginate_ReturnsThePage_AndTheTotals()
    {
        var page = StatementReport.Paginate(Ledger, pageNumber: 2, pageSize: 2);

        Assert.Equal(2, page.Items.Count);
        Assert.Same(Ledger[2], page.Items[0]);
        Assert.Equal(5, page.TotalItems);
        Assert.Equal(3, page.TotalPages);
        Assert.True(page.HasNextPage);
    }

    [Fact]
    public void Paginate_AfterTheLastPage_IsEmpty()
    {
        var page = StatementReport.Paginate(Ledger, pageNumber: 4, pageSize: 2);

        Assert.Empty(page.Items);
        Assert.False(page.HasNextPage);
    }

    [Fact]
    public void Paginate_RejectsInvalidArguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => StatementReport.Paginate(Ledger, 0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => StatementReport.Paginate(Ledger, 1, 0));
    }

    [Fact]
    public void Paginate_EnumeratesTheSourceOnlyOnce()
    {
        var enumerations = 0;

        IEnumerable<int> Source()
        {
            enumerations++;
            foreach (var number in Enumerable.Range(1, 10))
            {
                yield return number;
            }
        }

        StatementReport.Paginate(Source(), 1, 3);

        Assert.Equal(1, enumerations);
    }

    [Fact]
    public void ByMonth_SumsInflowsAndOutflows_AndKeepsTheClosingBalance()
    {
        var months = StatementReport.ByMonth(Ledger.Reverse());

        Assert.Equal(["2026-09", "2026-10", "2026-11"], months.Select(m => m.Month));
        Assert.Equal(new MonthlySummary("2026-09", 1000m, 200m, 800m, 800m, 2), months[0]);
        Assert.Equal(new MonthlySummary("2026-10", 150m, 300m, -150m, 650m, 2), months[1]);
        Assert.Equal(700m, months[2].ClosingBalance);
    }

    [Fact]
    public void ByMonth_OfNothing_IsEmpty()
    {
        Assert.Empty(StatementReport.ByMonth([]));
    }
}
