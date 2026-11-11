---
name: sviluppo-incrementale
description: "Implementa o riprende una porzione degli incrementi T0–T9 di Codì, mantenendo dipendenze, stato e consegna."
---
# Sviluppo incrementale Codì

Usa questa procedura per richieste di sviluppo o ripresa. Per sola analisi prepara il risultato richiesto senza modificare file. Leggi [le regole comuni](../../copilot-instructions.md), [lo stato](../../STATO-SVILUPPO.md) e §21.1 della [specifica tecnica](../../../SPECIFICA_TECNICA_PIATTAFORMA_DIDATTICA.md).

## Scegliere la porzione

1. Confronta stato e sorgenti; conserva modifiche in corso. “Continua” riprende la porzione aperta, non avvia un altro scaffold.
2. Se l'utente indica una fase, usa quella. Altrimenti scegli il primo lavoro incompleto con prerequisiti disponibili e dichiara la scelta.
3. Definisci un risultato osservabile, file coinvolti, contratti e criteri di accettazione. Una porzione deve essere integrabile, non un insieme di classi vuote.
4. Verifica le decisioni D-xx e i rilievi A-xx solo quando influenzano il lavoro. Un dettaglio tecnico reversibile coerente con la baseline si risolve autonomamente; una contraddizione funzionale si espone con alternative.
5. Non richiedere nuove conferme per lavoro già autorizzato. Non estendere una piccola richiesta a tutte le fasi.

## Realizzare e integrare

- Scrivi implementazione, test significativi, registrazioni e configurazione necessari. Un prerequisito assente ma incluso nel perimetro va realizzato, non simulato.
- Usa i profili di [agenti](../../README.md) pertinenti. Quando l'utente ha scelto il coordinatore e l'ambiente offre delega, questa procedura prevede specialisti solo per compiti indipendenti o revisioni utili. Negli altri casi procedi sequenzialmente.
- Prima di ogni delega specifica file scrivibili, contratto condiviso, criteri e comandi di verifica. Mai due scrittori sullo stesso file; stato e integrazione sono del coordinatore.
- Per CQRS usa [caso-uso-cqrs](../caso-uso-cqrs/SKILL.md); per schema [migrazione-sqlite](../migrazione-sqlite/SKILL.md); per UI [pagina-angular](../pagina-angular/SKILL.md).
- Rivedi il percorso completo, correggi difetti introdotti ed esegui i gate pertinenti riportandone l'esito reale.
- Aggiorna i contratti soltanto se cambiano; usa [allinea-documentazione](../allinea-documentazione/SKILL.md).

## Chiudere o passare il lavoro

Aggiorna STATO-SVILUPPO.md con porzione, file, decisioni, evidenze, controlli pendenti e prossimo passo. Per ogni controllo registra chi lo ha eseguito, comando/scenario, data e risultato; non attribuire a una build precedente la validazione di file nuovi.

Consegna con [verifica-consegna](../verifica-consegna/SKILL.md). Se mancano esiti dei gate, lo stato è IMPLEMENTATO — DA VALIDARE, non VALIDATO. Se la porzione è parziale, lasciala IN CORSO indicando precisamente cosa manca.
