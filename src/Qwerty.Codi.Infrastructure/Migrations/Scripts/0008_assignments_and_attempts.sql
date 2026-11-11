-- Migrazione 0008: assegnazione dell'esercizio e tentativi (§7.3, §6.5), prima parte di T4.
--
-- Ottava versione dello schema, aggiunta senza modificare le precedenti gia' rilasciate (§8.3).
-- Con la 0007 esistono comando, consegne e coda; qui arrivano le due tabelle che il comando di
-- assegnazione scrive: quale versione pubblicata e' stata assegnata e che cosa ogni alunno ha
-- ricevuto per lavorarci.
--
-- `activity_assignment` fissa la **versione pubblicata** dell'esercizio: la sequenza della lezione
-- riferisce l'identita' dell'esercizio (§7.3), quindi una modifica successiva alla sequenza non
-- cambia il materiale gia' assegnato. `scope` vale `Class` per tutta la classe o `Participant` per
-- un solo destinatario, e `target_participant_id` esiste soltanto nel secondo caso: e' lo stesso
-- vincolo di coerenza di §6.5, scritto una volta nel database.
--
-- `exercise_attempt` e' il lavoro di un alunno su quell'assegnazione. Il numero e' progressivo per
-- coppia partecipante/assegnazione, cosi' un rizzero conserva il tentativo precedente invece di
-- sovrascriverlo. `status` non e' enumerato dalla specifica: i valori ammessi sono una **scelta
-- locale** registrata in §7.3 e descrivono il ciclo di vita osservabile — `Ready` (pronto e
-- bloccato, come nasce il tentativo), `Running`, `Paused`, `Completed`, `Archived`.
--
-- Due scelte locali di coerenza, sullo stile della 0006. La prima: `exercise_attempt` porta anche
-- `session_id`, che la tabella di §7.3 non elenca, per rendere impossibile un tentativo che riferisca
-- una partecipazione di un'altra sessione o un'assegnazione di un'altra lezione. La seconda:
-- `activity_assignment` espone due chiavi candidate — `(session_id, id)` e `(id,
-- exercise_version_id)` — che servono alle chiavi esterne composte delle tabelle successive, non a se
-- stesse; la seconda realizza il vincolo «riferimento versione coerente con assegnazione».
--
-- Convenzioni di §7.1 riprese dalle migrazioni precedenti: UUID TEXT canonico minuscolo, istanti
-- epoch millisecondi, stati testuali e booleani interi con CHECK, `row_version` sulle righe
-- modificabili, JSON sempre validato dal database con `json_valid`.

CREATE TABLE activity_assignment (
    id                  TEXT    NOT NULL PRIMARY KEY,
    session_id          TEXT    NOT NULL REFERENCES lesson_session(id) ON DELETE RESTRICT,
    -- Casella della sequenza da cui l'assegnazione e' nata: assente per un'assegnazione diretta.
    lesson_exercise_id  TEXT    NULL REFERENCES lesson_exercise(id) ON DELETE RESTRICT,
    scope               TEXT    NOT NULL CHECK (scope IN ('Class', 'Participant')),
    target_participant_id TEXT  NULL,
    -- Versione pubblicata: immutabile, quindi il materiale assegnato non cambia sotto gli alunni.
    exercise_version_id TEXT    NOT NULL REFERENCES exercise_version(id) ON DELETE RESTRICT,
    created_at_utc_ms   INTEGER NOT NULL,
    -- Un destinatario esiste solo per un'assegnazione individuale (§6.5).
    CHECK ((scope = 'Participant') = (target_participant_id IS NOT NULL)),
    -- Il destinatario deve appartenere alla sessione dell'assegnazione.
    FOREIGN KEY (session_id, target_participant_id)
        REFERENCES session_participant(session_id, id) ON DELETE RESTRICT,
    -- Chiavi candidate per le chiavi esterne composte di `exercise_attempt`.
    UNIQUE (session_id, id),
    UNIQUE (id, exercise_version_id)
);

CREATE INDEX ix_activity_assignment_session
ON activity_assignment(session_id, created_at_utc_ms);

CREATE TABLE exercise_attempt (
    id                        TEXT    NOT NULL PRIMARY KEY,
    -- Non elencata in §7.3: rende verificabile dal database la coerenza fra partecipazione e
    -- assegnazione, come scelta locale registrata nel commento in testa alla migrazione.
    session_id                TEXT    NOT NULL,
    participant_id            TEXT    NOT NULL,
    assignment_id             TEXT    NOT NULL,
    exercise_version_id       TEXT    NOT NULL,
    attempt_number            INTEGER NOT NULL CHECK (attempt_number >= 1),
    status                    TEXT    NOT NULL CHECK (status IN ('Ready', 'Running', 'Paused', 'Completed', 'Archived')),
    workspace_json            TEXT    NOT NULL CHECK (json_valid(workspace_json)),
    workspace_hash            TEXT    NOT NULL CHECK (length(trim(workspace_hash)) > 0),
    project_revision          INTEGER NOT NULL DEFAULT 0 CHECK (project_revision >= 0),
    checkpoint_json           TEXT    NULL CHECK (checkpoint_json IS NULL OR json_valid(checkpoint_json)),
    checkpoint_engine_version TEXT    NULL,
    last_saved_at_utc_ms      INTEGER NULL,
    completed_at_utc_ms       INTEGER NULL,
    created_at_utc_ms         INTEGER NOT NULL,
    updated_at_utc_ms         INTEGER NOT NULL,
    row_version               INTEGER NOT NULL DEFAULT 1 CHECK (row_version >= 1),
    -- Un secondo tentativo con lo stesso numero non esiste: la numerazione e' per assegnazione.
    UNIQUE (participant_id, assignment_id, attempt_number),
    -- Il tentativo appartiene a una partecipazione della propria sessione e a un'assegnazione della
    -- stessa sessione, con la versione dell'esercizio che l'assegnazione ha fissato.
    FOREIGN KEY (session_id, participant_id)
        REFERENCES session_participant(session_id, id) ON DELETE RESTRICT,
    FOREIGN KEY (session_id, assignment_id)
        REFERENCES activity_assignment(session_id, id) ON DELETE RESTRICT,
    FOREIGN KEY (assignment_id, exercise_version_id)
        REFERENCES activity_assignment(id, exercise_version_id) ON DELETE RESTRICT
);

CREATE INDEX ix_exercise_attempt_assignment
ON exercise_attempt(assignment_id, attempt_number);

CREATE INDEX ix_exercise_attempt_participant
ON exercise_attempt(participant_id, created_at_utc_ms);
