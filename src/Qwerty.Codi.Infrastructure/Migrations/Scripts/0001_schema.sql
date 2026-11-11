-- Schema di base: installazione pulita dell'intero database (§7, §8.3).
--
-- È l'unico script di migrazione e descrive per intero lo schema corrente. Nasce dal
-- consolidamento degli script 0001-0006 prodotti durante lo sviluppo iniziale: quei passi non sono
-- mai stati distribuiti, e le tabelle di contenuti, allegati, iscrizioni e associazione
-- corso/classe erano già state eliminate dal modello. Un solo file dice com'è fatto il database
-- oggi, senza obbligare a leggere sei passi per ricostruirlo.
--
-- Da qui in avanti vale la regola di §8.3: **una versione applicata non si modifica**. Ogni
-- cambiamento di schema aggiunge `0002_...sql`; questo file cambia solo finché nessuna versione è
-- stata rilasciata, e in quel caso i database esistenti vanno **ricreati, non aggiornati**: la loro
-- impronta registrata in `schema_migration` non corrisponderebbe più e la prontezza li dichiara
-- divergenti invece di applicare le versioni successive su uno schema diverso.
--
-- Righe modificabili con `row_version`, UUID TEXT canonico minuscolo, date epoch millisecondi,
-- booleani con CHECK: le convenzioni sono quelle del dizionario (§7).

CREATE TABLE school (
    id                  TEXT    NOT NULL PRIMARY KEY,
    name                TEXT    NOT NULL CHECK (length(trim(name)) BETWEEN 1 AND 120),
    normalized_name     TEXT    NOT NULL CHECK (length(trim(normalized_name)) BETWEEN 1 AND 120),
    is_archived         INTEGER NOT NULL DEFAULT 0 CHECK (is_archived IN (0, 1)),
    created_at_utc_ms   INTEGER NOT NULL,
    updated_at_utc_ms   INTEGER NOT NULL,
    row_version         INTEGER NOT NULL DEFAULT 1 CHECK (row_version >= 1)
);

CREATE TABLE classroom (
    id                TEXT    NOT NULL PRIMARY KEY,
    school_id         TEXT    NOT NULL REFERENCES school(id) ON DELETE RESTRICT,
    name              TEXT    NOT NULL CHECK (length(trim(name)) BETWEEN 1 AND 120),
    normalized_name   TEXT    NOT NULL CHECK (length(trim(normalized_name)) BETWEEN 1 AND 120),
    -- Anno scolastico in forma canonica AAAA/AAAA. Il dizionario §7.2 lo dichiara non
    -- annullabile e lo include nella chiave di unicità, quindi non è un'etichetta facoltativa:
    -- i due anni consecutivi si verificano nell'applicazione, qui si impone la forma.
    school_year       TEXT    NOT NULL CHECK (school_year GLOB '[0-9][0-9][0-9][0-9]/[0-9][0-9][0-9][0-9]'),
    is_archived       INTEGER NOT NULL DEFAULT 0 CHECK (is_archived IN (0, 1)),
    created_at_utc_ms INTEGER NOT NULL,
    updated_at_utc_ms INTEGER NOT NULL,
    row_version       INTEGER NOT NULL DEFAULT 1 CHECK (row_version >= 1)
);

CREATE TABLE course (
    id                TEXT    NOT NULL PRIMARY KEY,
    title             TEXT    NOT NULL CHECK (length(trim(title)) BETWEEN 1 AND 160),
    -- La descrizione è facoltativa per il docente, non ambigua per il database: vuota è
    -- l'assenza di descrizione, quindi la colonna non è annullabile. Il limite è una scelta
    -- locale reversibile, non un requisito del dizionario.
    description       TEXT    NOT NULL DEFAULT '' CHECK (length(description) <= 2000),
    is_archived       INTEGER NOT NULL DEFAULT 0 CHECK (is_archived IN (0, 1)),
    created_at_utc_ms INTEGER NOT NULL,
    updated_at_utc_ms INTEGER NOT NULL,
    row_version       INTEGER NOT NULL DEFAULT 1 CHECK (row_version >= 1)
);

CREATE TABLE lesson (
    id                TEXT    NOT NULL PRIMARY KEY,
    course_id         TEXT    NOT NULL REFERENCES course(id) ON DELETE RESTRICT,
    title             TEXT    NOT NULL CHECK (length(trim(title)) BETWEEN 1 AND 160),
    -- La descrizione è facoltativa per il docente, non ambigua per il database: vuota è
    -- l'assenza di descrizione. Lunghezze allineate a `course`, che è la stessa natura di testo.
    description       TEXT    NOT NULL DEFAULT '' CHECK (length(description) <= 2000),
    is_archived       INTEGER NOT NULL DEFAULT 0 CHECK (is_archived IN (0, 1)),
    created_at_utc_ms INTEGER NOT NULL,
    updated_at_utc_ms INTEGER NOT NULL,
    row_version       INTEGER NOT NULL DEFAULT 1 CHECK (row_version >= 1)
);

