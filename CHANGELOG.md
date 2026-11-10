# Changelog

Formato baseado em [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/)
e [Versionamento Semântico](https://semver.org/lang/pt-BR/).

## [Não lançado]

### Adicionado
- Solution `Plutus.slnx` com projetos Domain, Application, Infrastructure e testes.
- `Directory.Build.props` (Nullable, warnings como erro), Central Package Management e `.editorconfig`.
- Teste de arquitetura garantindo que Domain não depende das outras camadas.
- Pipeline de CI no GitHub Actions.
- Projeto de console `Plutus.Cli` (composition root).
- ADR 0001 — Dapper em vez de EF Core.
- Value objects `Money` (com moeda ISO 4217) e `AccountNumber` (com dígito verificador de Luhn).
- Agregado `Account` com abertura, limite, bloqueio, desbloqueio e encerramento, protegendo a invariante `saldo >= -limite`.
- 40 testes unitários do domínio.
- `Result` e `Result<T>` para falhas de negócio esperadas, com códigos estáveis em `AccountErrors`.
- Depósito e saque em `Account`, respeitando saldo e limite, com cada operação registrada como `Transaction` imutável.
- Serviço de domínio `TransferService`, que valida o crédito antes de debitar a origem.
- Eventos de domínio `MoneyTransferred` e `WithdrawalRejected`.
- 17 testes novos.
- Casos de uso `OpenAccount`, `DepositMoney`, `WithdrawMoney`, `TransferMoney` e `GetStatement`.
- Portas `IAccountRepository` e `IUnitOfWork`; a implementação com Dapper entra no Dia 05.
- Injeção de dependência com `AddApplication` e configuração validada em `LedgerOptions`.
- Projeto `Plutus.Application.Tests`, com 16 testes usando NSubstitute.
- Persistência com Dapper e SQLite: `AccountRepository`, SQL escrito à mão e valores em centavos.
- Migrations em scripts SQL embutidos, aplicadas por `DatabaseMigrator`.
- `UnitOfWork` com transação explícita: exceções e falhas de negócio desfazem tudo o que foi gravado.
- Projeto `Plutus.IntegrationTests`, com 11 testes contra um SQLite real.
- Extrato paginado (`Page<T>`), com descrição de cada lançamento e valores com sinal: entradas positivas, saídas negativas.
- Caso de uso `GetMonthlySummary`: entradas, saídas, saldo do mês e saldo de fechamento, agrupados com LINQ.
- `LedgerOptions.MaxPageSize` limita o tamanho da página do extrato.
- README com a discussão de `IEnumerable` vs `IQueryable` e execução adiada.
- 7 testes novos.
- Interface de console com Spectre.Console: menu, tabelas, painéis, cores e confirmação antes de transferir.
- Generic Host como composition root: configuração em `appsettings.json` e `appsettings.{Ambiente}.json`, DI e `ILogger`.
- Caso de uso `GetAccount` para consultar saldo, limite e disponível.
- Projeto `Plutus.Cli.Tests`, com 6 testes que simulam o teclado e rodam contra um SQLite real.
- Coluna `version` na conta (migration `V002`): todo `UPDATE` confere a versão lida e grava a próxima.
- `ConcurrencyConflictException` e o erro `application.concurrency_conflict`, que desfaz a transação e pede para tentar de novo.
- Teste com 300 transferências paralelas num SQLite em arquivo, provando que nenhum centavo é criado ou perdido.
- Teste do lost update: a gravação de uma conta lida antes de outro depósito é recusada.
- Teste de cancelamento: operação cancelada não grava nada.
- ADR 0002 — `BEGIN IMMEDIATE` no SQLite e coluna de versão como garantia.
- Projeto `Plutus.ArchitectureTests` com 7 regras de camada em NetArchTest (fork NetArchTest.eNhancedEdition 1.4.5).
- Cobertura de código pelo Microsoft.Testing.Platform (`dotnet test --coverage`), ligada em todos os projetos de teste por `tests/Directory.Build.props`.
- Cobertura medida: 92,4% das linhas, somando os cinco projetos de teste.
