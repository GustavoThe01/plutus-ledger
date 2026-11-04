# Roadmap — Plutus (10 dias)

## Semana 1 — Domínio e infraestrutura

### Dia 01 — Solution e padrões de projeto
- [x] `dotnet new sln` + projetos Domain, Application, Infrastructure, Cli e testes
- [x] Referências respeitando a regra de dependência (Domain não referencia ninguém)
- [x] `Directory.Build.props` (Nullable, TreatWarningsAsErrors, LangVersion latest) e `.editorconfig`
- [x] GitHub Actions: `dotnet build` + `dotnet test`
- [x] ADR 0001 — Dapper em vez de EF Core (e por que o Bifrost usará EF Core)
- **Conceitos:** organização de solution, MSBuild

### Dia 02 — Value objects e agregado
- [x] `Money` (`readonly record struct`, `decimal`, moeda), `AccountNumber`
- [x] Agregado `Account` com invariantes (saldo, conta bloqueada, limite)
- [x] Testes unitários escritos antes (TDD)
- **Conceitos:** igualdade por valor, agregado como fronteira de consistência

### Dia 03 — Operações e eventos de domínio
- [x] `Deposit`, `Withdraw` e `TransferService` (domain service)
- [x] **Result pattern** (`Result<T>`) para falhas de negócio
- [x] Domain events (`MoneyTransferred`, `WithdrawalRejected`)
- **Conceitos:** exceção vs. resultado, linguagem ubíqua

### Dia 04 — Camada de aplicação
- [ ] Casos de uso: abrir conta, depositar, sacar, transferir, consultar extrato
- [ ] Interfaces `IAccountRepository`, `IUnitOfWork`
- [ ] DI com `Microsoft.Extensions.DependencyInjection`, Options pattern
- **Conceitos:** orquestração vs. regra de negócio, lifetimes (Singleton/Scoped/Transient)

### Dia 05 — Dapper + SQLite
- [ ] Migrations em scripts SQL embarcados (ou DbUp)
- [ ] Repositórios assíncronos com Dapper
- [ ] Transferência dentro de uma única transação
- **Conceitos:** `async/await`, `IDbTransaction`, mapeamento manual

## Semana 2 — Consultas, concorrência e entrega

### Dia 06 — LINQ e extratos
- [ ] Extrato por período, paginado
- [ ] Agrupamento mensal de entradas/saídas, saldo acumulado
- [ ] Discutir no README: `IEnumerable` vs `IQueryable`, execução adiada
- **Conceitos:** deferred execution, projeções

### Dia 07 — Interface de console
- [ ] Generic Host + Spectre.Console (tabelas, prompts, cores)
- [ ] Logging com `ILogger` e configuração via `appsettings.json`
- **Conceitos:** composition root, configuração por ambiente

### Dia 08 — Concorrência
- [ ] Coluna `Version` e **concorrência otimista** no update de saldo
- [ ] Teste que dispara transferências paralelas e prova que o saldo fica correto
- [ ] `CancellationToken` ponta a ponta
- **Conceitos:** lost update, otimista vs. pessimista

### Dia 09 — Testes avançados
- [ ] Testes de integração com SQLite em memória
- [ ] Mocks com NSubstitute nos casos de uso
- [ ] Testes de arquitetura com NetArchTest; cobertura com Microsoft.Testing.Extensions.CodeCoverage
- **Conceitos:** pirâmide de testes

### Dia 10 — Regras extras e release
- [ ] Tarifas/juros via **Strategy** (conta corrente vs. poupança)
- [ ] `dotnet publish` single-file + seção "Como rodar"
- [ ] Diagrama de domínio (Mermaid), `CHANGELOG.md`, tag `v1.0.0`
- **Conceitos:** polimorfismo, distribuição de aplicações .NET

## Ideias de evolução (opcional)
- Expor os casos de uso numa Minimal API
- Event sourcing do histórico da conta
