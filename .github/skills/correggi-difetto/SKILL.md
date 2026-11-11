---
name: correggi-difetto
description: Corregge un difetto o una verifica fallita nel perimetro indicato.
disable-model-invocation: true
argument-hint: "Comportamento atteso, osservato e log disponibili"
---
Analizza il difetto o l'esito fornito, individua la causa e correggila con una regressione significativa. Non indebolire asserzioni o permessi per eliminare l'errore; esegui i gate pertinenti e riporta l'esito reale.

Leggi [le regole comuni](../../copilot-instructions.md) e segui [sviluppo-incrementale](../sviluppo-incrementale/SKILL.md). Usa i dettagli forniti nel messaggio; chiedi solo informazioni indispensabili non ricavabili dai file.

Esegui i controlli pertinenti — restore, build, test, lint, bundle — e riporta l'esito reale, distinguendo ciò che hai eseguito da ciò che resta DA VALIDARE.
