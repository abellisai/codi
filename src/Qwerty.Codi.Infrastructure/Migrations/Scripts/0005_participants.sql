-- Migrazione 0005: partecipanti, postazioni e stato desiderato (§7.2, §7.4), prima porzione di T3.
--
-- Quinta versione dello schema, aggiunta senza modificare le precedenti gia' rilasciate (§8.3).
-- Introduce l'identita' dell'alunno nella sessione: chi e' entrato e da quale postazione. Nessuna
-- tabella esistente viene toccata. Lo stato desiderato di §6.2 e il registro di idempotenza di
-- §7.5 arrivano con la porzione successiva, quando esistono il join e la credenziale che li
-- scrivono.
--
-- Convenzioni di §7.1 riprese dalle migrazioni precedenti: UUID TEXT canonico minuscolo, istanti
-- epoch millisecondi, stati testuali e booleani interi con CHECK, `row_version` sulle righe
-- modificabili, JSON sempre validato dal database con `json_valid`.
--
-- `credential_session` esiste dalla 0001 e ha gia' `participant_id`, ma senza chiave esterna:
-- la tabella dei partecipanti non esisteva. Aggiungerla ora richiederebbe di ricostruire la
-- tabella, quindi la coerenza fra credenziale e partecipazione resta verificata dall'applicazione:
-- e' una scelta locale registrata in §7.2.
--
-- `station` e' la postazione fisica: una chiave casuale generata dal browser e conservata come
-- impronta, mai un'impronta digitale del dispositivo (§7.2). La chiave identifica il PC, quindi e'
-- unica; `classroom_id` resta un contesto facoltativo, perche' lo stesso PC puo' essere usato in
-- classi diverse in momenti diversi e la postazione non appartiene a una classe.
--
-- `session_participant` e' l'identita' dell'alunno **nella sessione**: il nome appartiene alla
-- partecipazione, non alla persona, e nomi uguali sono ammessi (§5.2). L'unicita' `(session_id, id)`
-- esiste per le chiavi esterne composte delle tabelle che seguiranno, non per se stessa: il
-- partecipante e' gia' identificato dal suo `id`.
--
CREATE TABLE station (
    id                  TEXT    NOT NULL PRIMARY KEY,
    -- Contesto facoltativo: la classe in cui la postazione e' stata vista la prima volta.
    classroom_id        TEXT    NULL REFERENCES classroom(id) ON DELETE RESTRICT,
    label               TEXT    NULL,
    -- Impronta della chiave casuale del browser: la chiave in chiaro non si conserva.
    browser_key_hash    TEXT    NOT NULL CHECK (length(trim(browser_key_hash)) > 0),
    last_seen_at_utc_ms INTEGER NULL,
    created_at_utc_ms   INTEGER NOT NULL,
    updated_at_utc_ms   INTEGER NOT NULL,
    row_version         INTEGER NOT NULL DEFAULT 1 CHECK (row_version >= 1)
);

-- Una postazione per browser: la chiave e' casuale e rigenerarla creerebbe una postazione nuova,
-- quindi l'unicita' impedisce soltanto che lo stesso PC entri due volte nel registro.
CREATE UNIQUE INDEX ux_station_browser_key
ON station(browser_key_hash);

CREATE TABLE session_participant (
    id                TEXT    NOT NULL PRIMARY KEY,
    session_id        TEXT    NOT NULL REFERENCES lesson_session(id) ON DELETE RESTRICT,
    station_id        TEXT    NULL REFERENCES station(id) ON DELETE RESTRICT,
    -- Nome visualizzato, 1-30 grafemi. Il limite e' sui grafemi e non sui caratteri, quindi lo
    -- verifica l'applicazione (§7.4): il CHECK resta una rete di sicurezza a maglia larga, perche'
    -- un grafema puo' essere composto da piu' punti di codice.
    display_name      TEXT    NOT NULL CHECK (length(display_name) > 0 AND length(display_name) <= 120),
    admission_status  TEXT    NOT NULL CHECK (admission_status IN ('Admitted', 'Excluded')),
    -- Epoca della connessione operativa (§5.3, §12.5): zero significa «mai acquisita».
    connection_epoch  INTEGER NOT NULL DEFAULT 0 CHECK (connection_epoch >= 0),
    joined_at_utc_ms  INTEGER NOT NULL,
    excluded_at_utc_ms INTEGER NULL,
    created_at_utc_ms INTEGER NOT NULL,
    updated_at_utc_ms INTEGER NOT NULL,
    row_version       INTEGER NOT NULL DEFAULT 1 CHECK (row_version >= 1),
    -- Solo una partecipazione esclusa porta l'istante di esclusione (§7.4).
    CHECK ((admission_status = 'Excluded') = (excluded_at_utc_ms IS NOT NULL)),
    -- Chiave candidata per le chiavi esterne composte delle tabelle successive.
    UNIQUE (session_id, id)
);

CREATE INDEX ix_session_participant_session
ON session_participant(session_id);
