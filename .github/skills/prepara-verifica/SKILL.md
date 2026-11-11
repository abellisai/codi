---
name: prepara-verifica
description: Prepara la consegna raccogliendo gli esiti reali dei controlli.
disable-model-invocation: true
argument-hint: "Modifica o porzione da verificare"
---
Esegui la verifica del lavoro indicato in sola lettura. Controlla prerequisiti e disponibilità degli script, senza modificare lo stato e senza eseguire i gate: il revisore raccoglie gli esiti prodotti e indica quali verifiche restano.

Leggi [le regole comuni](../../copilot-instructions.md) e segui [verifica-consegna](../verifica-consegna/SKILL.md). Usa i dettagli forniti nel messaggio; chiedi solo informazioni indispensabili non ricavabili dai file.

Esegui i controlli pertinenti — restore, build, test, lint, bundle — e riporta l'esito reale, distinguendo ciò che hai eseguito da ciò che resta DA VALIDARE.
