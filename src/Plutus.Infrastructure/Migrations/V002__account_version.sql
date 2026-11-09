-- Versão da conta para concorrência otimista: todo UPDATE confere a versão lida e grava a próxima.
-- Se outra operação gravou antes, a versão não bate, o UPDATE não altera nenhuma linha e a operação é recusada.
ALTER TABLE account ADD COLUMN version INTEGER NOT NULL DEFAULT 0 CHECK (version >= 0);
