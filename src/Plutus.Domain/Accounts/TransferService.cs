using Plutus.Domain.Common;

namespace Plutus.Domain.Accounts;

/// <summary>
/// Transferência entre contas. É um serviço de domínio porque a regra envolve dois agregados
/// e não pertence a nenhum deles sozinho.
/// </summary>
public static class TransferService
{
    /// <remarks>
    /// Valida o crédito antes de debitar: assim, quando o débito acontece, o crédito não tem como falhar
    /// e as duas contas nunca ficam pela metade em memória. Gravar as duas na mesma transação
    /// do banco é responsabilidade da camada de aplicação (Dia 05).
    /// </remarks>
    public static Result<MoneyTransferred> Transfer(
        Account source,
        Account destination,
        Money amount,
        DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        if (source.Id == destination.Id)
        {
            return Result<MoneyTransferred>.Failure(AccountErrors.SameAccount(source.Number));
        }

        var canReceive = destination.CanMove(amount);
        if (canReceive.IsFailure)
        {
            return Result<MoneyTransferred>.Failure(canReceive.Error!);
        }

        var debit = source.Debit(amount, TransactionType.TransferOut, occurredAt, destination.Id);
        if (debit.IsFailure)
        {
            return Result<MoneyTransferred>.Failure(debit.Error!);
        }

        destination.Credit(amount, TransactionType.TransferIn, occurredAt, source.Id);

        var transferred = new MoneyTransferred(source.Id, destination.Id, amount, occurredAt);
        source.Raise(transferred);
        return Result<MoneyTransferred>.Success(transferred);
    }
}
