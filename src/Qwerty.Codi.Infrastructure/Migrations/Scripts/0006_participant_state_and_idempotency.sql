-- Migrazione 0006: stato desiderato e idempotenza del join (§7.4, §7.5), seconda porzione di T3.
--
-- Sesta versione dello schema, aggiunta senza modificare le precedenti gia' rilasciate (§8.3).
-- Con la 0005 esistono la postazione e la partecipazione; qui arrivano le due tabelle che il join
-- scrive: lo stato desiderato materializzato di §6.2 e il registro di idempotenza di §9.4.
--
-- `participant_state` e' lo stato che il server vuole per un partecipante: alla creazione e' la
-- copia del default di classe, poi lo modificano i comandi di regia. La chiave esterna composta
-- rende impossibile uno stato che riferisca una sessione diversa da quella della partecipazione.
-- `assignment_id` e `attempt_id` sono le colonne di §7.4 e restano senza chiave esterna finche'
-- `activity_assignment` e `exercise_attempt` non esistono (T4/T5): i loro valori li verifica
-- l'applicazione.
--
-- `idempotency_record` e' il registro di §7.5. La chiave e' la coppia (scope, chiave della
-- richiesta): lo scope e' l'**impronta** del cookie di ammissione e non il cookie, perche' il
-- registro non memorizza credenziali (§7.5). `response_json` contiene solo riferimenti alle entita'
-- create, senza marche di sicurezza: ripetere la stessa richiesta restituisce la stessa
-- partecipazione senza che il registro diventi un deposito di segreti.

CREATE TABLE participant_state (
    participant_id       TEXT    NOT NULL PRIMARY KEY,
    session_id           TEXT    NOT NULL,
    -- Revisione della sessione da cui questo stato e' stato copiato (§6.2): non e' contigua.
    effective_revision   INTEGER NOT NULL CHECK (effective_revision >= 0),
    assignment_id        TEXT    NULL,
    attempt_id           TEXT    NULL,
    view                 TEXT    NOT NULL CHECK (view IN ('Waiting', 'Exercise', 'Goodbye')),
    interaction_enabled  INTEGER NOT NULL DEFAULT 0 CHECK (interaction_enabled IN (0, 1)),
    teacher_paused       INTEGER NOT NULL DEFAULT 0 CHECK (teacher_paused IN (0, 1)),
    recovery_blocked     INTEGER NOT NULL DEFAULT 0 CHECK (recovery_blocked IN (0, 1)),
    execution_generation INTEGER NOT NULL DEFAULT 0 CHECK (execution_generation >= 0),
    directive_json       TEXT    NULL CHECK (directive_json IS NULL OR json_valid(directive_json)),
    resume_context_json  TEXT    NULL CHECK (resume_context_json IS NULL OR json_valid(resume_context_json)),
    created_at_utc_ms    INTEGER NOT NULL,
    updated_at_utc_ms    INTEGER NOT NULL,
    row_version          INTEGER NOT NULL DEFAULT 1 CHECK (row_version >= 1),
    -- Uno stato per partecipante, e riferito alla sessione della sua partecipazione (§7.4).
    FOREIGN KEY (session_id, participant_id)
        REFERENCES session_participant(session_id, id) ON DELETE RESTRICT
);

CREATE TABLE idempotency_record (
    -- Impronta dello scope dell'ingresso: mai il cookie o una credenziale.
    scope_key         TEXT    NOT NULL CHECK (length(trim(scope_key)) > 0),
    request_key       TEXT    NOT NULL CHECK (length(trim(request_key)) > 0),
    -- Impronta del contenuto della richiesta: la stessa chiave con un contenuto diverso e'
    -- un conflitto, non una ripetizione (§9.4).
    request_hash      TEXT    NOT NULL CHECK (length(trim(request_hash)) > 0),
    response_code     INTEGER NOT NULL,
    response_json     TEXT    NOT NULL CHECK (json_valid(response_json)),
    expires_at_utc_ms INTEGER NOT NULL,
    PRIMARY KEY (scope_key, request_key)
);

-- La scadenza si cerca per data: serve alla manutenzione del registro, non alle letture del join.
CREATE INDEX ix_idempotency_record_expires
ON idempotency_record(expires_at_utc_ms);
