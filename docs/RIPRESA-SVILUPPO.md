# Riprendere lo sviluppo di Codì

Nomi tecnici allineati alla rinomina del 16 ottobre 2026: `Codi` e `Qwerty.Codi`.
Le evidenze successive alla mappa seguente sono nel [registro corrente](../.github/STATO-SVILUPPO.md).

Aggiornato: 9 ottobre 2026, dopo l'ingresso con il codice di lezione (funzionale 0.7, tecnica 0.37).
Le evidenze correnti sono nel [registro](../.github/STATO-SVILUPPO.md); il [rapporto T4](T4-REGIA-AFFIDABILE.md)
conserva la procedura del collaudo precedente.
Le [specifiche funzionali](../SPECIFICHE_PIATTAFORMA_DIDATTICA.md) definiscono il prodotto.
La [specifica tecnica](../SPECIFICA_TECNICA_PIATTAFORMA_DIDATTICA.md) comprende anche contratti futuri.

## Stato corrente degli incrementi

| Incremento | Stato | Perimetro |
|---|---|---|
| T0–T2 | Validati localmente | Dipendenze, fondazioni, scuole, classi, corsi, lezioni e sequenza di esercizi |
| T3 | Chiuso | Sessioni, scoperta pubblica, ingresso — **codice di lezione** dalla porzione del 9 ottobre 2026, con la catena come ripiego —, credenziali, uscita e griglia dei partecipanti |
| T4 | Completato | Regia idempotente, consegne per destinatario, conferme, autorizzazione temporanea e osservazione docente |
| T5 | Completato | Salvataggi, recupero locale, finalizzazione e ripristino dopo riavvio server |
| T6 | Chiuso | Autore Blockly, anteprima isolata e versioni pubblicate immutabili |
| T7 | Chiuso (collaudato nel browser) | Player Blockly nel worker, checkpoint, valutazione locale, ACK di pausa con soglia |
| T8 | Chiuso (percorso dell'anteprima collaudato nel browser il 7 ottobre 2026) | Anteprima dei blocchi con freschezza, avviso ai docenti e vista nel dettaglio dell'alunno |
| T9 | Da iniziare | Laboratorio su 12 PC, installazione, backup e ripristino |

L'ultima porzione, del 7 ottobre 2026, ha spostato la vista d'insieme della classe dalla regia a una
pagina dedicata (`/admin/sessions/:id/participants`): una card per alunno con le azioni del singolo,
mentre la regia conserva contesto, stato, comandi di classe e il collegamento. Lo stato della sessione
è posseduto da `SessionRegiaService`, condiviso dalle due pagine con una sola sottoscrizione all'hub.
La porzione è validata localmente e collaudata nel browser (specifica tecnica 0.35).

T4 comprende una dimostrazione finita di 40 avanzamenti.
La dimostrazione verifica avvio, pausa, ripresa e riavvio.
Il player Blockly del partecipante appartiene a T7.
I test simulano 12 connessioni. Il laboratorio reale resta di T9.
La deroga seed A-09 resta aperta prima di T9.

## Percorsi effettivamente presenti

| Percorso | Comportamento |
|---|---|
| `/admin/login` | Accesso docente tramite cookie protetto |
| `/admin/schools` e `/admin/schools/:schoolId` | Scuole, classi e anno scolastico |
| `/admin/courses` e `/admin/courses/:courseId` | Corsi e lezioni |
| `/admin/courses/:courseId/lessons/:lessonId` | Sequenza ordinata degli esercizi |
| `/admin/exercises` | Libreria degli esercizi |
| `/admin/exercises/:exerciseId/editor` | Bozza Blockly, anteprima, soluzione privata e pubblicazione |
| `/admin/sessions` | Elenco delle sessioni e istanziazione della lezione nella classe |
| `/admin/sessions/:sessionId` | Regia: contesto, stato, comandi di classe e collegamento alla vista dei partecipanti |
| `/admin/sessions/:sessionId/participants` | Lista di card con presenza, stato desiderato, lavoro e azioni del singolo alunno; dettaglio e blocchi nella card selezionata |
| `/admin/local-recovery` | Copie locali non confermate sul PC condiviso, esportabili dal docente |
| `/join` | Codice di lezione e nome con il feedback dell'ingresso; il sito dichiara se una lezione è in play. La scelta di scuola e classe resta come ripiego nascosto |
| `/lezione` | Stato a riquadri con icona colorata, player Blockly nel worker, **prova locale** del programma con animazione della scena, checkpoint e progetto dell'alunno |
| `/admin/workspace-prototype` | Prototipo Blockly locale separato dalla persistenza |

Gli elenchi mantengono paginazione in memoria.
Lo stato della sessione è posseduto da `SessionRegiaService`, condiviso dalla regia e dalla pagina dei
partecipanti con una sola sottoscrizione all'hub.
Le form admin usano Signal Forms e Spartan Field.
La pagina dell'alunno mostra lo stato con riquadri a icona colorata (collegamento, permesso di
lavorare, stato del programma) e consente la prova locale del proprio algoritmo (funzionale 0.8,
tecnica 0.38): la prova vive nella pagina, non nel worker, e non produce checkpoint né telemetria.
La revisione del 14 novembre 2026 introduce ingresso numerato, pulsanti grandi e il riquadro «Adesso».
Three.js anima il benvenuto e i risultati corretti. Il collaudo con bambini e sui dodici PC resta da eseguire.
Associazioni corso-classe, iscrizioni e contenuti/immagini restano eliminati dal prodotto.

