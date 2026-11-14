# Codì — regole operative

Piattaforma per un docente e 12 PC di bambini di 8–9 anni: .NET 10, Angular, SignalR, SQLite/RepoDB, Blockly. La specifica descrive il prodotto da costruire; controlla il codice per sapere cosa esiste.

## Prima di lavorare

1. Individua la radice `Codi/` dalla soluzione `Qwerty.Codi.slnx`. I percorsi indicati sono relativi a quella radice, salvo i link Markdown.
2. Leggi [lo stato di sviluppo](STATO-SVILUPPO.md) e la [mappa di ripresa dal sorgente](../docs/RIPRESA-SVILUPPO.md), confrontali con i file e con eventuali modifiche in corso. Non supporre che esista Git; se manca, usa inventario e confronto dei contenuti senza inizializzarlo.
3. Leggi le sezioni pertinenti della [specifica funzionale](../SPECIFICHE_PIATTAFORMA_DIDATTICA.md) e della [specifica tecnica](../SPECIFICA_TECNICA_PIATTAFORMA_DIDATTICA.md), poi le istruzioni di area. I riferimenti § senza qualificazione sono tecnici.
4. Una richiesta di implementazione autorizza le modifiche necessarie nel suo perimetro: porta a termine codice, test scritti e documentazione. Un'analisi o revisione resta in sola lettura.

## Autorità e autonomia

Le istruzioni esplicite dell'utente prevalgono sulle convenzioni locali. Per il prodotto prevale la specifica funzionale, poi quella tecnica; queste personalizzazioni descrivono il metodo di lavoro.

- Riusa nomi, contratti e componenti esistenti. Una funzione catalogata ma assente dal codice va implementata: la presenza nel catalogo non è un duplicato.
- Decidi autonomamente dettagli locali reversibili coerenti con i contratti. Non chiedere conferma per organizzare file, scrivere test o correggere un difetto nel perimetro assegnato.
- Una PROPOSTA è la baseline da seguire finché non viene cambiata; DA VALIDARE richiede evidenza, non un'approvazione preventiva automatica.
- Se due requisiti sono incompatibili, registra sezioni, alternative e impatto nello stato; chiedi solo la decisione necessaria e continua le parti indipendenti. Non nascondere il conflitto inventando un contratto.
- Mantieni il lavoro dell'utente e degli altri agenti. Non annullare modifiche estranee.
- Non considerare la configurazione agentica un'autorizzazione a pubblicare, distribuire o alterare dati operativi.

## Architettura da preservare

