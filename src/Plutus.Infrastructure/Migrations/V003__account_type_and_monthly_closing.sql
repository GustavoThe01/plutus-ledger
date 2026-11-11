-- Tipo de conta: corrente (padrão das contas que já existem) ou poupança. Poupança não tem limite.
ALTER TABLE account ADD COLUMN type TEXT NOT NULL DEFAULT 'Checking'
    CHECK (type IN ('Checking', 'Savings') AND (type = 'Checking' OR overdraft_limit_cents = 0));

-- Tarifa e rendimento são lançamentos novos. O SQLite não altera um CHECK existente,
-- então a tabela é recriada com a regra nova e os lançamentos são copiados na ordem em que foram gravados.
CREATE TABLE ledger_transaction_new (
    id                  TEXT PRIMARY KEY,
    account_id          TEXT NOT NULL REFERENCES account (id) ON DELETE RESTRICT,
    type                TEXT NOT NULL CHECK (type IN ('Deposit', 'Withdrawal', 'TransferIn', 'TransferOut', 'Fee', 'Interest')),
    amount_cents        INTEGER NOT NULL CHECK (amount_cents > 0),
    balance_after_cents INTEGER NOT NULL,
    occurred_at         TEXT NOT NULL,
    counterparty_id     TEXT REFERENCES account (id) ON DELETE RESTRICT
) STRICT;

INSERT INTO ledger_transaction_new (id, account_id, type, amount_cents, balance_after_cents, occurred_at, counterparty_id)
SELECT id, account_id, type, amount_cents, balance_after_cents, occurred_at, counterparty_id
FROM ledger_transaction
ORDER BY rowid;

DROP TABLE ledger_transaction;
ALTER TABLE ledger_transaction_new RENAME TO ledger_transaction;
CREATE INDEX ix_ledger_transaction_account_date ON ledger_transaction (account_id, occurred_at);

-- Um registro por conta e mês fechado: é o que torna o fechamento idempotente.
CREATE TABLE monthly_closing (
    account_id TEXT NOT NULL REFERENCES account (id) ON DELETE RESTRICT,
    month      TEXT NOT NULL CHECK (month GLOB '[0-9][0-9][0-9][0-9]-[0-1][0-9]'),
    outcome    TEXT NOT NULL CHECK (outcome IN ('Charged', 'Credited', 'NothingToPost')),
    closed_at  TEXT NOT NULL,
    PRIMARY KEY (account_id, month)
) STRICT;