## Backend e dati presenti

La soluzione mantiene Domain, Application, Infrastructure e Api.
Il registro dichiara 51 comandi e 32 query.
L'ingresso in aula usa il **codice di lezione** (funzionale 0.7, tecnica 0.37): `StartSession` genera
un codice di otto cifre e la regia lo mostra al docente; `GET /api/v1/public/in-play` dice alla home
dell'alunno se una lezione è in corso e `JoinSession` accetta il codice oppure la catena; lo schema è
alla versione 10 con `join_code` su `lesson_session`.
Il bootstrap amministrativo è solo locale.
L'[inventario HTTP](API-ENDPOINTS.md) distingue le aree admin/public e le policy obbligatorie.

I contratti sono in tre collocazioni dichiarate (tecnica 0.41, DTO raccolti dalla 0.42): i **DTO del
dispatcher** in `Application/Dto`, un file per tipo nel namespace `Qwerty.Codi.Application.Dto`
(53 tipi, nessun duplicato e nessun oggetto anonimo); i **messaggi realtime** con lo stesso tipo in
`Dto` e costanti, eventi e argomenti con la loro famiglia (`Sessions/ParticipantRealtimeContract.cs`,
`Sessions/TeacherRealtimeEvents.cs`); i **corpi degli endpoint dedicati** in `Api/Contracts` come record
`*Response`. Un payload che il client legge è sempre un tipo con un nome.

I comandi di regia acquisiscono il coordinatore prima della transazione.
La transazione conserva stato desiderato, comando, consegne, idempotenza e outbox.
La notifica segue il commit. La conferma applicativa segue il caricamento del lavoro.
Una consegna inviata senza conferma resta distinguibile da una consegna applicata.

Ogni destinatario riceve il proprio tentativo, la propria generazione e il proprio stato desiderato.
La pausa individuale conserva le autorizzazioni degli altri partecipanti.
Un ingresso tardivo riceve l'attività corrente e un tentativo indipendente.
L'ingresso tardivo non ripete una direttiva storica.

Il salvataggio del progetto è idempotente per `saveId`, verifica impronta e revisione e rifiuta una
revisione superata; il checkpoint è legato a progetto, generazione, motore e tentativo. La copia
locale in IndexedDB non sostituisce il server: viene riproposta e riconciliata al collegamento.
Dopo `EndSession` il server emette permessi conclusivi a finestra limitata e revoca le credenziali
allo scadere; al riavvio del server sospende le sessioni non terminate e supersede le direttive non
confermate.

Presenza, heartbeat, autorizzazione temporanea e osservazioni restano in memoria.
`ActivityStateUpdated` verifica identità, epoch, revisione, tentativo, generazione e sequenza.
`GetSessionOverview` e `TeacherSnapshot` alimentano la regia docente.
Una revisione a 64 bit resta una stringa decimale nei contratti JSON.

L'anteprima dei blocchi (§15.5) non è un salvataggio: `PublishWorkspacePreview` verifica epoca,
istantanea applicata, presenza, partecipazione ammessa, stato desiderato corrente e tentativo, valida
il progetto con le regole del salvataggio e aggiorna una cache in memoria (una voce per partecipante,
capienza 128, freschezza 30 secondi). `GetParticipantPreview` restituisce contenuto, revisione,
impronta, istante di ricezione e `partial` confrontando con il progetto persistito; risponde `null`
quando non c'è o è scaduta. Il metodo hub `WorkspacePreviewUpdated` accetta fino a 24 KiB e l'endpoint
`PUT /api/v1/public/participant/attempts/{id}/preview` fino a 256 KiB. `WorkspacePreviewNotifier`
avvisa il gruppo dei docenti della sessione con identità, sessione, revisione, impronta e istante,
**senza il progetto**: il contenuto si legge a parte.

