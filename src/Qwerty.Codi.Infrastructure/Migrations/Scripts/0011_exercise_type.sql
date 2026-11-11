-- Migrazione 0011: il tipo di attività sull'anagrafica dell'esercizio (§13.9).
--
-- L'undicesima versione dello schema, aggiunta senza modificare le precedenti già rilasciate (§8.3).
-- Aggiunge una colonna a `exercise`; nessuna tabella esistente cambia forma e nessun dato viene
-- riscritto.
--
-- Perché il tipo sta anche qui e non solo nella definizione. La definizione della bozza contiene già
-- `exerciseType`, ma la bozza può non esistere: quando il docente crea un esercizio, l'editor deve
-- sapere quali campi mostrare, e senza il tipo sull'identità non avrebbe nulla da cui dedurlo. Il
-- tipo è l'unica informazione della definizione che vive anche sull'anagrafica.
--
-- `DEFAULT 'GridPath'` non è un ripiego silenzioso: è la migrazione degli esercizi già esistenti,
-- che sono tutti esercizi di percorso perché il registro dei motori ne conosceva uno solo. Un
-- valore predefinito diverso inventerebbe un tipo che nessuno ha scelto.
--
-- Il tipo non cambia dopo la creazione: un esercizio già assegnato ha versioni pubblicate che
-- dichiarano il loro tipo, e cambiarlo sull'anagrafica le renderebbe incoerenti con l'identità.
-- L'`UPDATE` dell'anagrafica non tocca questa colonna, quindi il vincolo è nel codice, non qui.

ALTER TABLE exercise ADD COLUMN exercise_type TEXT NOT NULL DEFAULT 'GridPath';

-- Il tipo dichiarato deve essere uno di quelli che il prodotto sa interpretare. I valori sono la
-- copia della prima registrazione del registro dei motori; aggiungere un tipo significa una nuova
-- migrazione che estende questo vincolo, perché il database non legge il registro.
CREATE TRIGGER trg_exercise_type_known
BEFORE INSERT ON exercise
WHEN NEW.exercise_type NOT IN ('GridPath', 'Quiz')
BEGIN
    SELECT RAISE(ABORT, 'exercise_type non e un tipo registrato');
END;

CREATE TRIGGER trg_exercise_type_known_update
BEFORE UPDATE OF exercise_type ON exercise
WHEN NEW.exercise_type NOT IN ('GridPath', 'Quiz')
BEGIN
    SELECT RAISE(ABORT, 'exercise_type non e un tipo registrato');
END;
