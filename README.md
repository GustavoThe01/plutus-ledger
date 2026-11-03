# Plutus · bank-ledger

> *Plutus, deus grego da riqueza, cuida de cada centavo do livro-razão.*

**Livro-razão bancário** (contas, depósitos, saques, transferências e extratos) em **C# / .NET 10**,
com persistência em **SQLite usando Dapper** e interface de console rica.

Foco em **modelagem de domínio** e em recursos modernos do C#, sem depender de ORM pesado.

## Competências demonstradas
- **DDD tático:** agregados, entidades, value objects, domain services, domain events
- C# moderno: `record`/`record struct`, nullable reference types, `required`, pattern matching
- **Result pattern** vs. exceções para erros de negócio
- **LINQ** (extratos, agrupamentos, paginação) e `async/await` com `CancellationToken`
- **Dapper** + `Microsoft.Data.Sqlite`, transações e **concorrência otimista**
- Generic Host, injeção de dependência, Options pattern, `ILogger`
- Testes com **xUnit, Shouldly e NSubstitute**; testes de arquitetura com **NetArchTest**
- Solution organizada: `Directory.Build.props`, `.editorconfig`, warnings como erro

## Arquitetura planejada
```
Plutus.slnx
├── src/
│   ├── Plutus.Domain/          # Account, Money, Transaction, eventos
│   ├── Plutus.Application/     # casos de uso, interfaces
│   ├── Plutus.Infrastructure/  # Dapper, SQLite, migrations
│   └── Plutus.Cli/             # Spectre.Console + Generic Host
└── tests/
    ├── Plutus.Domain.Tests/
    ├── Plutus.Application.Tests/
    └── Plutus.IntegrationTests/
```

## Como rodar
_A preencher no Dia 10 (publish single-file)._

## Status
Veja o [ROADMAP.md](ROADMAP.md).
