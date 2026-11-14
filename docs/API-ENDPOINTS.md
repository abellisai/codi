# Organizzazione degli endpoint HTTP

Contratto: [specifica tecnica](../SPECIFICA_TECNICA_PIATTAFORMA_DIDATTICA.md) §9–§11,
§14.2. Aggiornato: 9 ottobre 2026, versione tecnica 0.37.
Il catalogo corrente comprende ingresso, regia affidabile, osservazione, autore Blockly e
salvataggi del lavoro con permesso conclusivo.
Il [rapporto T4](T4-REGIA-AFFIDABILE.md) registra le evidenze locali.

## Strategia

Un solo backend, database, registro CQRS e insieme di handler. Due gruppi ASP.NET Core
con prefissi versionati distinguono i client:

| Area | Client | Prefisso HTTP | Policy del dispatcher |
|---|---|---|---|
| Admin | `projects/admin`, HTTPS 4200 | `/api/v1/admin` | `Admin`, `SessionController`; `LoginAdmin` come ingresso anonimo |
| Public | `projects/public`, HTTPS 4300 | `/api/v1/public` | `AdmittedParticipant`, `ActiveParticipantConnection`, `FinalizingParticipant`; `JoinSession` come ingresso anonimo |

I due hub di §11.1 hanno schemi propri scelti dal percorso: `/hubs/v1/participant` usa la
credenziale dell'alunno, `/hubs/v1/teacher` quella del docente. Una credenziale di un'area non
autentica l'hub dell'altra.

Public comprende l'app alunni e le richieste autenticate dei partecipanti: non significa
accesso anonimo a tutte le risorse. Un URL admin non concede il ruolo admin. Il dispatcher
limita il catalogo all'area prima di leggere il payload; l'esecutore verifica ancora canale,
identità e policy prima dell'handler. La proprietà della risorsa resta un controllo applicativo.

Il nome fuori area, sconosciuto o solo locale risponde `404 CommandNotFound` oppure
`404 QueryNotFound`, senza elencare il catalogo. Nell'area corretta, identità assente:
401; policy mancante: 403. Antiforgery sulle richieste autenticate con cookie resta attivo.
Admin usa `__Host-CodiAntiforgery`. Public usa `__Host-CodiPublicAntiforgery`.
I due client inviano il proprio token corrente in `X-Codi-Antiforgery`, anche per negoziazione e polling SignalR.
`BootstrapAdminAccount` è escluso da entrambe le aree HTTP.

Il trasporto comune HTTP e i contratti restano esportati da `@codi/api`.
Il client SignalR usa invece `@codi/api/signalr-antiforgery`, per evitare il caricamento anticipato della libreria.
Questa separazione riguarda i moduli Web e non modifica endpoint, credenziali o protocollo.

## Rotte disponibili nel sorgente

Dal 12 novembre 2026 `ListSessionParticipants` include `rowVersion` come stringa decimale.
La rinomina e l'esclusione inviano tale valore in `baseRowVersion`.
I comandi restano `UpdateParticipant` ed `ExcludeParticipant`, nel gruppo admin esistente.

Le query admin `GetExercise` e `ListExercises` includono `publicationStatus`.
I valori sono `Draft`, `Available` e `UnpublishedChanges`.
Il server confronta la bozza con l'ultima versione, inclusa la soluzione privata, senza esporre tale soluzione nelle risposte.
La pubblicazione usa i comandi esistenti; non aggiunge rotte.

