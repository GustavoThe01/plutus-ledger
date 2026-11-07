namespace Plutus.Application;

/// <summary>Regras configuráveis do livro-razão (seção "Ledger" da configuração).</summary>
public sealed class LedgerOptions
{
    public const string SectionName = "Ledger";

    /// <summary>Moeda das contas novas (ISO 4217).</summary>
    public string DefaultCurrency { get; set; } = "BRL";

    /// <summary>Maior limite que uma conta pode receber na abertura.</summary>
    public decimal MaxOverdraftLimit { get; set; } = 10_000m;

    /// <summary>Maior período, em dias, de uma consulta de extrato.</summary>
    public int MaxStatementDays { get; set; } = 366;

    /// <summary>Maior quantidade de linhas por página do extrato.</summary>
    public int MaxPageSize { get; set; } = 100;
}
