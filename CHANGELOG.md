# Changelog

Formato baseado em [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/)
e [Versionamento Semântico](https://semver.org/lang/pt-BR/).

## [Não lançado]

## [1.0.0] - 2026-11-11

Primeira versão completa: os 10 dias do roadmap.

### Adicionado
- Tipo de conta (`AccountType`): conta corrente ou poupança. Poupança não aceita limite de cheque especial, nem na abertura nem depois.
- Regras de fim de mês com **Strategy** (`IMonthlyPolicy`): `CheckingAccountFee` (tarifa com isenção por saldo) e `SavingsInterest` (rendimento truncado no centavo).
- Lançamentos `Fee` e `Interest`, com `Account.ChargeFee` e `Account.CreditInterest`.
- Caso de uso `CloseMonth`, idempotente por conta e mês (tabela `monthly_closing`), que só aceita meses encerrados.
- Migration `V003`: coluna `type` na conta, tabela de lançamentos recriada com os tipos novos no `CHECK` e tabela `monthly_closing`.
- `LedgerOptions`: `CheckingMonthlyFee`, `CheckingFeeExemptBalance` e `SavingsMonthlyRate`, validados na partida.
- Console: escolha do tipo de conta na abertura (poupança não pergunta limite), tipo no painel da conta e opção "Fechar o mês".
- Publicação single-file e self-contained (`dotnet publish -r win-x64` ou `-r linux-x64`).
- Diagrama de domínio atualizado e seção do README sobre tarifas e rendimentos.
- 38 testes novos (147 no total). Um deles aplica a V003 sobre um banco do Dia 09 com dados.
- Cobertura medida: 93,6% das linhas.

### Alterado
- O console lê o `appsettings.json` da pasta do executável, e não da pasta de onde foi chamado.
- Com a entrada redirecionada (arquivo ou pipe), o console avisa que precisa de um terminal interativo em vez de lançar uma exceção.

### Adicionado (Dias 01 a 09)
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

[Não lançado]: https://github.com/GustavoThe01/plutus-ledger/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/GustavoThe01/plutus-ledger/releases/tag/v1.0.0