CREATE TABLE exercise (
    id                TEXT    NOT NULL PRIMARY KEY,
    title             TEXT    NOT NULL CHECK (length(trim(title)) BETWEEN 1 AND 160),
    -- La descrizione è facoltativa per il docente, non ambigua per il database: vuota è l'assenza
    -- di descrizione, quindi la colonna non è annullabile. Lunghezze allineate a `course`/`lesson`.
    description       TEXT    NOT NULL DEFAULT '' CHECK (length(description) <= 2000),
    is_archived       INTEGER NOT NULL DEFAULT 0 CHECK (is_archived IN (0, 1)),
    created_at_utc_ms INTEGER NOT NULL,
    updated_at_utc_ms INTEGER NOT NULL,
    row_version       INTEGER NOT NULL DEFAULT 1 CHECK (row_version >= 1)
);

CREATE TABLE lesson_exercise (
    id                TEXT    NOT NULL PRIMARY KEY,
    lesson_id         TEXT    NOT NULL REFERENCES lesson(id) ON DELETE RESTRICT,
    exercise_id       TEXT    NOT NULL REFERENCES exercise(id) ON DELETE RESTRICT,
    -- Le posizioni sono 1..N. `position <> 0` e non `position >= 1` perché il riordino di §7.3
    -- usa posizioni temporanee prima dei valori definitivi: temporanea è un valore negativo,
    -- quindi non collide con nessuna posizione reale sull'indice unico.
    position          INTEGER NOT NULL CHECK (position <> 0),
    created_at_utc_ms INTEGER NOT NULL,
    updated_at_utc_ms INTEGER NOT NULL,
    row_version       INTEGER NOT NULL DEFAULT 1 CHECK (row_version >= 1)
);

CREATE TABLE admin_user (
    id                  TEXT    NOT NULL PRIMARY KEY,
    username            TEXT    NOT NULL CHECK (length(trim(username)) BETWEEN 1 AND 120),
    normalized_username TEXT    NOT NULL CHECK (length(trim(normalized_username)) BETWEEN 1 AND 120),
    password_hash       TEXT    NOT NULL CHECK (length(password_hash) > 0),
    security_stamp      TEXT    NOT NULL CHECK (length(security_stamp) > 0),
    is_active           INTEGER NOT NULL DEFAULT 1 CHECK (is_active IN (0, 1)),
    failed_login_count  INTEGER NOT NULL DEFAULT 0 CHECK (failed_login_count >= 0),
    locked_until_utc_ms INTEGER NULL,
    created_at_utc_ms   INTEGER NOT NULL,
    updated_at_utc_ms   INTEGER NOT NULL,
    row_version         INTEGER NOT NULL DEFAULT 1 CHECK (row_version >= 1)
);

CREATE TABLE credential_session (
    id                TEXT    NOT NULL PRIMARY KEY,
    principal_kind    TEXT    NOT NULL CHECK (principal_kind IN ('Admin', 'Participant')),
    admin_user_id     TEXT    NULL REFERENCES admin_user(id) ON DELETE RESTRICT,
    participant_id    TEXT    NULL,
    security_stamp    TEXT    NOT NULL CHECK (length(security_stamp) > 0),
    issued_at_utc_ms  INTEGER NOT NULL,
    expires_at_utc_ms INTEGER NOT NULL,
    revoked_at_utc_ms INTEGER NULL,
    created_at_utc_ms INTEGER NOT NULL,
    updated_at_utc_ms INTEGER NOT NULL,
    row_version       INTEGER NOT NULL DEFAULT 1 CHECK (row_version >= 1),
    -- Esattamente uno tra amministratore e partecipante, coerente con il tipo (§7.2).
    CHECK (
        (principal_kind = 'Admin' AND admin_user_id IS NOT NULL AND participant_id IS NULL)
        OR (principal_kind = 'Participant' AND participant_id IS NOT NULL AND admin_user_id IS NULL)
    ),
    -- Una credenziale nasce già scaduta soltanto per errore di calcolo.
    CHECK (expires_at_utc_ms > issued_at_utc_ms)
);



CREATE INDEX ix_admin_user_active
ON admin_user(id)
WHERE is_active = 1;

CREATE INDEX ix_classroom_school
ON classroom(school_id);

CREATE INDEX ix_credential_session_expires_at
ON credential_session(expires_at_utc_ms);

CREATE INDEX ix_lesson_course
ON lesson(course_id);

CREATE INDEX ix_lesson_exercise_exercise
ON lesson_exercise(exercise_id);

CREATE UNIQUE INDEX ux_admin_user_normalized_username
ON admin_user(normalized_username);

CREATE UNIQUE INDEX ux_classroom_school_name_year
ON classroom(school_id, normalized_name, school_year);

CREATE UNIQUE INDEX ux_lesson_exercise_position
ON lesson_exercise(lesson_id, position);

CREATE UNIQUE INDEX ux_school_normalized_name
ON school(normalized_name);
