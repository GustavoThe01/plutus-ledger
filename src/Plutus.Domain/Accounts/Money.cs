using System.Globalization;
using Plutus.Domain.Common;

namespace Plutus.Domain.Accounts;

/// <summary>
/// Valor monetário com moeda (código ISO 4217, ex.: BRL). Imutável e comparado por valor.
/// </summary>
/// <remarks>
/// Uso <see cref="decimal"/> para não ter erros de arredondamento de ponto flutuante.
/// O valor pode ser negativo, porque o saldo de uma conta com limite pode ficar abaixo de zero.
/// Valores com mais de 2 casas decimais são rejeitados, nunca arredondados em silêncio.
/// </remarks>
public readonly record struct Money
{
    private const int DecimalPlaces = 2;

    public decimal Amount { get; }

    public string Currency { get; }

    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public static Money Create(decimal amount, string currency)
    {
        if (decimal.Round(amount, DecimalPlaces) != amount)
        {
            throw new DomainException($"O valor deve ter no máximo {DecimalPlaces} casas decimais: {amount}.");
        }

        return new Money(amount, NormalizeCurrency(currency));
    }

    public static Money Zero(string currency) => new(0m, NormalizeCurrency(currency));

    public bool IsZero => Amount == 0m;

    public bool IsPositive => Amount > 0m;

    public bool IsNegative => Amount < 0m;

    public static Money operator +(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(left.Amount + right.Amount, left.Currency);
    }

    public static Money operator -(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(left.Amount - right.Amount, left.Currency);
    }

    public static Money operator -(Money value) => new(-value.Amount, value.Currency);

    public static bool operator <(Money left, Money right) => Compare(left, right) < 0;

    public static bool operator >(Money left, Money right) => Compare(left, right) > 0;

    public static bool operator <=(Money left, Money right) => Compare(left, right) <= 0;

    public static bool operator >=(Money left, Money right) => Compare(left, right) >= 0;

    public Money Add(Money other) => this + other;

    public Money Subtract(Money other) => this - other;

    public Money Negate() => -this;

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Currency} {Amount:0.00}");

    private static int Compare(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return left.Amount.CompareTo(right.Amount);
    }

    private static void EnsureSameCurrency(Money left, Money right)
    {
        if (left.Currency != right.Currency)
        {
            throw new DomainException($"Não é possível operar {left.Currency} com {right.Currency}.");
        }
    }

    private static string NormalizeCurrency(string currency)
    {
        var normalized = currency?.Trim().ToUpperInvariant() ?? string.Empty;

        if (normalized.Length != 3 || !normalized.All(char.IsAsciiLetterUpper))
        {
            throw new DomainException($"Moeda inválida: '{currency}'. Use o código ISO de 3 letras (ex.: BRL).");
        }

        return normalized;
    }
}
