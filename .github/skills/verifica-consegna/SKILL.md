---
name: verifica-consegna
description: "Prepara una consegna Codì eseguendo i controlli pertinenti e riportandone l'esito reale."
---
# Verifica e consegna

Leggi [le regole comuni](../../copilot-instructions.md). Usa questa skill per una modifica o per preparare una verifica; non cambia il perimetro autorizzato.

1. Ricava file e comportamento modificati dal diff, dall'inventario o dal contesto. Non presumere che Git sia presente.
2. Rileggi il percorso interessato e i contratti pertinenti; distingui revisione statica, test scritti ed esecuzioni con risultati.
3. Controlla l'esistenza di progetto, script, lockfile, collector e configurazione prima di eseguire un comando. Usa [comandi e prerequisiti](references/comandi.md).
4. Per sole modifiche documentali/agentiche prepara controlli di frontmatter, link, riferimenti, instradamento e coerenza; niente build applicativa superflua.
5. Per codice esegui i gate pertinenti e riporta gli esiti reali, distinguendo ciò che hai eseguito da ciò che resta DA VALIDARE.
6. Non attribuire a una build il superamento dei test, né a test senza casi una copertura del comportamento. Non inventare percentuali.
7. Se stai consegnando una modifica autorizzata, aggiorna il passaggio di consegne (oppure restituiscilo al coordinatore in delega). In sola revisione non modificare lo stato.

Rispondi in modo proporzionato: risultato, file principali, evidenze effettive, verifiche DA VALIDARE e prossimo passo necessario. Non ripetere l'intera checklist o tutta la roadmap per una modifica piccola.