- `Domain` contiene invarianti e non dipende dagli altri progetti; `Application` dipende dal dominio; `Infrastructure` implementa le astrazioni applicative; `Api` compone i servizi. `Web` è esterno alla soluzione .NET (§4.2).
- Record e handler per richiesta, con nome pubblico e policy espliciti (§4.4, §9.3). Il registro rifiuta richieste senza policy.
- Il dispatcher espone due modelli di percorso per ciascun gruppo `/api/v1/admin` e `/api/v1/public` (§10.1). Leggi [la guida endpoint](../docs/API-ENDPOINTS.md): l'area limita il catalogo, l'esecutore verifica comunque la policy. Gli endpoint dedicati di §10.3 e i metodi hub di §11 traducono il trasporto; non aggiungere una rotta per ogni caso d'uso né alias delle vecchie rotte.
- `AnonymousBootstrap` è un elenco chiuso: `JoinSession`, `LoginAdmin`, `BootstrapAdminAccount`. Quest'ultimo è solo locale e non deve essere esposto via HTTP (§10.4). Policy e canale ammesso sono controlli distinti.
- Endpoint e hub estraggono identità e contesto del trasporto. Gli handler ricevono un contesto esplicito affidabile e non dipendono da `HttpContext`, hub o connection ID non verificati.
- Validazione della richiesta nei behavior, invarianti nel dominio, casi d'uso negli handler/servizi. Un handler non invoca altre richieste.
- Comandi di regia: coordinatore di sessione prima della transazione; stato, comando, consegne, idempotenza e outbox persistiti prima della notifica (§12.1).
- Presenza, heartbeat e lease restano in memoria. L'anteprima segue l'eccezione esplicita `PublishWorkspacePreview` (§15.5); non trasformarla in salvataggio del progetto.
- Frontend: servizi tipizzati per HTTP, due metodi di `ApiService` per il dispatcher e servizi di trasporto per gli endpoint dedicati. I componenti non usano `HttpClient` direttamente (§14.2, §20.7).
- Web è un workspace con `projects/admin` (`web.admin`, Spartan, HTTPS 4200) e `projects/public` (`web.frontend`, interfaccia propria per bambini e Three.js decorativo, HTTPS 4300). Entrambe le cartelle avviano la propria app con `npm run start`; dipendenze e lockfile restano nella root Web. I launcher Windows nella root Codì sono documentati nel [README](../README.md#avvio-rapido-windows); API HTTPS 5200 e F5 senza browser. Usa Node già attivo tramite nvm.
- Nessun ACK presunto, credenziale in localStorage, replay automatico di vecchie direttive o esecuzione libera del codice dei bambini.

## Istruzioni da caricare per area

| Lavoro | Istruzioni |
|---|---|
| C# / .NET | [csharp-dotnet](instructions/csharp-dotnet.instructions.md) |
| Record, handler, dispatcher, policy | [cqrs-dispatcher](instructions/cqrs-dispatcher.instructions.md) |
| SQLite, RepoDB, migrazioni | [persistenza-sqlite](instructions/persistenza-sqlite.instructions.md) |
| Angular, servizi e viste | [frontend-angular](instructions/frontend-angular.instructions.md) |
| Form Angular admin, editor inline e controlli | [admin-signal-forms](instructions/admin-signal-forms.instructions.md) |
| Hub, sincronizzazione, outbox, recovery e salvataggi | [protocollo-realtime](instructions/protocollo-realtime.instructions.md) |
| Blockly, worker, checkpoint | [esercizi-blockly](instructions/esercizi-blockly.instructions.md) |
| Test .NET | [test-dotnet](instructions/test-dotnet.instructions.md) |
| Specifiche e guide | [documentazione-spec](instructions/documentazione-spec.instructions.md) |
| Agenti, prompt e skill | [sviluppo-agentico](instructions/sviluppo-agentico.instructions.md) |

## Verifica e consegna

Esegui tu i controlli del perimetro: `dotnet restore`, build, test, lint, formattazione, bundle e raccolte di copertura, con gli stessi comandi di §20.5 e dalla radice del progetto. Leggi l'output dal terminale e riporta l'esito reale — codice di uscita, conteggi, warning — invece di dedurlo dal codice o dal controllo statico. Restano fuori dalla tua autorizzazione: installazioni di software di sistema, scrittura su dati operativi, pubblicazione e distribuzione.

Le verifiche sono locali. Per scelta dell'utente non si usa GitHub Actions: non creare
workflow o configurazioni di CI senza una sua nuova richiesta esplicita. Conserva script
di test, copertura, lint, build e audit per l'esecuzione locale.

Un esito positivo si dichiara solo con l'output che lo mostra; se un comando non è stato eseguito, scrivilo e consegna come **IMPLEMENTATO — DA VALIDARE**, con comandi, prerequisiti ed evidenze mancanti. Per dichiarare **VALIDATO** serve l'esito effettivo dei controlli pertinenti, eseguiti dall'agente o dall'utente. Una build riuscita non dimostra test o funzionalità, e il controllo statico non sostituisce compilatore e test: il language server può dichiarare "nessun errore" su file che la build rifiuta.

Scrivi test del comportamento significativo al livello adatto (§19, §20.3), senza test vuoti o asserzioni tautologiche. Non aggiungere test applicativi per sole modifiche documentali. Usa [verifica-consegna](skills/verifica-consegna/SKILL.md) per una consegna proporzionata; aggiorna lo stato soltanto quando il lavoro autorizza modifiche.

## Documentazione e lingua

Codice e identificatori in inglese; documentazione, commenti e testi UI in italiano. Rispetta `.editorconfig`. Aggiorna entrambe le specifiche quando cambia un requisito funzionale; una modifica tecnica o di processo aggiorna solo i documenti pertinenti. Implementare un requisito già descritto non richiede di riscriverlo né di incrementare la versione della specifica: aggiorna lo stato e le evidenze.

La [guida d'uso](README.md) descrive coordinatore, specialisti, prompt, skill e differenze tra ambienti. Non fissare un modello nelle personalizzazioni se l'utente non lo richiede.
