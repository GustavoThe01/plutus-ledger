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
