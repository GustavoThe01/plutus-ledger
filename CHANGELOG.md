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
