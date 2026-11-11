---
name: nuova-migrazione
description: Modifica schema SQLite con migrazione, DDL e test.
disable-model-invocation: true
argument-hint: "Tabella o vincolo da modificare e risultato atteso"
---
Realizza la modifica di schema richiesta, preservando migrazioni rilasciate e dati storici. Non applicare le migrazioni al database operativo.

Leggi [le regole comuni](../../copilot-instructions.md) e segui [migrazione-sqlite](../migrazione-sqlite/SKILL.md). Usa i dettagli forniti nel messaggio; chiedi solo informazioni indispensabili non ricavabili dai file.

Esegui i controlli pertinenti — restore, build, test, lint, bundle — e riporta l'esito reale, distinguendo ciò che hai eseguito da ciò che resta DA VALIDARE.
