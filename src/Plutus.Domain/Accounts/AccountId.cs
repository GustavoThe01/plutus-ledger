namespace Plutus.Domain.Accounts;

/// <summary>Identificador técnico da conta, tipado para não ser confundido com outros Guids.</summary>
public readonly record struct AccountId(Guid Value)
{
    public static AccountId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
