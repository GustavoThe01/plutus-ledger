using System.Globalization;
using Plutus.Domain.Common;

namespace Plutus.Domain.Accounts;

/// <summary>
/// Número da conta no formato <c>NNNNNN-D</c>: 6 dígitos e um dígito verificador.
/// </summary>
/// <remarks>
/// O dígito verificador usa o algoritmo de Luhn (o mesmo dos cartões de crédito).
/// Ele detecta qualquer erro de um único dígito e a maioria das trocas de dígitos vizinhos,
/// os erros mais comuns de digitação.
/// </remarks>
public readonly record struct AccountNumber
{
    private const int BaseLength = 6;
    private const int MaxSequence = 999_999;

    public string Value { get; }

    private AccountNumber(string value) => Value = value;

    /// <summary>Gera o número da conta a partir de um sequencial (1 a 999999).</summary>
    public static AccountNumber FromSequence(int sequence)
    {
        if (sequence is < 1 or > MaxSequence)
        {
            throw new DomainException($"O sequencial da conta deve estar entre 1 e {MaxSequence}: {sequence}.");
        }

        var digits = sequence.ToString("D6", CultureInfo.InvariantCulture);
        return new AccountNumber($"{digits}-{CalculateCheckDigit(digits)}");
    }

    public static AccountNumber Parse(string value)
    {
        if (!TryParse(value, out var accountNumber))
        {
            throw new DomainException($"Número de conta inválido: '{value}'. Formato esperado: 000000-0.");
        }

        return accountNumber;
    }

    public static bool TryParse(string? value, out AccountNumber accountNumber)
    {
        accountNumber = default;
        var trimmed = value?.Trim();

        if (trimmed is not { Length: BaseLength + 2 } || trimmed[BaseLength] != '-')
        {
            return false;
        }

        var digits = trimmed[..BaseLength];
        var checkDigit = trimmed[^1];

        if (!digits.All(char.IsAsciiDigit) || !char.IsAsciiDigit(checkDigit))
        {
            return false;
        }

        if (CalculateCheckDigit(digits) != checkDigit - '0')
        {
            return false;
        }

        accountNumber = new AccountNumber(trimmed);
        return true;
    }

    public override string ToString() => Value;

    private static int CalculateCheckDigit(string digits)
    {
        var sum = 0;
        var doubleIt = true;

        for (var i = digits.Length - 1; i >= 0; i--)
        {
            var digit = digits[i] - '0';

            if (doubleIt)
            {
                digit *= 2;
                if (digit > 9)
                {
                    digit -= 9;
                }
            }

            sum += digit;
            doubleIt = !doubleIt;
        }

        return (10 - (sum % 10)) % 10;
    }
}
