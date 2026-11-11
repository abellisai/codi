-- Migrazione 0004: autoraggio degli esercizi Blockly (§7.3), prima porzione di T6.
--
-- Quarta versione dello schema, aggiunta senza modificare le precedenti già rilasciate (§8.3).
-- Introduce le tre tabelle dell'autore di esercizi: la bozza di lavoro, le versioni pubblicate e
-- i contenuti privati della versione che non escono mai verso gli alunni. Nessuna tabella esistente
-- viene toccata: le colonne restano quelle della 0001-0003.
--
-- Convenzioni di §7.1 riprese dalle migrazioni precedenti: UUID TEXT canonico minuscolo, istanti
-- epoch millisecondi, JSON validato dal database con `json_valid` (disponibile nel provider
-- fissato, come nella 0002), `row_version` sulle righe modificabili. Tutti i campi JSON sono
-- `TEXT NOT NULL CHECK (json_valid(<colonna>))`, annullabili solo dove la specifica mette il `?`.
--
-- `exercise_draft` è la bozza in corso di modifica: una sola per esercizio, quindi la chiave
-- primaria è l'`exercise_id` e coincide con quella di `exercise` (§7.3). È una riga modificabile e
-- porta `row_version`. La specifica cita solo `row_version` fra le colonne proprie della bozza:
-- `created_at_utc_ms` e `updated_at_utc_ms` sono una scelta locale coerente con §7.1 («ogni tabella
-- di dominio ha created_at_utc_ms; quelle modificabili anche updated_at_utc_ms»).
--
-- `exercise_version` è una versione pubblicata e immutabile (§7.3): non ha `updated_at_utc_ms` e
-- non ha `row_version`, perché le tabelle immutabili non lo richiedono (§7.1). L'immutabilità è
-- garantita dal database con un trigger `BEFORE UPDATE`, così non dipende soltanto dal fatto che
-- l'applicazione non offra un percorso di aggiornamento. L'unicità `(exercise_id, version_number)`
-- impedisce di pubblicare due volte lo stesso numero per lo stesso esercizio.
--
-- `exercise_version_private` è il completamento riservato al docente: la soluzione di riferimento e
-- le note dell'autore, legate uno-a-uno alla versione. La specifica non la collega a un utente e non
-- le dà `row_version` né istanti, perché nasce e muore con la versione pubblicata.

CREATE TABLE exercise_draft (
    exercise_id             TEXT    NOT NULL PRIMARY KEY REFERENCES exercise(id) ON DELETE RESTRICT,
    definition_json         TEXT    NOT NULL CHECK (json_valid(definition_json)),
    workspace_json          TEXT    NOT NULL CHECK (json_valid(workspace_json)),
    -- Soluzione di riferimento facoltativa: assente è NULL, presente è JSON valido.
    reference_solution_json TEXT    NULL CHECK (reference_solution_json IS NULL OR json_valid(reference_solution_json)),
    created_at_utc_ms       INTEGER NOT NULL,
    updated_at_utc_ms       INTEGER NOT NULL,
    row_version             INTEGER NOT NULL DEFAULT 1 CHECK (row_version >= 1)
);

CREATE TABLE exercise_version (
    id                     TEXT    NOT NULL PRIMARY KEY,
    exercise_id            TEXT    NOT NULL REFERENCES exercise(id)   ON DELETE RESTRICT,
    version_number         INTEGER NOT NULL CHECK (version_number >= 1),
    definition_json        TEXT    NOT NULL CHECK (json_valid(definition_json)),
    initial_workspace_json TEXT    NOT NULL CHECK (json_valid(initial_workspace_json)),
    -- Metadati tecnici della versione. La specifica non ne fissa la lunghezza: si impone soltanto
    -- che non siano stringhe vuote o di soli spazi, come per le impronte della 0001.
    engine_version         TEXT    NOT NULL CHECK (length(trim(engine_version)) > 0),
    schema_version         TEXT    NOT NULL CHECK (length(trim(schema_version)) > 0),
    content_hash           TEXT    NOT NULL CHECK (length(trim(content_hash)) > 0),
    published_by_admin_id  TEXT    NOT NULL REFERENCES admin_user(id) ON DELETE RESTRICT,
    created_at_utc_ms      INTEGER NOT NULL
);

-- Un solo numero di versione per esercizio (§7.3). L'indice unico copre anche la ricerca per
-- `exercise_id`, quindi non serve un indice separato su quella colonna.
CREATE UNIQUE INDEX ux_exercise_version_number
ON exercise_version(exercise_id, version_number);

CREATE TABLE exercise_version_private (
    exercise_version_id     TEXT NOT NULL PRIMARY KEY REFERENCES exercise_version(id) ON DELETE RESTRICT,
    -- Soluzione di riferimento facoltativa, riservata al docente (§7.3): mai nei DTO degli alunni.
    reference_solution_json TEXT NULL CHECK (reference_solution_json IS NULL OR json_valid(reference_solution_json)),
    -- Note dell'autore: testo libero e facoltativo. La specifica non fissa una lunghezza massima,
    -- quindi qui non se ne inventa una.
    author_notes            TEXT NULL
);

-- Una versione pubblicata non si modifica: al suo posto se ne pubblica una nuova (§7.3).
CREATE TRIGGER trg_exercise_version_immutable
BEFORE UPDATE ON exercise_version
BEGIN
    SELECT RAISE(ABORT, 'exercise_version e immutabile: pubblicare una nuova versione');
END;
