-- Migrazione 0002: sessione di lezione (§7.4, §7.6), prima porzione di T3.
--
-- Seconda versione dello schema, aggiunta senza modificare la 0001 già applicata (§8.3). Riguarda
-- la riga di sessione e l'unicità della sessione aperta per classe; partecipanti, postazioni,
-- outbox e comandi di regia arrivano con versioni successive.
--
-- Convenzioni di §7.1 riprese dalla 0001: UUID TEXT canonico minuscolo, istanti epoch millisecondi,
-- stati testuali e booleani interi con CHECK, `row_version` sulle righe modificabili.
--
-- `class_default_json` è JSON validato anche dal database con `json_valid`, disponibile nel
-- provider fissato (RepoDb.Sqlite.Microsoft 1.14.0 su Microsoft.Data.Sqlite 10.0.12 e
-- SQLitePCLRaw 2.1.12): il vincolo è mantenuto, non sostituito. Il valore predefinito
-- `{"view":"Waiting"}` lo scrive l'applicazione in `LessonSession.Create`, quindi qui non si
-- aggiunge un DEFAULT che lo duplichi.

CREATE TABLE lesson_session (
    id                  TEXT    NOT NULL PRIMARY KEY,
    lesson_id           TEXT    NOT NULL REFERENCES lesson(id)      ON DELETE RESTRICT,
    classroom_id        TEXT    NOT NULL REFERENCES classroom(id)   ON DELETE RESTRICT,
    controller_admin_id TEXT    NOT NULL REFERENCES admin_user(id)  ON DELETE RESTRICT,
    status              TEXT    NOT NULL CHECK (status IN ('Lobby', 'Active', 'Paused', 'Ended')),
    admissions_open     INTEGER NOT NULL DEFAULT 0 CHECK (admissions_open IN (0, 1)),
    session_revision    INTEGER NOT NULL DEFAULT 1 CHECK (session_revision >= 1),
    class_default_json  TEXT    NOT NULL CHECK (json_valid(class_default_json)),
    started_at_utc_ms   INTEGER NULL,
    ended_at_utc_ms     INTEGER NULL,
    created_at_utc_ms   INTEGER NOT NULL,
    updated_at_utc_ms   INTEGER NOT NULL,
    row_version         INTEGER NOT NULL DEFAULT 1 CHECK (row_version >= 1),
    -- In Ended gli ingressi sono sempre chiusi (§6.1).
    CHECK (status <> 'Ended' OR admissions_open = 0),
    -- Una sessione in Lobby non è ancora iniziata; una avviata lo è (§6.1).
    CHECK ((status = 'Lobby') = (started_at_utc_ms IS NULL)),
    -- Solo una sessione terminata porta l'istante di fine.
    CHECK ((status = 'Ended') = (ended_at_utc_ms IS NOT NULL))
);

-- Al più una sessione non terminata per classe (§7.6): l'indice è parziale perché le sessioni
-- concluse restano nello storico e possono essere più d'una per la stessa classe.
CREATE UNIQUE INDEX ux_session_one_open_per_classroom
ON lesson_session(classroom_id)
WHERE status <> 'Ended';
