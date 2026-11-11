-- Migrazione 0012: il terzo tipo di esercizio entra fra quelli registrati (§13.9).
--
-- La dodicesima versione dello schema, aggiunta senza modificare le precedenti già rilasciate (§8.3).
-- Non aggiunge colonne e non riscrive dati: estende il vincolo sul tipo, che la migrazione 0011 aveva
-- fissato ai due tipi allora registrati.
--
-- Il vincolo vive nel database e non legge il registro dei motori, quindi ogni tipo nuovo richiede
-- una migrazione. Questa è la conseguenza voluta di quella scelta: un valore sconosciuto non entra
-- nel database per errore di un percorso che non è quello di creazione, e l'elenco ammesso è scritto
-- dove il database lo può far rispettare.
--
-- I trigger si ricreano invece di estendersi: SQLite non permette di modificare un trigger, e la
-- forma nuova è la stessa con un valore in più.

DROP TRIGGER IF EXISTS trg_exercise_type_known;
DROP TRIGGER IF EXISTS trg_exercise_type_known_update;

CREATE TRIGGER trg_exercise_type_known
BEFORE INSERT ON exercise
WHEN NEW.exercise_type NOT IN ('GridPath', 'Quiz', 'ValueProgram')
BEGIN
    SELECT RAISE(ABORT, 'exercise_type non e un tipo registrato');
END;

CREATE TRIGGER trg_exercise_type_known_update
BEFORE UPDATE OF exercise_type ON exercise
WHEN NEW.exercise_type NOT IN ('GridPath', 'Quiz', 'ValueProgram')
BEGIN
    SELECT RAISE(ABORT, 'exercise_type non e un tipo registrato');
END;
