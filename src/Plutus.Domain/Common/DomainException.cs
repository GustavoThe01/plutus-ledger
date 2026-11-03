namespace Plutus.Domain.Common;

/// <summary>
/// Violação de uma invariante do domínio (valor inválido ou operação proibida no estado atual).
/// </summary>
/// <remarks>
/// No Dia 03, falhas de negócio esperadas (como saldo insuficiente) passam a usar o Result pattern.
/// A exceção fica reservada para o que nunca deveria acontecer com dados válidos.
/// </remarks>
public sealed class DomainException : Exception
{
    public DomainException(string message)
        : base(message)
    {
    }

    public DomainException()
    {
    }

    public DomainException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
