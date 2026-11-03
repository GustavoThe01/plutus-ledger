# ADR 0001 — Dapper em vez de EF Core

- **Status:** Aceita
- **Data:** 2026-11-03

## Contexto
O Plutus é um livro-razão: poucas tabelas, operações de saldo sensíveis a concorrência e
consultas de extrato que precisam ser previsíveis. O objetivo didático do projeto é dominar
a modelagem de domínio e o controle explícito de transações.

## Decisão
Usar **Dapper** com `Microsoft.Data.Sqlite`, SQL escrito à mão e transações explícitas.

## Alternativas consideradas
- **EF Core** — produtividade alta, change tracking e migrations, mas esconde o SQL gerado e
  as transações. Será usado no projeto **Bifrost**, permitindo comparar as duas abordagens.
- **ADO.NET puro** — controle total, porém com muito código repetitivo de mapeamento.

## Consequências
- ✅ SQL e transações visíveis e revisáveis; ótimo para mostrar concorrência otimista.
- ✅ Domínio fica livre de anotações e de exigências de ORM (construtores, setters).
- ⚠️ Mapeamento e migrations são responsabilidade nossa (scripts SQL versionados).