| Metodo | Percorso | Comportamento |
|---|---|---|
| GET | `/api/v1/admin/bootstrap` | Coppia antiforgery prima dell'accesso docente |
| POST | `/api/v1/admin/auth/login` | Cookie admin HttpOnly e token antiforgery autenticato |
| GET | `/api/v1/admin/auth/me` | Profilo della credenziale amministrativa verificata |
| POST | `/api/v1/admin/auth/logout` | Revoca credenziale, cancella cookie, 204 |
| POST | `/api/v1/admin/execute/command/{commandName}` | 37 nomi di comando ammessi; bootstrap amministrativo solo locale, con policy obbligatoria |
| POST | `/api/v1/admin/execute/query/{queryName}` | 23 query presenti, con la policy dichiarata dalla query |
| GET | `/api/v1/public/bootstrap` | Coppia antiforgery e ticket di ammissione, consumato dall'ingresso |
| GET | `/api/v1/public/schools`, `/schools/{id}/classrooms`, `/classrooms/{id}/sessions` | Scoperta pubblica pre-join: scuole, classi e la sessione in play di una classe; è il ripiego dell'ingresso |
| GET | `/api/v1/public/in-play` | C'è una lezione in play? Un solo booleano: la home dell'alunno se ne accorge e chiede il codice di lezione |
| POST | `/api/v1/public/join` | Ingresso idempotente con il **codice di lezione** (`joinCode`, `displayName`) oppure con la **catena** (`schoolId`, `classroomId`, `sessionId`, `displayName`): emette il cookie del partecipante e svuota il ticket |
| GET | `/api/v1/public/auth/me` | Contesto del partecipante: identità, ammissione, vista, stato della sessione e nomi del percorso (scuola, classe, lezione) |
| POST | `/api/v1/public/participant/leave` | Uscita volontaria: revoca la credenziale e cancella i cookie, 204 |
| PUT | `/api/v1/public/participant/attempts/{id}/workspace` | Salvataggio del progetto del tentativo, con revisione, impronta e idempotenza |
| PUT | `/api/v1/public/participant/attempts/{id}/checkpoint` | Checkpoint del motore legato a progetto, generazione e tentativo |
| PUT | `/api/v1/public/participant/attempts/{id}/preview` | Anteprima dei blocchi per il docente, entro 256 KiB; non salva nulla e aggiorna una cache in memoria |
| POST | `/api/v1/public/participant/attempts/{id}/finalization` | Unico salvataggio conclusivo ammesso dal permesso del grant |
| GET | `/api/v1/public/participant/finalizations` | Permessi conclusivi della credenziale corrente |
| POST | `/api/v1/public/execute/command/{commandName}` | Nove comandi pubblici, con la propria policy obbligatoria |
| POST | `/api/v1/public/execute/query/{queryName}` | Cinque query del partecipante autenticato |

**Dove vivono i contratti (0.41, DTO raccolti dalla 0.42).** Tre collocazioni, una per tipo di contratto:

- **DTO del dispatcher** — record `*Dto` in `Qwerty.Codi.Application/Dto`, **un file per tipo** con
  il nome del tipo (`SessionDto.cs`, `ParticipantContextDto.cs`), namespace
  `Qwerty.Codi.Application.Dto`: sono il corpo di `POST /execute/command|query/{nome}`. Comandi e
  query restano invece nella cartella del caso d'uso che li produce.
- **Messaggi realtime** — il tipo del messaggio sta in `Application/Dto`; costanti, nomi degli eventi e
  argomenti del protocollo restano con la loro famiglia (`Sessions/ParticipantRealtimeContract.cs`,
  `Sessions/TeacherRealtimeEvents.cs`, `Sessions/ParticipantPreviewCommands.cs`): il nome dell'evento e
  la forma del messaggio sono lo stesso contratto.
- **Corpi degli endpoint dedicati** — record `*Response` in
  [`src/Qwerty.Codi.Api/Contracts/TransportResponses.cs`](../src/Qwerty.Codi.Api/Contracts/TransportResponses.cs):
  `AntiforgeryBootstrapResponse`, `ParticipantJoinResponse`, `AdminSignInResponse`,
  `AdminProfileResponse`. Sono proiezioni dei DTO applicativi, private dei campi che non devono
  uscire (marca di sicurezza, credenziale, ticket di ammissione).

Nessun payload che il client legge è costruito come oggetto anonimo: se ha una forma, ha un tipo con
un nome, altrimenti nessun test può difenderlo da un cambio involontario.
| GET | `/health/live`, `/health/ready` | Processo e readiness del database; nessun healthcheck media |
| SignalR | `/hubs/v1/participant` | Hub del partecipante: credenziale dell'alunno, gruppo `participant:{id}` |
| SignalR | `/hubs/v1/teacher` | Hub del docente: credenziale docente, `ObserveSession` → gruppo `session:{id}:teachers` |

