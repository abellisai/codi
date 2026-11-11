---
name: revisiona-modifica
description: Revisiona una porzione per difetti e violazioni di contratto.
disable-model-invocation: true
argument-hint: "File o porzione da esaminare"
---
Revisiona i file o la porzione indicata, usando il diff se disponibile. Riporta solo rilievi concreti con file/riga, scenario, impatto e correzione minima; non modificare file.

Leggi [le regole comuni](../../copilot-instructions.md) e segui [verifica-consegna](../verifica-consegna/SKILL.md). Usa i dettagli forniti nel messaggio; chiedi solo informazioni indispensabili non ricavabili dai file.

Il revisore lavora in sola lettura: valuta gli esiti dei controlli già eseguiti e segnala quali verifiche mancano, senza eseguirle. Chi modifica il codice esegue i gate e allega l'output.
