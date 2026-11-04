using Plutus.Domain.Common;

namespace Plutus.Domain.Accounts;

/// <summary>Uma transferência foi concluída.</summary>
public sealed record MoneyTransferred(
    AccountId SourceAccountId,
    AccountId DestinationAccountId,
    Money Amount,
    DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>Uma saída foi recusada por falta de saldo. Útil para alertas e detecção de fraude.</summary>
public sealed record WithdrawalRejected(
    AccountId AccountId,
    Money Requested,
    Money Available,
    DateTimeOffset OccurredAt) : IDomainEvent;
