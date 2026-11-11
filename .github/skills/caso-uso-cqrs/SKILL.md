---
name: caso-uso-cqrs
description: "Implementa o modifica una richiesta CQRS di Codì con handler, policy, validazione e test pertinenti."
---
# Caso d'uso CQRS

Leggi [le regole comuni](../../copilot-instructions.md) e [le istruzioni CQRS](../../instructions/cqrs-dispatcher.instructions.md). Non applicare questo workflow a heartbeat, presenza o rinnovo lease.

## Individuare il contratto

Cerca il nome in §10.4–§10.5 e nel codice separatamente:
- Catalogato e non implementato: realizza il contratto esistente.
- Catalogato e implementato: estendi o correggi quel caso, senza creare sinonimi.
- Non catalogato: verifica se è già coperto da una richiesta; se è una nuova funzione autorizzata, aggiorna prima il contratto pertinente. Non inventare una policy per aggirare un'ambiguità.

Identifica policy (§9.3), canali ammessi, input/output, proprietà della risorsa e status HTTP. BootstrapAdminAccount rimane solo locale. Verifica le API di Mediator e le astrazioni realmente disponibili prima di usare un template.

Per HTTP identifica anche l'area secondo [API-ENDPOINTS](../../../docs/API-ENDPOINTS.md):
admin/regia in `/api/v1/admin`, partecipanti in `/api/v1/public`. Usa il dispatcher condiviso;
testa la richiesta nell'area corretta e il rifiuto nell'altra, conservando i controlli di policy.

## Implementare

1. Crea record e handler con nome pubblico e policy secondo il meccanismo effettivo del progetto. Se l'entità ha già i suoi file `[Entità]Commands.cs`/`[Entità]CommandHandlers.cs` (o la coppia delle query), aggiungi lì la richiesta nuova e il suo handler invece di creare un file per richiesta. I [modelli strutturali](assets/comando-handler.cs.template) richiedono adattamento e non sono codice pronto.
2. Distingui validazione di input, autorizzazione, invarianti e orchestration. Estrai identità nel trasporto e passa un contesto affidabile: non accettare identità autorevoli dal corpo.
3. Riusa servizi e repository; propagazione CancellationToken e transazione condivisa, nessuna richiesta invocata da un'altra.
4. Per regia applica §6.4–§6.5 e §12.1: coordinatore prima della transazione, precondizioni su tutti i destinatari, revisione e idempotenza, commit prima della distribuzione.
5. Per CRUD usa la versione di riga e i suoi status; per query nessuna scrittura. Non imporre outbox e revisione di sessione a ogni comando.
6. Integra nel registro/pipeline e nei trasporti ammessi, senza una nuova rotta per il solo nome. Se le fondazioni mancano, completa quelle necessarie nel perimetro o riporta la dipendenza reale.
7. Scrivi test al confine che garantisce il comportamento: dominio/handler, pipeline, persistenza o trasporto. Il [modello test](assets/test-caso-uso.cs.template) indica parti obbligatorie da sostituire; nessun test vuoto.
8. Aggiorna cataloghi solo se il contratto cambia; aggiorna stato e matrice di copertura quando presenti.

Applica la [checklist pertinente](references/checklist.md), marcando non applicabili i controlli di altre categorie. Esegui i gate pertinenti e registra l'esito reale.
