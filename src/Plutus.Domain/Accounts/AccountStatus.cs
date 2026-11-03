namespace Plutus.Domain.Accounts;

public enum AccountStatus
{
    /// <summary>Movimenta normalmente.</summary>
    Active = 1,

    /// <summary>Não movimenta até ser desbloqueada (suspeita de fraude, ordem judicial...).</summary>
    Blocked = 2,

    /// <summary>Encerrada definitivamente. Não volta a ficar ativa.</summary>
    Closed = 3,
}
