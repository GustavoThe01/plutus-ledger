# ADR 0002 — Concorrência: BEGIN IMMEDIATE no SQLite e coluna de versão como garantia

- **Status:** Aceita
- **Data:** 2026-11-09

## Contexto
Duas operações na mesma conta ao mesmo tempo podem causar **lost update**. As duas leem saldo 100;
uma deposita 50 e grava 150; a outra deposita 10 e grava 110. O depósito de 50 some, e o livro-razão
passa a dizer uma coisa e o saldo outra.

Há dois jeitos clássicos de evitar isso:

- **Pessimista:** travar a conta na leitura, e quem chega depois espera.
- **Otimista:** não travar nada, conferir na gravação se a conta ainda está como foi lida e recusar se não estiver.

## Decisão
- A transação continua com **`BEGIN IMMEDIATE`** (padrão do Microsoft.Data.Sqlite). No SQLite, isso reserva a
  escrita desde o início: as operações sobre o banco são serializadas, e quem chega depois lê o saldo já atualizado.
- A tabela `account` ganha a coluna **`version`**. Todo `UPDATE` grava `version + 1` e só acontece
  `WHERE id = @Id AND version = @Version`. Se nenhuma linha for afetada, o repositório lança
  `ConcurrencyConflictException`, a transação é desfeita e o caso de uso devolve o erro
  `application.concurrency_conflict` ("tente de novo").

Ou seja: no SQLite, quem evita o conflito é a trava do banco; a versão é a garantia que **não depende do banco**.
Ela protege o caso de uma conta lida fora da transação e gravada depois, e continua valendo se o Plutus for
para um banco em que leituras não bloqueiam escritas (como o PostgreSQL em `READ COMMITTED`).

## Alternativas consideradas
- **Transação adiada (`BEGIN DEFERRED`) e só a versão:** é o otimista "puro", sem trava na leitura. Mas no SQLite
  duas transações que leram e tentam escrever entram num impasse: uma espera a outra soltar a leitura enquanto a outra
  tenta escrever, e só o timeout resolve. Não serve para um livro-razão.
- **Só `BEGIN IMMEDIATE`, sem versão:** funciona hoje, mas a correção ficaria presa a um detalhe do SQLite.
- **`SELECT ... FOR UPDATE`:** é o pessimista explícito, mas o SQLite não tem esse comando.

## Consequências
- ✅ O teste com 300 transferências paralelas entre três contas mostra o dinheiro total intacto e cada saldo
  igual à soma dos seus lançamentos.
- ✅ Um teste determinístico mostra a versão recusando a gravação de uma conta lida antes de outro depósito,
  exatamente o cenário do lost update.
- ⚠️ No SQLite, as escritas são uma de cada vez. Para um livro-razão de uma instituição, isso é aceitável;
  para muitas escritas por segundo, o caminho seria outro banco.
- ⚠️ Um conflito devolve erro em vez de repetir sozinho. Repetir automaticamente fica para quando houver uma
  situação real de conflito frequente, e com limite de tentativas.
