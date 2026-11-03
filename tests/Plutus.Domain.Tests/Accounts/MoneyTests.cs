using Plutus.Domain.Accounts;
using Plutus.Domain.Common;

namespace Plutus.Domain.Tests.Accounts;

public class MoneyTests
{
    [Fact]
    public void Create_NormalizesCurrencyCode()
    {
        var money = Money.Create(10m, " brl ");

        Assert.Equal("BRL", money.Currency);
    }

    [Fact]
    public void Equality_IsByValue()
    {
        Assert.Equal(Money.Create(10.50m, "BRL"), Money.Create(10.5m, "BRL"));
    }

    [Fact]
    public void Add_IsExact()
    {
        var sum = Money.Create(0.10m, "BRL") + Money.Create(0.20m, "BRL");

        Assert.Equal(Money.Create(0.30m, "BRL"), sum);
    }

    [Fact]
    public void Subtract_CanGoBelowZero()
    {
        var result = Money.Create(10m, "BRL") - Money.Create(25m, "BRL");

        Assert.True(result.IsNegative);
        Assert.Equal(-15m, result.Amount);
    }

    [Theory]
    [InlineData(1.005)]
    [InlineData(0.001)]
    public void Create_RejectsMoreThanTwoDecimals(decimal amount)
    {
        var exception = Assert.Throws<DomainException>(() => Money.Create(amount, "BRL"));

        Assert.Contains("casas decimais", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("BR")]
    [InlineData("REAL")]
    [InlineData("B1L")]
    public void Create_RejectsInvalidCurrency(string currency)
    {
        Assert.Throws<DomainException>(() => Money.Create(1m, currency));
    }

    [Fact]
    public void Operations_RejectDifferentCurrencies()
    {
        var real = Money.Create(1m, "BRL");
        var dollar = Money.Create(1m, "USD");

        Assert.Throws<DomainException>(() => real + dollar);
        Assert.Throws<DomainException>(() => real > dollar);
    }

    [Fact]
    public void Comparison_Works()
    {
        Assert.True(Money.Create(2m, "BRL") > Money.Create(1.99m, "BRL"));
        Assert.True(Money.Zero("BRL") <= Money.Create(0m, "BRL"));
    }

    [Fact]
    public void ToString_UsesTwoDecimals()
    {
        Assert.Equal("BRL 7.50", Money.Create(7.5m, "BRL").ToString());
    }
}
