---
description: "Richieste, handler, policy e canali del dispatcher CQRS Codì."
applyTo: "**/src/Qwerty.Codi.Application/**,**/src/Qwerty.Codi.Api/Dispatch/**,**/src/Qwerty.Codi.Api/Endpoints/**,**/src/Qwerty.Codi.Api/Hubs/**"
---
# CQRS e dispatcher

Fonti: §4.4, §9.3, §10 e §12.1. Usa [caso-uso-cqrs](../skills/caso-uso-cqrs/SKILL.md) per l'implementazione.

- Cerca separatamente nei cataloghi e nel codice: un nome già catalogato può essere ancora da implementare.
- Record con nome pubblico esplicito, policy e payload tipizzato; un handler per richiesta. Non derivare il nome pubblico implicitamente dal tipo.
- Organizzazione dei file: per ogni entità `[Entità]Commands.cs`, `[Entità]CommandHandlers.cs`, `[Entità]Queries.cs`, `[Entità]QueryHandlers.cs` nella cartella dell'area, con più richieste della stessa entità nello stesso file e i record tenuti separati dai rispettivi handler. `Organization` ne ospita tre (`School`, `Course`, `Classroom`), `Sessions` una sola, `Lessons` una sola che comprende gli esercizi della lezione. Non creare un file per richiesta: una richiesta nuova di un'entità già presente si aggiunge ai suoi file. DTO, servizi condivisi e astrazioni restano in file propri, e ogni file ha un solo blocco `using` senza duplicati.
- Il registro verifica metadati e unicità all'avvio. Policy prima dell'handler, con controllo di proprietà e precondizioni nel contesto della risorsa.
- Dispatcher: POST `/api/v1/{area}/execute/command/{commandName}` e `/api/v1/{area}/execute/query/{queryName}`, con `area` = `admin` oppure `public`. I gruppi sono composti in `Api/Endpoints/Admin` e `Public`; adattatore, registro e handler sono unici. Non aggiungere una rotta per ogni richiesta.
- Leggi [API-ENDPOINTS](../../docs/API-ENDPOINTS.md). Admin/SessionController appartengono ad admin; le tre policy partecipante a public. LoginAdmin è anonimo nell'area admin, JoinSession nell'area public, BootstrapAdminAccount solo locale. Policy nuove escluse finché classificate. Fuori area: 404; nell'area corretta: controlli di identità, policy e risorsa invariati.
- Sono previsti endpoint dedicati (§10.3), hub (§11) e bootstrap locale: condividono i casi d'uso quando il catalogo lo prevede, conservando i vincoli del trasporto.
- AnonymousBootstrap: JoinSession, LoginAdmin, BootstrapAdminAccount. Quest'ultimo non è mai esposto via HTTP; non rendere invocabili su qualunque trasporto tutte le richieste del registro.
- Input/validazione nel behavior, invarianti nel dominio, coordinamento nell'handler/servizi. Gli handler non usano HttpContext o hub e non chiamano altre richieste.
- Regia: expectedSessionRevision + idempotenza; coordinatore prima della transazione, tutti i destinatari validati prima delle scritture; stato e outbox atomici. Ritorno 202 significa registrazione, non applicazione.
- CRUD: controllo della versione di riga, 412 su conflitto e 428 se la versione obbligatoria manca. Query: DTO e nessuna mutazione persistente.
- Contatori a 64 bit nel JSON come stringhe decimali; usare il serializer/DTO coerente col contratto.
- Errori con codice/status stabili (§10.7), cancellazione propagata; nessun retry cieco di mutazioni.
- Telemetria, presenza e lease non diventano comandi. PublishWorkspacePreview è l'eccezione esplicita di §15.5 per la cache dell'anteprima.
- Test del registro, dei canali consentiti, del rifiuto prima dell'handler e dei confini di transazione; chiamare soltanto Handle non prova la policy.

Non inventare firme Mediator o attributi pronti all'uso: verifica la versione fissata e le astrazioni del progetto.
