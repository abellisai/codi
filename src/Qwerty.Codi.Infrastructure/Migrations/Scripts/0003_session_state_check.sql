-- Migrazione 0003: coerenza fra stato e istanti di `lesson_session` (§6.1, §7.4).
--
-- La 0002 imponeva `(status = 'Lobby') = (started_at_utc_ms IS NULL)`, cioè "non in Lobby" uguale a
-- "avvio registrato". Quel vincolo vieta di concludere una sessione mai avviata, mentre §6.1
-- ammette `Ended` da qualunque stato non terminale, `Lobby` compreso: una lezione istanziata e mai
-- avviata si chiude senza aver registrato un avvio. Il difetto è emerso dal caso d'uso
-- `EndSession` su una sessione in `Lobby`, che il database rifiutava.
--
-- Il vincolo corretto distingue i tre casi: `Lobby` non ha avvio, `Active` e `Paused` ne hanno uno,
-- `Ended` può non averlo. La coerenza dell'istante di fine resta quella già scritta — solo una
-- sessione terminata lo porta — e non cambia.
--
-- SQLite non permette di modificare un `CHECK` esistente, quindi la tabella si ricostruisce con la
-- procedura raccomandata: nuova tabella, copia, sostituzione. Nessun'altra colonna o vincolo
-- cambia; nessuna tabella riferisce `lesson_session`, quindi la sostituzione non lascia riferimenti
-- pendenti e non serve disattivare le chiavi esterne (che dentro una transazione non si potrebbero
-- comunque cambiare). L'indice parziale segue la tabella: si ricrea dopo la sostituzione.

CREATE TABLE lesson_session_rebuilt (
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
    -- Lobby non è iniziata; Active e Paused lo sono; Ended può non esserlo, perché si può
    -- concludere una sessione mai avviata (§6.1).
    CHECK (
        (status = 'Lobby' AND started_at_utc_ms IS NULL)
        OR (status IN ('Active', 'Paused') AND started_at_utc_ms IS NOT NULL)
        OR status = 'Ended'
    ),
    -- Solo una sessione terminata porta l'istante di fine.
    CHECK ((status = 'Ended') = (ended_at_utc_ms IS NOT NULL))
);

INSERT INTO lesson_session_rebuilt (
    id, lesson_id, classroom_id, controller_admin_id, status, admissions_open, session_revision,
    class_default_json, started_at_utc_ms, ended_at_utc_ms, created_at_utc_ms, updated_at_utc_ms,
    row_version)
SELECT
    id, lesson_id, classroom_id, controller_admin_id, status, admissions_open, session_revision,
    class_default_json, started_at_utc_ms, ended_at_utc_ms, created_at_utc_ms, updated_at_utc_ms,
    row_version
FROM lesson_session;

DROP TABLE lesson_session;

ALTER TABLE lesson_session_rebuilt RENAME TO lesson_session;

-- Al più una sessione non terminata per classe (§7.6).
CREATE UNIQUE INDEX ux_session_one_open_per_classroom
ON lesson_session(classroom_id)
WHERE status <> 'Ended';
