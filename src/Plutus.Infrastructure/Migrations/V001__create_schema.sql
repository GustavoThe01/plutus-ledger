-- Dinheiro em centavos e datas em ISO 8601 UTC, que podem ser comparadas como texto.
CREATE TABLE account (
    id                    TEXT PRIMARY KEY,
    number                TEXT NOT NULL UNIQUE,
    holder_name           TEXT NOT NULL,
    balance_cents         INTEGER NOT NULL,
    overdraft_limit_cents INTEGER NOT NULL CHECK (overdraft_limit_cents >= 0),
    currency              TEXT NOT NULL CHECK (length(currency) = 3),
    status                TEXT NOT NULL CHECK (status IN ('Active', 'Blocked', 'Closed')),
    -- Segunda linha de defesa: mesmo um bug na aplicação não consegue gravar um saldo além do limite.
    CHECK (balance_cents >= -overdraft_limit_cents)
) STRICT;

-- Livro-razão: os lançamentos só são inseridos, nunca alterados.
CREATE TABLE ledger_transaction (
    id                  TEXT PRIMARY KEY,
    account_id          TEXT NOT NULL REFERENCES account (id) ON DELETE RESTRICT,
    type                TEXT NOT NULL CHECK (type IN ('Deposit', 'Withdrawal', 'TransferIn', 'TransferOut')),
    amount_cents        INTEGER NOT NULL CHECK (amount_cents > 0),
    balance_after_cents INTEGER NOT NULL,
    occurred_at         TEXT NOT NULL,
    counterparty_id     TEXT REFERENCES account (id) ON DELETE RESTRICT
) STRICT;

CREATE INDEX ix_ledger_transaction_account_date ON ledger_transaction (account_id, occurred_at);
