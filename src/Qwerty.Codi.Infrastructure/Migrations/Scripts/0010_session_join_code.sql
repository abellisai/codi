-- Ingresso con codice numerico (§6.1, §9.4): il docente detta otto cifre e l'alunno entra
-- digitandole, senza scegliere scuola, classe e sessione.
--
-- Il codice è generato all'avvio della sessione e resta lo stesso finché la sessione è in play:
-- la pausa sospende l'attività e non l'ingresso, quindi non invalida il codice già detto a voce.
-- Non si riusa in un'altra sessione, perché l'indice unico lo impedisce; dopo lo Stop resta nella
-- riga come memoria della lezione, ma non è più spendibile perché la sessione non è più in play.
--
-- Le sessioni avviate prima di questa migrazione restano con join_code NULL: si raggiungono con la
-- catena scuola → classe → sessione, che resta ammessa.
-- La prima cifra non è zero: uno zero iniziale è la cifra che si perde più facilmente quando il
-- codice si detta o si digita, e il dominio lo rifiuta. Il vincolo lo rifiuta allo stesso modo,
-- perché una riga fuori formato renderebbe illeggibile la sessione, non solo il suo codice.
ALTER TABLE lesson_session ADD COLUMN join_code TEXT NULL
    CHECK (
        join_code IS NULL
        OR (
            length(join_code) = 8
            AND join_code GLOB '[1-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]'
        )
    );

-- Unicità: NULL non partecipa al vincolo, quindi le sessioni senza codice non collidono fra loro.
CREATE UNIQUE INDEX ux_lesson_session_join_code ON lesson_session(join_code);