Il login tramite dispatcher non emette il cookie: la UI usa l'endpoint dedicato.
`BootstrapAdminAccount` è solo locale. `JoinSession` è implementata e ha due porte: il percorso
dedicato `/api/v1/public/join`, che è quello che l'app usa perché il trasporto emette il cookie, e
il dispatcher generico dell'area public, che esegue lo stesso comando con lo stesso contesto. Il
corpo non dichiara né lo scope di ammissione né la chiave di idempotenza: li legge il trasporto,
dal ticket e dall'intestazione `Idempotency-Key`.

Il corpo dell'ingresso ha **due forme alternative** (§9.4): `{ "joinCode": "24681357",
"displayName": "Luca" }`, che è la via ordinaria in aula, oppure `{ "schoolId": …, "classroomId": …,
"sessionId": …, "displayName": "Luca" }`, che è il ripiego. Dichiararle entrambe o nessuna delle due
è `400 ValidationFailed`. Un codice inesistente è `404 InvalidJoinCode` — lo stesso esito di un codice
scritto male — mentre un codice valido su una lezione non in play riceve il rifiuto dello stato
(`409 SessionStateConflict` o `409 AdmissionsClosed`). Dopo dieci codici errati in un minuto lo stesso
browser o lo stesso indirizzo riceve `429 TooManyJoinAttempts`; un ingresso riuscito azzera il
conteggio. Il codice non compare in nessuna lettura pubblica: lo espongono soltanto `GetSession` e
`ListSessions`, che vivono nell'area admin.
`CreateSession`, `StartSession`, `Pause`, `Resume`, `EndSession`, `DeleteSession`, `OpenAdmissions`,
`CloseAdmissions`, `ShowWaiting`, `AssignExercise` ed `EnableActivity` passano dal dispatcher admin
con policy `SessionController`; `ShowWaiting` è il primo comando che scrive lo stato desiderato
(default di classe e stati dei partecipanti), registra comando, consegne e messaggio outbox nella
stessa transazione e risponde **202 Accepted** con il riepilogo delle consegne e il riferimento a
`GetCommandStatus` (§10.1, §10.6); `AssignExercise` assegna una **versione pubblicata** — dichiarata
per identificativo oppure presa dalla casella della sequenza, che risolve l'ultima versione
pubblicata dell'esercizio — alla classe o a un singolo alunno, e per ogni destinatario crea
l'assegnazione, **un tentativo** con il progetto iniziale della versione e lo stato desiderato
corrispondente (vista esercizio, controlli chiusi); `EnableActivity` accende i controlli
dell'attività già assegnata, senza avviare il programma e senza ricreare il tentativo, e rifiuta
con `409` se **un solo** destinatario non ha un tentativo, è in pausa docente o non è sulla vista
dell'esercizio (§6.5, nessuna applicazione parziale implicita). `RunProgram`, `RestartExecution` e
`ResetWork` chiudono il contenuto della sessione: i primi due scrivono una **direttiva** di esecuzione
— applicabile una volta sola, senza generazione nel comando di classe e con la generazione del
destinatario in quello individuale — e il terzo archivia il tentativo e ne crea uno dal modello
iniziale della versione assegnata, verificando che la conferma si riferisca proprio al lavoro
corrente. Tutti dichiarano la revisione
attesa e la fanno avanzare una volta sola. Le quattro letture del contenuto sono implementate:
`GetAssignedExercise` porta la versione fissata dall'assegnazione con la sua definizione (§13.1: la
soluzione privata resta fuori) e `GetAttempt` il tentativo corrente con progetto, impronta e
checkpoint per il player; `ListSessionParticipants` e `GetParticipantDetail` (`SessionController`,
**senza** revisione attesa perché non mutano nulla) danno al docente la griglia e il singolo alunno,
ciascuna con lo storico contato dei tentativi.
La distribuzione ai client usa stati completi per destinatario, revisione, epoch e identità del server.
I client confermano l'applicazione dopo il caricamento del lavoro.
Il dispatcher conserva due ritentativi brevi e distingue `Sent`, `Applied` e `Unconfirmed`.
`Pause`, `Resume` ed `EndSession` rispondono 202 con ricevuta e campi della sessione.
`DeleteSession` risponde **200** con la sola sessione eliminata: non produce consegne, non usa
l'outbox e non passa dal registro di idempotenza; rifiuta con `409` una sessione in corso, `409` una
revisione superata, `404` una sessione già eliminata o di un altro docente.
La regia di una sessione esistente richiede `Idempotency-Key`.
`GetSessionOverview` e `TeacherSnapshot` aggiornano osservazioni, presenze e ricevute della dashboard.
`ReportRecoveryBlocked` materializza l'incertezza del partecipante corrente, con verifica di epoch, tentativo e revisione.

La scoperta pubblica
precedente al join (`/api/v1/public/schools`, `/schools/{id}/classrooms`, `/classrooms/{id}/sessions`
e — dalla 0.37 — `/api/v1/public/in-play`, che risponde con il solo booleano della lezione in play)
usa endpoint dedicati e non entra nel catalogo. `UpdateParticipant` ed `ExcludeParticipant`
modificano una riga esistente e dichiarano `baseRowVersion`; non incrementano `sessionRevision`.
Upload/download media admin sono stati rimossi: non sono più rotte disponibili.
`POST /api/v1/public/participant/leave` esegue `LeaveSession`: revoca la credenziale senza
cancellare la partecipazione e cancella il cookie del partecipante insieme al token antiforgery.
I salvataggi del lavoro (`PUT .../workspace`, `.../checkpoint`) e la finalizzazione del grant
conclusivo sono **implementati**; l'anteprima pubblicata ha due porte — il metodo hub
`WorkspacePreviewUpdated` entro 24 KiB, che rifiuta con `InvalidPayload` oltre il limite, e
`PUT .../preview` entro 256 KiB. Nessuna delle due salva il progetto: l'anteprima vive in una cache
in memoria e il docente la legge con `GetParticipantPreview`.

## Catalogo eseguibile

Il sorgente dichiara 51 comandi, uno solo locale, e 32 query:

| Area | Comandi | Query |
|---|---|---|
| Scuole | CreateSchool, UpdateSchool, ArchiveSchool | ListSchools, GetSchool |
| Classi | CreateClassroom, UpdateClassroom, ArchiveClassroom | ListClassrooms, GetClassroom |
| Corsi | CreateCourse, UpdateCourse, ArchiveCourse | ListCourses, GetCourse |
| Lezioni | CreateLesson, UpdateLesson, ArchiveLesson | ListLessons, GetLesson |
| Sequenza | ReplaceLessonExercises | ListLessonExercises |
| Sessioni | CreateSession, StartSession, Pause, Resume, EndSession, DeleteSession, OpenAdmissions, CloseAdmissions, ShowWaiting, AssignExercise, EnableActivity, RunProgram, RestartExecution, ResetWork | GetSession, ListSessions, GetCommandStatus, ListSessionParticipants, GetParticipantDetail, GetSessionOverview, ListParticipantAttempts |
| Salvataggi e finalizzazione | SaveAttemptWorkspace, SaveAttemptCheckpoint (policy `ActiveParticipantConnection`), FinalizeAttempt (policy `FinalizingParticipant`) | GetAttemptFinalizations (policy `FinalizingParticipant`) |
| Regia dei partecipanti | UpdateParticipant, ExcludeParticipant | — |
| Ingresso pubblico | JoinSession (policy `AnonymousBootstrap`, ammessa da §10.4), LeaveSession (policy `AdmittedParticipant`) | GetParticipantContext, GetParticipantSnapshot, GetAssignedExercise, GetAttempt (policy `AdmittedParticipant`) |
| Connessione del player | InitializeParticipantConnection (policy `AdmittedParticipant`), AcknowledgeCommand e ReportRecoveryBlocked (policy `ActiveParticipantConnection`) | — |
| Anteprima dei blocchi | PublishWorkspacePreview (policy `ActiveParticipantConnection`; hub entro 24 KiB, endpoint dedicato entro 256 KiB) | GetParticipantPreview (policy `SessionController`; `null` quando l'anteprima manca o è scaduta) |
| Esercizi, solo metadati | CreateExercise, UpdateExercise, ArchiveExercise | ListExercises, GetExercise |
| Esercizi, autoraggio | SaveExerciseDraft, ValidateExerciseDraft, PublishExerciseVersion, CopyExerciseVersionToDraft, DeleteExerciseVersion | GetExerciseDraft, GetReferenceSolution, ListExerciseVersions, GetExerciseVersion |
| Accesso | LoginAdmin, LogoutAdmin; BootstrapAdminAccount solo locale | Profilo tramite auth/me |

`ReplaceLessonActivities` e `ListLessonActivities` non sono alias del nuovo modello.
Contenuti, media, iscrizioni e associazioni corso-classe sono eliminati dal prodotto per
decisione dell'utente. Le specifiche 0.7/0.37 e i cataloghi §10.4–§10.5 sono allineati:
il join crea direttamente la partecipazione alla sessione.

Il **codice di ingresso** non aggiunge nomi al catalogo: nasce dentro `StartSession`, viaggia nei
DTO di sessione dell'area admin (`joinCode` in `SessionDto` e `SessionSummaryDto`) e si consuma in
`JoinSession`. I cataloghi restano quelli di sopra — 51 comandi e 32 query — e la ripartizione per
area non cambia.

Le tre richieste sulle versioni (`PublishExerciseVersion`, `ListExerciseVersions`,
`GetExerciseVersion`) riportano anche le **note dell'autore** della versione, che vivono nella riga
compagna privata `exercise_version_private`: sono metadati del docente e restano nell'area admin,
mentre la soluzione di riferimento continua a uscire solo da `GetReferenceSolution` (§13.1).

`DeleteExerciseVersion` chiude le richieste sulle versioni con policy `Admin` e risponde **200**
con `{ exerciseId, versionNumber }`: elimina la versione e la sua riga compagna privata senza
produrre consegne né usare l'outbox. Rifiuta con `409 LatestExerciseVersion` l'ultima versione —
quella che le prossime assegnazioni userebbero — con `409 ExerciseVersionInUse` una versione già
riferita da un'assegnazione, e con `404 ResourceNotFound` un numero o un esercizio che non esiste.
Se l'esercizio ha una sola versione, quella è l'ultima e non si elimina.

### Payload della sequenza

`ReplaceLessonExercises` riceve la versione della lezione e la sequenza completa:

```json
{
  "lessonId": "a970f347-5052-46ad-b8c5-2390ec5bd986",
  "baseRowVersion": "1",
  "exercises": [
    { "exerciseId": "d62cf6ab-3caa-41ab-b2f1-3bc82e9fc92a" }
  ]
}
```

Risposta: elenco di caselle con id, lessonId, exerciseId, position e rowVersion.
Posizioni definitive 1..N, lista vuota ammessa, esercizi inesistenti/archiviati rifiutati.
La transazione riordina tramite posizioni negative temporanee, riusa le identità delle righe
per esercizio ed elimina quelle non più richieste. Il backend ammette esercizi ripetuti.
**Perimetro A-11:** è previsto un solo editor della lezione. Il salvataggio non incrementa
la versione della lezione e non garantisce l'esclusione fra editor simultanei, fuori perimetro.

## Organizzazione del server e dei client

- `Api/Endpoints/CodiApiEndpoints.cs` compone i gruppi; `Endpoints/Admin` raccoglie accesso
  e dispatcher, `Endpoints/Public` bootstrap e dispatcher. Gli hub di §11.1 sono in `Api/Hubs`:
  `ParticipantHub` e `TeacherHub`, con gli schemi a cookie distinti scelti dal percorso.
- `Endpoints/CodiHttpRequestArea.cs` classifica le policy; `Dispatch/DispatcherEndpoints.cs`
  limita il catalogo prima del payload. `Authentication` risolve cookie, antiforgery e contesto, e
  instrada i percorsi degli hub verso la credenziale dell'area corrispondente.
- `Application/Sessions/OutboxDispatcher.cs` è il dispatcher outbox: legge i messaggi disponibili,
supera i comandi con revisione obsoleta, invia `CommandIssued` fuori dalla transazione e poi segna
consegne e messaggio. `Api/BackgroundJobs/OutboxDispatchService.cs` lo avvia come servizio in
background (`Api/Hubs/HubCommandDeliveryNotifier.cs` è il trasporto verso i gruppi,
`Infrastructure/Realtime/OutboxSignal.cs` il segnale in memoria che lo sveglia); la consegna non è
mai dedotta dalla presenza: soltanto la conferma applicativa prova l’applicazione.
- `libraries/api/api-area.ts` esporta API_AREA e i due prefissi; ciascuna app configura la propria
  area. ApiService offre solo executeCommand/executeQuery; AdminSessionService gestisce l'accesso.
- Il proxy inoltra `/api/**`, `/health/**`, `/hubs/**` a HTTPS 5200: i due hub rispondono alla
  negoziazione e collegamenti aperti da `ParticipantRealtime` e `TeacherRealtime`. Riavviare ng serve se si
  modifica il proxy.

Le vecchie rotte condivise non hanno alias. Il fallback esclude API/health/hub e vecchi prefissi
media dall'HTML della SPA. Le route delle pagine `/admin/*` sono distinte dalle API.

## Lavoro futuro e verifiche

Scoperta, credenziali e uscita del partecipante sono **implementate**: gli endpoint pubblici di
scoperta sono dedicati, `JoinSession`, `GetParticipantContext` e `LeaveSession` sono nel catalogo,
e il ticket di ammissione è emesso dal bootstrap pubblico. La regia degli ingressi e dei
partecipanti è **implementata** (`OpenAdmissions`, `CloseAdmissions`, `UpdateParticipant`,
`ExcludeParticipant`). Il **trasporto degli hub** è **implementato** e il protocollo del partecipante ha ora handshake,
presenza, autorizzazione temporanea, consegna e conferma: `InitializeClient` esegue
`InitializeParticipantConnection` (epoca persistita, snapshot inviato), `SnapshotApplied` abilita il
rinnovo e rimanda lo snapshot se la revisione è avanzata, `Heartbeat` rinnova l'autorizzazione solo a
stato applicato, `CommandAcknowledged` esegue `AcknowledgeCommand` e registra l'esito sulla consegna.
La verifica dell'`Origin` (§11.2) è attiva: gli origin ammessi si configurano in
`Codi:Realtime:AllowedOrigins` (in sviluppo 4200 e 4300) e vale anche l'origine del server. Una
conferma è ammessa anche dopo la fine della lezione, se la connessione è ancora operativa: chiudere la
lezione non riapre permessi di lavoro ma non deve lasciare senza conferma l'ultimo stato applicato
(A-04). Telemetria e fotografia della sessione sono implementate. Lo storico dei tentativi
(`ListParticipantAttempts`) e i salvataggi con permesso conclusivo sono **implementati**; l'anteprima
dei blocchi (`PublishWorkspacePreview`, `GetParticipantPreview`) e la sua vista nel dettaglio del
docente sono **implementate**: il percorso è stato collaudato nel browser il 7 ottobre 2026 per il
metodo hub, mentre il ripiego HTTP oltre 24 KiB resta coperto dai soli test locali e il laboratorio
reale resta di T9. Il ciclo di vita della sessione (istanziazione, avvio in play, pausa/ripresa,
conclusione) è definito nelle specifiche ed è **implementato**
(`CreateSession`, `StartSession`, `Pause`, `Resume`, `EndSession`) e passa dal dispatcher admin con
policy `SessionController`, così come la scoperta pubblica pre-join. Media, contenuti,
iscrizioni e associazioni corso-classe
sono eliminati dal prodotto per decisione utente. `JoinSession` crea direttamente la
partecipazione. Non esporre archivi amministrativi ai partecipanti.

Per una richiesta nuova: dichiarare policy, classificare area, riusare il dispatcher,
provare accesso nell'area corretta e rifiuto nell'altra. Per endpoint dedicati autorizzare
contesto e proprietà prima dell'accesso ai dati. Le policy future provate con invoker controllato
non attestano un'implementazione degli alunni.

HttpEndpointAreaTests, SchoolHttpTests e OrganizationHttpTests contengono prove di isolamento,
autenticazione e CRUD; i test Angular verificano area e accesso. I test media sono stati rimossi.
I gate correnti sono stati rieseguiti il 9 ottobre 2026 e sono registrati nel
[registro di stato](../.github/STATO-SVILUPPO.md): build Release a 0 warning, 1.181 test .NET e 334
test Angular senza fallimenti, copertura sopra baseline e gate 4/4. Vedi anche la
[mappa di ripresa](RIPRESA-SVILUPPO.md).
