-- Migrazione 0007: registro dei comandi di regia, consegne e outbox (§7, §12.1), prima porzione
-- della regia affidabile di T4.
--
-- Settima versione dello schema, aggiunta senza modificare le precedenti gia' rilasciate (§8.3).
-- Le tre tabelle sono quelle di §7 e realizzano cio' che §12.1 scrive nella stessa transazione del
-- comando: il comando registrato, lo stato di consegna per ogni destinatario e il messaggio da
-- distribuire. La distribuzione (OutboxDispatcher) e le conferme (ACK) arrivano con la porzione
-- successiva: qui il comando si **registra**, e il record outbox e' cio' che rende recuperabile il
-- lavoro pendente se il processo cade fra il commit e l'invio.
--
-- `session_command` e' il comando come e' stato accettato: nome pubblico di §10.4, ambito di §6.5,
-- revisione risultante e payload applicato. L'unicita' `(session_id, revision)` lega una revisione a
-- un solo comando, coerente con l'incremento di revisione a ogni comando di regia; `issued_by_admin_id`
-- resta facoltativo perche' le azioni di sistema non hanno un autore.
--
-- `command_delivery` e' lo stato di consegna **per destinatario**: esiste anche per un client
-- offline, perche' il server non deduce la consegna dalla presenza. La chiave e' composta — comando e
-- partecipante — quindi la tabella non ha un identificativo proprio, come `idempotency_record`.
--
-- `outbox_message` e' la coda dei messaggi persistiti: `available_at_utc_ms` e' l'istante da cui il
-- messaggio e' distribuibile e `processed_at_utc_ms` registra il tentativo concluso, non la conferma
-- del client (§12.2). Il payload e' il documento del messaggio, non una credenziale.
--
CREATE TABLE session_command (
    id                    TEXT    NOT NULL PRIMARY KEY,
    session_id            TEXT    NOT NULL REFERENCES lesson_session(id) ON DELETE RESTRICT,
    -- Autore del comando; nullo soltanto per le azioni di sistema.
    issued_by_admin_id    TEXT    NULL REFERENCES admin_user(id) ON DELETE RESTRICT,
    -- Nome pubblico del comando (§10.4), non il nome del tipo C#.
    command_name          TEXT    NOT NULL CHECK (length(trim(command_name)) > 0),
    scope                 TEXT    NOT NULL CHECK (scope IN ('Class', 'Participant')),
    target_participant_id TEXT    NULL,
    revision              INTEGER NOT NULL CHECK (revision >= 1),
    payload_json          TEXT    NOT NULL CHECK (json_valid(payload_json)),
    issued_at_utc_ms      INTEGER NOT NULL,
    -- Il destinatario esiste soltanto per un comando individuale (§6.5).
    CHECK ((scope = 'Participant') = (target_participant_id IS NOT NULL)),
    -- Una revisione appartiene a un solo comando.
    UNIQUE (session_id, revision)
);

CREATE INDEX ix_session_command_session
ON session_command(session_id);

CREATE TABLE command_delivery (
    command_id         TEXT    NOT NULL REFERENCES session_command(id) ON DELETE RESTRICT,
    participant_id     TEXT    NOT NULL REFERENCES session_participant(id) ON DELETE RESTRICT,
    effective_revision INTEGER NOT NULL CHECK (effective_revision >= 1),
    -- Stati di §12.2: una consegna superata conserva la ricevuta storica ma non torna indietro.
    status             TEXT    NOT NULL CHECK (status IN ('Pending', 'Sent', 'Applied', 'Failed', 'Unconfirmed', 'Superseded')),
    -- Vero quando l'applicazione richiede un recupero (AppliedWithRecoveryRequired, §12.2).
    requires_recovery  INTEGER NOT NULL DEFAULT 0 CHECK (requires_recovery IN (0, 1)),
    sent_at_utc_ms     INTEGER NULL,
    ack_at_utc_ms      INTEGER NULL,
    applied_at_utc_ms  INTEGER NULL,
    error_code         TEXT    NULL,
    send_attempts      INTEGER NOT NULL DEFAULT 0 CHECK (send_attempts >= 0),
    PRIMARY KEY (command_id, participant_id)
);

-- La coda dell'outbox: le consegne non ancora concluse si cercano per stato.
CREATE INDEX ix_command_delivery_pending
ON command_delivery(command_id)
WHERE status IN ('Pending', 'Sent');

CREATE TABLE outbox_message (
    id                  TEXT    NOT NULL PRIMARY KEY,
    session_id          TEXT    NOT NULL REFERENCES lesson_session(id) ON DELETE RESTRICT,
    -- Nome dell'evento hub (§11.3), distinto dal nome del comando che lo produce.
    message_type        TEXT    NOT NULL CHECK (length(trim(message_type)) > 0),
    payload_json        TEXT    NOT NULL CHECK (json_valid(payload_json)),
    available_at_utc_ms INTEGER NOT NULL,
    attempt_count       INTEGER NOT NULL DEFAULT 0 CHECK (attempt_count >= 0),
    processed_at_utc_ms INTEGER NULL,
    last_error_code     TEXT    NULL
);

-- I messaggi da distribuire: non processati, dal piu' vecchio disponibile.
CREATE INDEX ix_outbox_message_pending
ON outbox_message(available_at_utc_ms)
WHERE processed_at_utc_ms IS NULL;