Il riduttore client ignora revisioni precedenti e riconosce i duplicati.
La pagina sospende la dimostrazione quando perde l'autorizzazione o diventa nascosta.
`ReportRecoveryBlocked` materializza l'incertezza dopo un refresh.
La ripresa esplicita senza checkpoint riporta la dimostrazione allo stato `Ready`.
Il recupero dei checkpoint persistiti è di T5; il player che li produce resta di T7.

Fonti correnti:

- [Operazione di regia](../src/Qwerty.Codi.Application/Sessions/SessionCommandOperation.cs).
- [Default di classe](../src/Qwerty.Codi.Application/Sessions/ClassActivityState.cs).
- [Outbox](../src/Qwerty.Codi.Application/Sessions/OutboxDispatcher.cs).
- [Osservazioni](../src/Qwerty.Codi.Application/Sessions/ParticipantObservationService.cs).
- [Anteprima dei blocchi](../src/Qwerty.Codi.Application/Sessions/ParticipantPreviewHandlers.cs).
- [Trasporto alunno](../src/Qwerty.Codi.Web/projects/public/src/app/core/participant-realtime.ts).
- [Trasporto docente](../src/Qwerty.Codi.Web/projects/admin/src/app/core/teacher-realtime.ts).
- [Matrice dei test](TEST-COVERAGE-MATRIX.md).

## Ordine di lavoro consigliato

Il prossimo incremento è T9 (laboratorio su 12 PC, installazione, backup e ripristino), con i quattro
residui dell'admin rinviati al laboratorio. Il **collaudo nel browser di T8** è stato eseguito il
7 ottobre 2026 insieme alla pagina dei partecipanti: l'anteprima pubblicata dal browser dell'alunno è
stata letta e resa nel dettaglio del docente (metodo hub, `rev. 2` e `rev. 4`, «coerente col progetto
salvato»). Resta non esercitato solo il **ripiego HTTP** dell'endpoint dedicato, che scatta oltre i
24 KiB della tavolozza: è un ramo di trasporto già coperto dai test locali.
T8 è chiuso e validato localmente il 5 ottobre 2026: comando `PublishWorkspacePreview` con
`IParticipantPreviewCache` (una voce per partecipante, capienza 128, freschezza 30 secondi), query
`GetParticipantPreview` con freschezza, revisione e `partial`, avviso `WorkspacePreviewAvailable` al
gruppo dei docenti senza il contenuto, metodo hub entro 24 KiB, endpoint dedicato entro 256 KiB,
pubblicatore `WorkspacePreviewPublisher` nell'app pubblica (aggrega entro un secondo, ripiega su HTTP)
e lettura nel dettaglio dell'alunno aperto nella regia, con il programma come elenco leggibile. Il
laboratorio su 12 postazioni resta di T9.
T7 è chiuso e **collaudato nel browser** il 5 ottobre 2026: motore ripristinabile (checkpoint e
ripresa in `libraries/exercise-engine`), protocollo del worker (§13.6), macchina del player, worker
dell'app pubblica e servizio `ExerciseRuntime` con la pagina `/lezione` che esegue il progetto e salva
i checkpoint. Include l'ACK di pausa con soglia di 500 ms e `AppliedWithRecoveryRequired` (§13.7) e la
ripresa automatica dopo una sospensione transitoria del permesso (§11.5). La procedura del collaudo è
in `TestResults/t7-collaudo.ps1`. Restano la decisione sulla verifica server (`ServerVerified`, D-10) e
il laboratorio su 12 postazioni (T9). T5 aveva già realizzato:

1. Salvataggio del progetto con revisione, impronta e idempotenza.
2. Checkpoint compatibile con versione del motore e tentativo corrente.
3. Recupero locale senza esecuzione automatica delle direttive storiche.
4. Finalizzazione con grant limitato dopo `EndSession`.
5. Recupero della sessione e dei tentativi dopo riavvio server.

`ListParticipantAttempts` è implementata con lo storico del lavoro.
`GetParticipantPreview` e `PublishWorkspacePreview` sono implementate con T8 e non scrivono nel database.
Il trasferimento della partecipazione con codice monouso resta un requisito futuro di §9.5.
I behavior generici del dispatcher restano una porzione architetturale separata.
L'idempotenza T4 esiste nell'operazione applicativa condivisa e conserva la transazione unica.

I comandi locali di verifica sono nel [README](../README.md#comandi-di-verifica-205).
I launcher mantengono API HTTPS 5200, admin HTTPS 4200 e public HTTPS 4300.
Non usare i dati operativi per test o migrazioni.
Non avviare pubblicazioni o distribuzioni senza richiesta esplicita.
