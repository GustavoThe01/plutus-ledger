using Plutus.Domain.Accounts;
using Plutus.Domain.Common;

namespace Plutus.Domain.Tests.Accounts;

public class AccountNumberTests
{
    [Theory]
    [InlineData(1, "000001-8")]
    [InlineData(123456, "123456-6")]
    [InlineData(999999, "999999-6")]
    public void FromSequence_GeneratesLuhnCheckDigit(int sequence, string expected)
    {
        Assert.Equal(expected, AccountNumber.FromSequence(sequence).Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1_000_000)]
    public void FromSequence_RejectsOutOfRange(int sequence)
    {
        Assert.Throws<DomainException>(() => AccountNumber.FromSequence(sequence));
    }

    [Fact]
    public void Parse_AcceptsGeneratedNumbers()
    {
        var generated = AccountNumber.FromSequence(4242);

        Assert.Equal(generated, AccountNumber.Parse(generated.Value));
    }

    [Theory]
    [InlineData("123456-7")]
    [InlineData("123465-6")]
    [InlineData("12345-6")]
    [InlineData("1234567")]
    [InlineData("12a456-6")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParse_RejectsInvalidNumbers(string? value)
    {
        Assert.False(AccountNumber.TryParse(value, out _));
    }

    [Fact]
    public void Parse_ThrowsForInvalidCheckDigit()
    {
        Assert.Throws<DomainException>(() => AccountNumber.Parse("123456-0"));
    }
}
