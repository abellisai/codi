---
name: migrazione-sqlite
description: "Modifica schema, migrazioni e repository SQLite di Codì con DDL coerente e test di persistenza."
---
# Modifica di schema SQLite

Leggi [le regole comuni](../../copilot-instructions.md), [persistenza](../../instructions/persistenza-sqlite.instructions.md) e §7–§8.

1. Individua dato, vincolo e motivazione nel dizionario e nei file; non duplicare colonne o dati derivabili. Controlla i punti aperti che toccano la modifica.
2. Se il contratto autorizzato è nuovo, aggiorna la specifica prima di implementarlo; per conflitti irrisolti esplicita la scelta necessaria.
3. Aggiungi la migrazione numerata atomica, senza riscrivere versioni rilasciate, e allinea il DDL di installazione pulita.
4. Aggiorna mapping, repository e astrazioni realmente necessarie. In delega concorda la proprietà dei file condivisi.
5. Scrivi test pertinenti di installazione, aggiornamento, riapplicazione, vincoli, rollback e concorrenza; database temporaneo dedicato.
6. Rivedi transazioni, cleanup ed errori. Esegui migrazioni e test su database temporanei; non applicare migrazioni sui dati operativi.
7. Aggiorna stato/contratti e consegna tramite [verifica-consegna](../verifica-consegna/SKILL.md), indicando i comandi eseguiti e ciò che resta DA VALIDARE.
