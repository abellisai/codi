# Stato di sviluppo Codì

Aggiornato: 22 ottobre 2026. Baseline documentale: specifica tecnica 0.58 e funzionale 0.17.
Questo registro accompagna §21.1; va confrontato con i file a ogni ripresa. Non sostituisce le specifiche.

## Eliminazione di una versione pubblicata non più recente: 22 ottobre 2026

**VALIDATO LOCALMENTE.** Su richiesta dell'utente del 22 ottobre 2026 («lasciamo la possibilità di
eliminare una versione, purché non sia pubblicata»), precisata con la sua scelta — *una versione
pubblicata, purché non sia quella più recente* — la tabella delle versioni dell'editor permette di
eliminare una versione con la sua riga compagna privata.

Il vincolo è quello che rende l'operazione sicura: l'**ultima** versione è quella che le prossime
assegnazioni userebbero, quindi non si elimina (per sostituirla se ne pubblica una nuova), e una
versione già riferita da `activity_assignment` non si elimina perché gli alunni ci stanno lavorando.
Il primo caso è `409 LatestExerciseVersion`, il secondo `409 ExerciseVersionInUse`; un numero o un
esercizio inesistenti rispondono `404 ResourceNotFound`.

Il comando è `DeleteExerciseVersion` (policy `Admin`), nel catalogo di §10.4: il catalogo passa a
**51 comandi e 32 query**. Nessuna migrazione: `exercise_version` e `exercise_version_private` sono
immutabili e senza `row_version`, quindi le due eliminazioni usano un predicato sull'identificativo
(`DeletePublishedAsync` e `DeleteByVersionAsync`) invece del controllo di versione del CRUD comune,
che cercherebbe una colonna che non esiste. La cancellazione avviene in una transazione: prima la
riga privata, poi la versione.

Nella pagina il pulsante è a sola icona, compare solo sulle versioni eliminabili e chiede conferma
**sulla riga** («Eliminare la versione N?» con conferma e annulla), come l'archiviazione degli
elenchi; l'esito è l'avviso standard del dispatcher («Versione eliminata») e l'elenco si rilegge.

L'agente ha eseguito questi controlli il 22 ottobre 2026.

| Controllo | Esito |
|---|---|
| Build della soluzione .NET | Uscita 0; 0 errori e 0 avvisi |
| `dotnet format Qwerty.Codi.slnx --verify-no-changes` | Uscita 0 |
| Test .NET completi | Uscita 0; **1.397** superati (398 dominio, 991 integrazione, 8 protocollo), 0 falliti. I 4 nuovi casi di integrazione coprono eliminazione di una versione non recente con la riga privata, rifiuto dell'ultima versione, rifiuto di una versione assegnata e risorsa assente |
| Test Angular admin completi | Uscita 0; **389** superati, di cui 3 nuovi sull'eliminazione |
| ESLint e Prettier sull'intero workspace Web | Uscita 0 per entrambi |
| `node scripts/build.mjs` | Uscita 0; entrambe le app compilano. Avviso preesistente: bundle iniziale admin 558,76 kB contro 500 kB |

Le prove verificano che la versione eliminata sparisca con il proprio contenuto privato lasciando
l'ultima e lo stato `Available`, che l'ultima versione sia rifiutata da `409 LatestExerciseVersion`,
che una versione assegnata sia rifiutata da `409 ExerciseVersionInUse` e che numero ed esercizio
inesistenti rispondano `404`. Lato pagina: il pulsante non compare sull'ultima versione, il primo
gesto non manda nulla e la conferma invia `{ exerciseId, versionNumber }`. Non è stato eseguito un
collaudo nel browser.

### Rilievi della revisione del 23 ottobre 2026, applicati

La revisione di sola lettura della porzione non ha trovato difetti funzionali; ha indicato sei
rifiniture, applicate tutte tranne una, rinviata con motivazione.

| Rilievo | Esito |
|---|---|
| §7.3 non citava il nuovo comando e la riga di `exercise_version` diceva solo «immutabile» | In §7.3 l'elenco dei casi d'uso include `DeleteExerciseVersion` e la tabella dichiara che si elimina solo una versione non più recente e non assegnata (§13.8) |
| La conferma restava aperta dopo un rifiuto (`pendingDeleteVersion` azzerato solo nel successo) | L'azzeramento è in un `finally`: dopo un `409` la domanda si chiude e la riga torna all'azione |
| Nessun test con `versionNumber` come **stringa decimale**, la forma che invia il client (§10.2) | Il caso di successo invia `"1"` come stringa; i casi di rifiuto restano numerici e coprono la tolleranza del converter |
| La conferma in linea non annunciava il cambio di stato e perdeva il focus | Il pulsante di conferma ha un `id` e riceve il focus quando la domanda compare: l'etichetta accessibile dice che cosa sta per succedere |
| Ingresso e conferma dichiaravano precondizioni diverse (`canAct()` contro `busy()`) | Comportamento invariato e motivato nel template: l'eliminazione non dipende dalla bozza, quindi la conferma resta disponibile con modifiche non salvate |
| Il commento sul controllo difensivo diceva che un'altra richiesta poteva inserirsi fra lettura e scrittura | Il commento ora dichiara che è difensivo: `BEGIN IMMEDIATE` prende il lock di scrittura prima delle letture |
| Paragrafo del comando lontano dalla riga di catalogo in `docs/API-ENDPOINTS.md` | Spostato accanto alle altre richieste sulle versioni |
| Mappatura dei codici HTTP senza test diretto (§10.7) | **Rinviato**: è una lacuna preesistente di tutto il dispatcher, non introdotta qui; richiede un banco HTTP sul dispatcher |

Gate rieseguiti dall'agente il 23 ottobre 2026 dopo le correzioni: build .NET 0 errori e 0 avvisi,
`dotnet format --verify-no-changes` uscita 0, **1.397 test .NET** (398 dominio, 991 integrazione,
8 protocollo), **389 test Angular** admin, ESLint e Prettier uscita 0, bundle delle due app
compilato con l'avviso preesistente del budget admin (558,76 kB contro 500 kB).
Nessun collaudo nel browser.

## Esito del salvataggio della bozza: 22 ottobre 2026

**VALIDATO LOCALMENTE.** Su richiesta dell'utente del 22 ottobre 2026 («quando salvo la bozza non va
bene il messaggio lì … usa lo stesso pattern»), il salvataggio della bozza usa ora **l'avviso
standard** dell'applicazione: l'interceptor delle notifiche mostra «Bozza salvata» come per ogni
altro comando, con il titolo preso da `COMMAND_TITLES`. `SaveExerciseDraft` non è più fra i comandi
con esito in pagina.

I messaggi propri sono stati tolti dai tre editor — griglia, Quiz e `ValueProgram` mostravano
«Bozza salvata.» sotto l'intestazione — e dalla barra della pubblicazione, dove lo stesso testo
compariva una seconda volta. Sono state rimosse anche le due segnalazioni rimaste senza uso
(`notice` del Quiz e `savedNotice` di `ValueProgram`). Anche la **copia di una versione** usa
l'avviso standard («Versione copiata nella bozza») e non mostra più due messaggi in pagina: la
pagina ricarica i documenti e basta.

Nella stessa porzione, lo stato dell'esercizio nella barra è diventato un **badge Helm**
(`@spartan-ng/helm/badge`, generato con la CLI Spartan in `projects/admin/src/app/ui/badge`):
`secondary` per Bozza, `default` per Disponibile, `destructive` per Modifiche da pubblicare, con il
testo nel badge e un `title` che spiega come cambiarlo. La conferma della pubblicazione è il badge
che passa a «Disponibile»: il messaggio «Esercizio disponibile per le lezioni.» è stato tolto.

Per non perdere la regola approvata il 16 ottobre — **una sola conferma** per «Rendi disponibile» —
`ApiService` accetta ora l'opzione `silentSuccess` (token `SILENT_SUCCESS`): il salvataggio che fa
parte della pubblicazione non produce un avviso proprio, mentre «Salva bozza» premuto dal docente
sì. Verifica e pubblicazione restano fuori dagli avvisi automatici perché il loro esito è composto:
l'elenco dei problemi e lo stato della bozza si leggono nella pagina.

Nessuna modifica a dominio, backend, catalogo o schema.

**Correzione segnalata dall'utente («quando voglio vedere le versioni e faccio avvia la prova non
va»).** Dopo la rimozione del programma iniziale (porzione qui sotto), una versione nuova ha un
progetto vuoto e la prova di quella versione rispondeva «Il progetto è vuoto», cioè si presentava
come un errore. Ora `ExercisePreview` accetta `trialDisabledReason`: quando la versione non ha
blocchi, la pagina mostra la scena e una riga che spiega che il programma lo compone l'alunno, e
i comandi della prova non sono offerti. Le versioni pubblicate prima conservano il loro progetto e
restano eseguibili.

**Verifica della bozza** («quando faccio verifica versione il messaggio mettilo in toast»):
«Verifica la bozza» annuncia l'esito con un avviso — «La bozza è pubblicabile.» oppure «La bozza non
è ancora pubblicabile.» con l'indicazione che i problemi sono in pagina. L'elenco cliccabile dei
problemi resta al suo posto, perché è quello che serve per correggere; la riga «La bozza è
pubblicabile: non c'è nulla da correggere.» e il messaggio di rifiuto nella barra sono stati tolti.
La validazione che fa parte di «Rendi disponibile» resta muta: l'esito della pubblicazione è
comunque composto e si legge nella pagina.

L'agente ha eseguito questi controlli il 22 ottobre 2026.

| Controllo | Esito |
|---|---|
| Test Angular admin completi | Uscita 0; 386 test superati, di cui 2 sulla verifica ad avviso, 2 sulla prova di versione e 3 su salvataggio, sequenza composta e copia |
| ESLint e Prettier sull'intero workspace Web | Uscita 0 per entrambi |
| `node scripts/build.mjs` | Uscita 0; entrambe le app compilano. Avviso preesistente: bundle iniziale admin 558,72 kB contro 500 kB |

Le prove verificano che un `SaveExerciseDraft` esplicito produca un solo avviso «Bozza salvata»,
che lo stesso comando con `silentSuccess` non ne produca, che la copia produca «Versione copiata
nella bozza», che la verifica mostri l'esito come avviso lasciando i problemi in pagina, che una
versione senza programma non offra la prova e che una con programma la offra. Non è stato eseguito
un collaudo nel browser.

## Niente programma iniziale nell'admin: 22 ottobre 2026

**VALIDATO LOCALMENTE.** Su richiesta dell'utente del 22 ottobre 2026 («dall'admin togli il programma
iniziale»), l'editor di un esercizio `GridPath` non mostra più la sezione «Progetto a blocchi» — il
programma iniziale che l'alunno trovava già nell'editor — né la sua anteprima. Resta la **soluzione
di riferimento**, con il proprio workspace e la propria prova: è l'unico documento a blocchi della
pagina e serve a verificare che l'esercizio sia risolvibile.

Il salvataggio invia `workspaceJson` **vuoto** (`{}`): una bozza che aveva un programma iniziale lo
perde al primo salvataggio successivo. È una scelta dichiarata, non un effetto collaterale: un
progetto iniziale che il docente non vede né corregge sarebbe un'attività fantasma nelle mani dei
bambini. La verifica ammette già il progetto iniziale vuoto (§13.3), quindi la pubblicazione
continua a funzionare e il campo `initial_workspace_json` resta nello schema e nella versione.

Nessuna modifica a dominio, backend, schema, catalogo o API. `EMPTY_INITIAL_WORKSPACE` è lo stesso
documento che l'editor mandava per una bozza nuova.

L'agente ha eseguito questi controlli il 22 ottobre 2026.

| Controllo | Esito |
|---|---|
| Test Angular admin completi | Uscita 0; 381 test superati, con i due casi dell'editor riscritti |
| ESLint e Prettier sull'intero workspace Web | Uscita 0 per entrambi |
| `node scripts/build.mjs` | Uscita 0; entrambe le app compilano. Avviso preesistente: bundle iniziale admin 556,32 kB contro 500 kB |

Le prove verificano che la pagina non contenga più «Progetto a blocchi» e abbia **un solo** editor a
blocchi, che la bozza salvi `workspaceJson: "{}"` anche quando quella letta ne aveva uno composto, e
che la soluzione privata resti l'unico documento inviato con `referenceSolutionJson`. Non è stato
eseguito un collaudo nel browser.

## Limiti dell'editor della griglia: 22 ottobre 2026

**VALIDATO LOCALMENTE.** Su richiesta dell'utente del 22 ottobre 2026, l'editor di un esercizio
`GridPath` espone fra i limiti **solo la durata massima della corsa**, in **minuti** (1–60). La
richiesta è arrivata in due passaggi nella stessa giornata — prima «in secondi», poi «in minuti» — e
l'unità finale è quella in minuti; il campo `maxActiveDurationMinutes` viene convertito in
`maxActiveDurationMs` (× 60 000) nella definizione, quindi il documento di §13.2 non cambia.

Numero di blocchi, profondità di annidamento e passi di esecuzione non sono più campi del docente:
sono budgets tecnici del motore. Restano nel formato e nella bozza — il salvataggio di una bozza
esistente li conserva, invece di azzerarli — e una bozza nuova parte dai valori dell'esempio di
§13.2. Le versioni già pubblicate restano leggibili e la loro impronta non cambia; nessuna modifica
a dominio, backend, schema o catalogo, quindi nessun test .NET in questa porzione.

L'agente ha eseguito questi controlli il 22 ottobre 2026.

| Controllo | Esito |
|---|---|
| Test Angular admin completi | Uscita 0; 381 test superati, di cui 1 nuovo sui limiti e i casi dell'editor aggiornati |
| ESLint e Prettier sull'intero workspace Web | Uscita 0 per entrambi |
| `node scripts/build.mjs` | Uscita 0; entrambe le app compilano. Avviso preesistente: bundle iniziale admin 556,32 kB contro 500 kB |

Le prove verificano che la pagina mostri il solo campo della durata, che 120 000 ms della bozza si
leggano come 2 minuti, che 7 minuti si salvino come 420 000 ms e che `maxBlocks` della bozza resti
quello letto. Non è stato eseguito un collaudo nel browser.

## Eliminazione delle sessioni: 22 ottobre 2026

**VALIDATO LOCALMENTE.** Su richiesta dell'utente del 22 ottobre 2026 («nella parte admin vorrei
poter riuscire a cancellare le sessioni»), l'elenco delle sessioni offre ora l'eliminazione
definitiva di una sessione **conclusa o mai avviata** (§6.1, funzionale 0.14, tecnica 0.52).
Una sessione in corso non la offre: si conclude prima con Stop, così la cancellazione non è mai lo
scorciatoio per fermare la classe. Il pulsante è una **sola icona** con nome accessibile e la
conferma vive **sulla riga** — «Eliminare «…»?» con conferma e annullamento — come l'archiviazione
di scuole, classi, corsi ed esercizi; una prima versione usava un dialogo ed è stata unificata su
richiesta dell'utente nella stessa giornata.

Il comando è `DeleteSession` (policy `SessionController`, revisione attesa obbligatoria): rifiuta
con `409 SessionStateConflict` una sessione in corso, `409 SessionRevisionConflict` una revisione
superata e `404 ResourceNotFound` una sessione assente o di un altro docente. Nella stessa
transazione e nello stesso ordine di dipendenza rimuove permessi conclusivi, tentativi, stati
desiderati, consegne, comandi, outbox, assegnazioni, credenziali dei partecipanti, partecipazioni e
infine la riga di `lesson_session` con il controllo di `row_version`; scuola, classe, lezione,
esercizio e versioni restano. Chat, anteprima e telemetria in memoria si eliminano solo dopo il
commit. Nessuna migrazione, nessuna rotta nuova: il comando passa dal dispatcher admin e risponde
`200` con la sola sessione eliminata.

L'agente ha eseguito questi controlli il 22 ottobre 2026.

| Controllo | Esito |
|---|---|
| Build .NET Release | Uscita 0; nessun avviso o errore |
| Test .NET completi Release, `-m:1` | Uscita 0; 398 dominio, 987 integrazione, 8 protocollo. Totale 1.393, di cui 6 nuovi su `DeleteSession` |
| Test Angular admin completi | Uscita 0; 379 test superati, di cui 3 nuovi sull'eliminazione |
| Test Angular pubblici completi | Uscita 0; 131 test superati |
| ESLint e Prettier sull'intero workspace Web | Uscita 0 per entrambi |
| `node scripts/build.mjs` | Uscita 0; entrambe le app compilano. Avviso preesistente: bundle iniziale admin 556,32 kB contro 500 kB |
| `dotnet format whitespace --verify-no-changes` sui file toccati | Uscita 0 |

I test di integrazione coprono il percorso completo — sessione avviata, alunno entrato, esercizio
assegnato, sessione conclusa — e verificano che tutte le tabelle della sessione siano vuote mentre
il materiale didattico resta; coprono inoltre il rifiuto su sessione in corso, la revisione assente,
la revisione superata e la sessione di un altro docente. Non è stato eseguito un collaudo nel
browser con server reale: la UI è provata dai test DOM. La deroga seed A-09 e il limite di copertura
restano aperti come prima.

## Tipologia nella griglia degli esercizi: 22 ottobre 2026

**VALIDATO LOCALMENTE.** Su richiesta dell'utente del 22 ottobre 2026 («nella griglia degli esercizi
non si vede la tipologia»), l'elenco della libreria degli esercizi mostra una colonna **Tipologia**
fra il titolo e lo stato. L'etichetta è quella italiana già usata nel selettore di creazione —
«Percorso sulla griglia», «Domanda con risposta», «Programma con variabili» — e arriva dallo stesso
registro dei motori, non da una tabella scritta nella pagina: un tipo nuovo compare senza modificare
l'elenco, e un tipo che la build non conosce si mostra con il proprio nome tecnico invece di
lasciare la cella vuota. Il tipo era già nel contratto di `ListExercises` (`exerciseType` di
`ExerciseDto`): nessuna modifica al backend, alle specifiche o al catalogo.

L'agente ha eseguito questi controlli il 22 ottobre 2026.

| Controllo | Esito |
|---|---|
| Test Angular admin completi | Uscita 0; 380 test superati, di cui 1 nuovo sulla tipologia, più l'asserzione aggiunta al caso esistente dell'elenco |
| ESLint e Prettier sull'intero workspace Web | Uscita 0 per entrambi |
| `node scripts/build.mjs` | Uscita 0; entrambe le app compilano. Avviso preesistente: bundle iniziale admin 556,32 kB contro 500 kB |

La sequenza degli esercizi dentro una lezione non è stata toccata: mostra il titolo e le azioni di
riordino, e la tipologia si legge nella libreria. Nessun collaudo nel browser.

## README: logo, avvio e sviluppo: 16 ottobre 2026

**DOCUMENTAZIONE VERIFICATA.** Aggiornati i README principale, Web e `.github` con il logo vettoriale condiviso.
La guida principale descrive clone, prerequisiti, pacchetti, certificati, tre servizi, struttura e dati isolati.
La guida Web descrive l'esportazione del certificato, gli script per app, i test e la verifica del pacchetto statico.
La guida degli agenti collega le istruzioni di avvio e sviluppo.
Il README principale riporta lo schema 12 e il comportamento effettivo delle migrazioni mancanti.
Le specifiche restano alla baseline 0.51 e 0.13; nessun contratto applicativo cambia.

L'agente ha verificato 112 collegamenti locali, 17 ancore e 36 riferimenti agli script npm; nessun errore.
Porte, profilo backend e percorsi dei certificati corrispondono ai file di configurazione.
Il parser PowerShell ha verificato 28 blocchi di esempio senza errori e senza eseguirli.
Prettier sul README Web e `git diff --check` hanno restituito uscita 0.
Copione ed esiti sono in `TestResults/readme-codi`.
I controlli documentali non eseguono restore, installazioni, certificati o avvii applicativi.
Non sono necessarie nuove build o esecuzioni dei test applicativi per questa modifica documentale.

## Pubblicazione admin semplificata: 16 ottobre 2026

**VALIDATO LOCALMENTE CON SERVER REALE.** Implementate le prime quattro modifiche approvate dal docente.
Gli editor `GridPath`, `Quiz` e `ValueProgram` condividono il comando «Rendi disponibile».
Il comando salva le modifiche, verifica i documenti salvati e pubblica la revisione restituita dal server.
«Salva bozza» resta disponibile anche per il primo salvataggio.
La barra resta visibile durante lo scorrimento, anche su mobile.
I messaggi di validazione collegano il problema al campo corrispondente e conservano i valori.
Durante verifica e pubblicazione, i campi e Blockly non accettano modifiche.
Una sola conferma segnala il completamento della pubblicazione.

L'editor e l'elenco mostrano Bozza, Disponibile oppure Modifiche da pubblicare.
Le query admin calcolano lo stato dai documenti persistiti e dalla soluzione privata dell'ultima versione.
Le risposte non espongono la soluzione privata e non richiedono nuove rotte o migrazioni.
La risposta corretta del Quiz ora appartiene alla Signal Form dell'editor.
Il confronto canonico della definizione evita modifiche apparenti dopo la normalizzazione del server.

La ripubblicazione di un contenuto precedente crea una nuova versione più recente.
Ripetere l'ultima pubblicazione conserva invece la stessa versione.
Le assegnazioni esistenti mantengono la propria versione immutabile.
Le modifiche non aggiungono il collegamento alle lezioni o la chiusura automatica della cronologia.

L'agente ha eseguito questi controlli il 16 ottobre 2026.

| Controllo | Esito |
|---|---|
| Build .NET Release senza restore | Uscita 0; nessun avviso o errore |
| Test .NET completi Release, senza nuova compilazione, `-m:1` | Uscita 0; 398 dominio, 981 integrazione, 8 protocollo. Totale 1.387 |
| Test Angular admin completi | Uscita 0; 376 test superati |
| Test Angular pubblici completi | Uscita 0; 131 test superati |
| ESLint e Prettier sull'intero workspace Web | Uscita 0 per entrambi |
| Build delle due applicazioni Angular | Uscita 0; avviso sul bundle iniziale admin: 556,32 kB contro 500 kB |
| Chrome, server reale HTTPS 5326 e database isolato | 11 verifiche superate; nessun errore JavaScript |
| `git diff --check` | Uscita 0 |

Il browser verifica validazione, focus, salvataggio, pubblicazione, stati persistiti e ripubblicazione dopo copia della prima versione.
Le verifiche coprono i tre tipi di esercizio e la barra su desktop e mobile.
L'agente ha controllato visivamente le schermate della barra e del messaggio Quiz.
Copione, registri e risultati sono in `TestResults/semplificazione-pubblicazione/`.
Il database isolato usa lo schema 12; nessun dato operativo è stato modificato.
Una prima esecuzione parallela .NET si è interrotta con `MSB4166`; la successiva esecuzione seriale supera tutti i test.
I test admin richiedono risorse esterne di Blockly; restano gli avvisi preesistenti sul canvas.
L'audit delle dipendenze della porzione Quiz resta applicabile: nessuna dipendenza è cambiata.
Il limite preesistente della copertura resta aperto; questa porzione non dichiara una nuova misura della copertura.

## Salvataggio e pubblicazione ValueProgram: 16 ottobre 2026

**VALIDATO LOCALMENTE CON SERVER REALE.** Corretti i due difetti segnalati nell'admin numerico.
Il primo salvataggio crea la bozza senza versione base.
Gli aggiornamenti inviano `baseRowVersion` e conservano la revisione restituita dal server come stringa.
Il pannello condiviso permette verifica, pubblicazione e copia delle versioni.
La pubblicazione resta disabilitata finché esistono modifiche non salvate, anche nel progetto Blockly.
La copia ricarica progetto, definizione e soluzione privata; il salvataggio successivo usa la revisione nuova.
Catalogo e limiti della bozza conservano i valori letti dal server.
La consultazione delle versioni mostra il programma iniziale in sola lettura.

La form `ValueProgram` ora usa Signal Forms e validatori associati ai campi Spartan.
Un rifiuto conserva i valori; un salvataggio in corso disabilita campi e interazione con Blockly.
Le bozze illeggibili o di uno schema futuro non diventano nuove bozze da sovrascrivere.

L'agente ha eseguito questi controlli il 16 ottobre 2026.

| Controllo | Esito |
|---|---|
| Test Angular admin completi | Uscita 0; 370 test superati, inclusi 10 nuovi casi dell'autore numerico |
| Test Angular pubblici completi | Uscita 0; 131 test superati |
| ESLint e Prettier sull'intero workspace Web | Uscita 0 per entrambi |
| `node scripts/build.mjs` | Uscita 0; entrambe le app compilano. Avviso preesistente: bundle iniziale admin 555,74 kB contro 500 kB |
| Chrome sul pacchetto compilato, con API reale HTTPS 5324 | 9 verifiche superate; nessun errore JavaScript |
| `git diff --check` | Uscita 0 |

Il browser esegue accesso, creazione dell'esercizio e primo salvataggio dalla UI.
Un comando API prepara un programma iniziale completo soltanto nel database di prova.
Due aggiornamenti successivi della bozza passano dalla UI con revisioni successive accettate dal server.
Il collaudo verifica pubblicazione, seconda versione, copia della prima versione, nuovo salvataggio e ricarica della pagina.
La lettura server conferma definizione, progetto e revisione salvati.
La verifica mobile conferma che la tabella non allarga la pagina.
Le schermate desktop e mobile sono state controllate visivamente dall'agente.

Il database isolato è in `TestResults/value-program-admin/data` e usa lo schema 12.
Copione, risultati e registri sono in `TestResults/value-program-admin/`.
Il server usa gli assembly Release esistenti; non cambiano backend o schema del database.
Nessun dato operativo è stato usato per il collaudo.
I test admin completi richiedono risorse esterne di Blockly e sono eseguiti fuori dal sandbox.
Restano gli avvisi preesistenti sul canvas nell'ambiente dei test.
L'audit delle dipendenze della porzione Quiz resta applicabile: nessuna dipendenza è cambiata.
I limiti didattici su messaggi testuali e attesa temporale restano fuori dal perimetro della correzione.

## Pubblicazione delle domande Quiz: 16 ottobre 2026

**VALIDATO LOCALMENTE.** L'editor `Quiz` mostrava soltanto il salvataggio della bozza.
Ora include il pannello condiviso per verifica, pubblicazione ed elenco delle versioni.
La verifica invia la risposta nella soluzione privata, separata dalla definizione pubblica.
La pubblicazione usa la revisione salvata e resta disabilitata con modifiche non salvate.
La copia ricarica domanda, risposta privata e revisione della bozza.
L'anteprima delle versioni `Quiz` mostra il testo della domanda, senza usare il motore della griglia.
La pagina non mostra più comandi o avvisi della griglia per gli editor degli altri tipi.
Su mobile la tabella delle versioni scorre nella propria area senza allargare la pagina.

L'agente ha eseguito questi controlli il 16 ottobre 2026.

| Controllo | Esito |
|---|---|
| Test Angular admin completi | Uscita 0; 360 test superati, inclusi 7 nuovi casi |
| Test Angular pubblici completi | Uscita 0; 131 test superati |
| Test .NET `QuizExerciseJsonTests` e `ExercisePublicationTests`, Release senza nuova compilazione | Uscita 0; 43 test superati. Nessuna modifica al backend |
| ESLint e Prettier sull'intero workspace Web | Uscita 0 per entrambi |
| `node scripts/build.mjs` | Uscita 0; entrambe le app compilano. Budget iniziale admin: 555,74 kB contro 500 kB |
| Spartan `info` e `healthcheck`, senza aggiornamenti | Uscita 0; controlli dei sorgenti superati. Il rapporto segnala pacchetti Spartan più recenti |
| Chrome sul pacchetto compilato, desktop e mobile | 7 verifiche superate; nessun errore JavaScript |
| `npm run audit:runtime -- --json` | Uscita 1; una vulnerabilità alta nella dipendenza transitiva `source-map-js` |

Il test completo admin richiede accesso alle risorse esterne di Blockly.
Il primo comando nel sandbox ha superato i test ma ha restituito errori di rete.
La nuova esecuzione fuori dal sandbox termina con uscita 0.
Restano gli avvisi preesistenti sul canvas nell'ambiente dei test.
L'audit riguarda dipendenze non modificate da questa correzione; nessun pacchetto è stato aggiornato.

Copione e risultati del browser sono in `TestResults/pubblicazione-quiz/`.
Il browser usa risposte API simulate e dati in memoria; non verifica la persistenza operativa.
Le schermate desktop e mobile sono state controllate visivamente dall'agente.
La guida delle form descrive il flusso aggiornato in `docs/ADMIN-SIGNAL-FORMS.md`.

**Verifica delle lezioni, limiti ancora aperti.** La lettura del pacchetto revisionato del 30 settembre
comprende 60 slide con note, 19 pagine PDF e sei progetti Scratch.
`Quiz` copre risposte testuali o intere prestabilite; `ValueProgram` copre gli algoritmi numerici.
I due difetti admin di `ValueProgram` sono risolti dalla porzione successiva descritta sopra.
Il suo motore non dispone di valori testuali o del blocco temporale richiesti dalla lezione 5.
Riordino di cartellini, osservazioni aperte e addestramento con immagini non sono tipologie implementate.
La sequenza della lettera A ordina quattro frasi: non è un percorso spaziale `GridPath`.
Il rapporto `output/Verifica_fattibilita_esercizi_con_Codi.md`, nella radice del corso, è superato.
I 18 test del runner numerico e 12 test applicativi pertinenti alla verifica superano l'esecuzione locale.
I limiti didattici restano aperti; la correzione admin non aggiunge tipologie o blocchi.

## Logo SVG nelle due applicazioni: 16 ottobre 2026

**VALIDATO LOCALMENTE.** La proposta approvata diventa un logo vettoriale con tre blocchi, scritta blu scuro e accento giallo.
`public/brand/codi-logo.svg` contiene il logo completo; `public/brand/codi-mark.svg` contiene il simbolo.
Entrambi usano solo tracciati e forme vettoriali, senza font esterni o immagini incorporate.
I due builder Angular copiano gli stessi file nelle rispettive applicazioni.

L'intestazione comune dell'app pubblica mostra il logo su tutte le pagine.
L'accesso docente e il menu admin mostrano il logo completo.
Il menu compresso mostra il simbolo; il collegamento mantiene il nome accessibile.
Il tema scuro admin usa una versione monocromatica bianca tramite le classi esistenti.
Entrambi gli indici usano il simbolo come icona SVG della scheda del browser.
Il titolo della pagina pubblica iniziale ora conserva l'accento di `Codì`.

L'agente ha eseguito i controlli seguenti il 16 ottobre 2026.

| Controllo | Esito |
|---|---|
| `node node_modules/@angular/cli/bin/ng.js generate @spartan-ng/cli:info --json` | Uscita 0; componenti esistenti confermati, nessuna generazione |
| `node node_modules/eslint/bin/eslint.js .` | Uscita 0 |
| `node node_modules/prettier/bin/prettier.cjs --check .` | Uscita 0 |
| `node node_modules/@angular/cli/bin/ng.js test web.admin --watch=false` | Uscita 0; 353 test superati |
| `node node_modules/@angular/cli/bin/ng.js test web.frontend --watch=false` | Uscita 0; 131 test superati |
| `node scripts/build.mjs` | Uscita 0; entrambe le app compilano. Avviso di budget admin: 555,74 kB contro 500 kB |
| Verifica con Chrome sul pacchetto di produzione | 15 controlli superati: logo caricato, dimensioni, mobile, menu compresso, riapertura, tema scuro, asset e icone delle due aree |

Il copione è `TestResults/logo-codi/verifica-browser.mjs`; gli esiti sono in `TestResults/logo-codi/esiti-browser.json`.
Le schermate sono nella stessa cartella, insieme all'anteprima raster del logo vettoriale.
Il browser usa risposte API simulate: la verifica riguarda la grafica, senza scrivere dati operativi.
Il controllo mobile usa una larghezza di 360 pixel; quello desktop usa 1280 pixel.
L'agente ha controllato visivamente le schermate dell'accesso, del menu e dell'ingresso pubblico.
Nessuna modifica al backend o alle dipendenze. Nessun collaudo sulle postazioni della scuola.
Uso e colori sono nella [guida del logo](../docs/LOGO-CODI.md).

## Rinomina locale in Codì: 16 ottobre 2026

**RINOMINA COMPLETATA.** Compilazione e test superati. Il gate di copertura resta sotto baseline anche sul codice precedente.
Nome del prodotto `Codì`; cartelle, progetti e identificatori tecnici `Codi`.
La soluzione è `Qwerty.Codi.slnx`. Tutti i namespace applicativi iniziano con `Qwerty.Codi`.
Il workspace Angular usa `@codi/api` e i manifest npm hanno il prefisso `codi`.
Configurazione `Codi`, variabile `CODI_BOOTSTRAP_PASSWORD`, cookie e header aggiornati insieme ai relativi test.
Launcher, istruzioni, skill locali, baseline di copertura e documentazione seguono i nuovi percorsi.

Il database locale e la sua copia preesistente cambiano soltanto nome.
Le impronte SHA-256 dei due file coincidono prima e dopo la rinomina.
Le impronte delle 12 migrazioni SQL coincidono prima e dopo la rinomina.
I tipi dei blocchi `aula_*` e il nome IndexedDB restano identificatori persistenti compatibili.
La modifica preesistente a questo registro resta presente.
Il repository GitHub e il suo indirizzo remoto restano invariati per decisione esplicita dell'utente.

L'agente ha eseguito i controlli seguenti il 16 ottobre 2026 sulla soluzione rinominata.

| Controllo | Esito |
|---|---|
| `dotnet restore .\Qwerty.Codi.slnx --locked-mode` | Uscita 0 |
| `dotnet build .\Qwerty.Codi.slnx --configuration Release --no-restore` | Uscita 0, nessun warning o errore |
| `dotnet test .\Qwerty.Codi.slnx --configuration Release --no-build`, con collector e impostazioni di copertura | Uscita 0; 1.384 test: 398 dominio, 978 integrazione, 8 protocollo |
| `node node_modules/@angular/cli/bin/ng.js test --watch=false` | Admin: 353/353. Prima esecuzione public: 130/131, uscita complessiva 1 per il test asincrono del quiz |
| `node node_modules/@angular/cli/bin/ng.js test web.frontend --watch=false`, dopo la correzione del test | Uscita 0; 131/131, totale web 484 test superati |
| ESLint e Prettier sull'intero workspace, poi sul test corretto | Uscita 0 per entrambi i controlli |
| `node scripts/build.mjs` | Uscita 0; entrambe le app compilano, warning preesistente admin: 554,95 kB contro 500 kB |
| `dotnet format whitespace .\Qwerty.Codi.slnx --verify-no-changes --no-restore --verbosity quiet` | Uscita 0 dopo l'allineamento a `.editorconfig` |
| `scripts/test-coverage-gate.ps1` | Uscita 0; 4/4 regressioni del verificatore superate |
| `scripts/assert-dotnet-coverage.ps1` sulla nuova raccolta | Uscita 1; Application 96,24% contro 97,33%, Domain 94,23% contro 96,47% |
| Controllo dei percorsi e dei collegamenti locali | Nessun file mancante fra i 980 iniziali; nessun link non risolto in 64 documenti |
| `git diff --check` | Uscita 0 |

**Copertura preesistente sotto baseline.** Il confronto usa una copia isolata del commit `6c1c4c3`, precedente alla rinomina.
Anche quella copia supera i 1.384 test ma non supera il gate: Application 96,22%, Domain 94,23%.
La raccolta nuova è in `TestResults/rinomina-codi-5db61c9199a8472dac5091d258268ccc`.
La raccolta precedente è in `TestResults/baseline-prima-rinomina-risultati`.
Le soglie sono invariate. Api e Infrastructure superano le rispettive soglie in entrambe le raccolte.
La copertura sotto baseline resta da completare in una porzione dedicata.

**Test del quiz.** La prima esecuzione riproduce l'instabilità già registrata il 14 ottobre.
Il test aspettava un solo timer prima di controllare il calcolo asincrono dell'impronta.
Ora attende l'esito visibile e ripristina la risposta simulata prima di ogni caso.
Il codice di produzione del quiz non cambia.
La formattazione C# ora rispetta `.editorconfig`.
Nessun nuovo collaudo nel browser o sulle postazioni del laboratorio.

Dettagli e nuovi nomi nella [guida alla rinomina](../docs/RINOMINA-CODI.md).

## Chat docente–alunno: il collaudo nei due browser — 2026-10-14

**VALIDATO NEL BROWSER.** Su richiesta dell'utente del 14 ottobre 2026 («collauda chat»): la chat era
provata dai soli test DOM, senza due browser veri. Il collaudo è stato eseguito su un sistema in
esecuzione — API `https://localhost:5310` in Development con dati isolati su schema 12, admin
`https://localhost:4310`, alunni `https://localhost:4301` — con il copione di preparazione
`TestResults/chat-collaudo.ps1` (fasi `seed`, `assign`, `inspect`) e le evidenze in
`TestResults/chat-collaudo/evidenze.md`. Nessun dato operativo toccato.

**12 verifiche su 12.** L'alunno entra con il codice di lezione e il nome; l'assegnazione e
l'abilitazione gli arrivano **senza ricaricare**; il docente apre la chat dalla card e scrive; il
messaggio arriva all'alunno **senza ricaricare**, e il pannello si apre da sé; l'alunno risponde; la
risposta compare nella card del docente in tempo reale; il contatore dei non letti sale a ogni
messaggio e si azzera alla rilettura (server: 2 → 0); la pausa del singolo toglie il campo e lascia
la riga «Puoi rispondere quando il maestro apre l'attività» con il thread ancora leggibile; la
ripresa lo restituisce; la ricarica rilegge il thread dal server; `GetParticipantMessages` restituisce
gli stessi tre messaggi nello stesso ordine.

**La bozza si svuota solo alla conferma.** In entrambi i lati la form conserva il testo scritto finché
il messaggio confermato compare nel thread riletto dal server: verificato nel browser, non solo nei
test.

**Osservazione fuori dal perimetro della chat.** Con la pagina dell'alunno in secondo piano il battito
si ferma, la connessione diventa non operativa oltre la soglia di §11.6 e la conferma
`CommandAcknowledged` viene **rifiutata** (`SessionPermissionDenied`): l'API registra un `fail:` di
SignalR con lo stack di `AcknowledgeCommand` e il bambino legge «Errore non previsto durante la
richiesta.». Con la connessione fresca — pausa dopo il rinnovo — non si osserva né il rifiuto né
l'avviso, quindi **il rifiuto non è causato dalla pausa** ma dalla connessione scaduta. Resta che un
esito previsto (§12.2 ammette le conferme non applicabili) si presenta come errore imprevisto al
bambino e come eccezione con stack all'operatore: è materia del protocollo del player (T4/T7), non
della chat, e non è stata corretta qui.

**Esiti attesi, non difetti.** Il `403` su `GET /api/v1/public/participant/finalizations` a ogni
caricamento della pagina dell'alunno fuori dalla finestra di finalizzazione (policy
`FinalizingParticipant`, concessa dal solo `EndSession`); il contatore dei non letti che comprende i
messaggi arrivati mentre il pannello del docente è aperto, finché non rilegge (`ParticipantChatTests`);
gli avvisi SignalR di metodo client assente, rumore preesistente.

**Che cosa NON è stato esercitato.** Un alunno per volta, non dodici postazioni; nessun PC della
scuola; il rifiuto della scrittura nella vista di attesa (`Waiting`), non provato (provata la pausa);
nessun uso da due macchine diverse.

## Chat lato alunno: il bambino legge e risponde — 2026-10-14

**VALIDATO LOCALMENTE.** Su richiesta dell'utente del 14 ottobre 2026 — «quando l'insegnante scrive
ad un alunno questo deve rispondere». Il contratto della chat c'era già lato server e lato regia;
mancava **del tutto il lato alunno**, quindi il server poteva consegnare un messaggio che nessuno
mostrava e il bambino non aveva un campo per rispondere.

**Trasporto.** `ParticipantRealtime` registra `ParticipantMessageReceived` e raccoglie i messaggi in
`chatIncoming`: il messaggio non è un comando, quindi non passa dalla coda delle applicazioni e non
tocca l'autorizzazione temporanea. Il segnale si svuota con il collegamento, perché il thread di una
lezione non vale per la successiva.

**Servizio.** `ParticipantChat` compone il thread da **due flussi**: la lettura con `GetOwnMessages`
all'apertura e a ogni ritorno del collegamento — l'hub non ripete ciò che è arrivato mentre la
pagina non c'era — e i messaggi annunciati dall'hub, deduplicati per identificativo. Un messaggio
arrivato mentre la lettura era in volo si **accoda** invece di sparire. `PostParticipantMessage`
porta il testo senza spazi ai bordi e una chiave nuova per invio; la regola di scrittura è quella del
server, cioè pagina al lavoro: né in attesa né in pausa. Il limite di 300 grafemi si verifica anche
qui, perché il bambino abbia subito una risposta comprensibile invece di un rifiuto dopo il viaggio.

**Pagina.** `ChatPanel` si apre **da sé** quando c'è un messaggio — a otto anni non si cerca una
conversazione — e senza messaggi resta una sola riga con il pulsante per scrivere. Il thread si legge
anche a pagina ferma e in quel caso il campo non c'è, con una riga che spiega perché: la chat sta con
i controlli, come già faceva il server. La bozza si conserva se il server rifiuta.

**Contratti.** La chat non era descritta in **nessuna** delle due specifiche, mentre il codice
citava §8 e §15.1 (dove §15.1 parla d'altro). Registrata ora in entrambe: §8.1 e il registro della
funzionale (0.11), §15.7 e le righe di catalogo della tecnica (0.49), con i quattro nomi
`PostTeacherMessage`, `PostParticipantMessage`, `GetParticipantMessages` e `GetOwnMessages` che
portano i cataloghi a **49 comandi e 32 query**.

| Controllo eseguito dall'agente | Esito |
|---|---|
| `npm run test:frontend -- --watch=false` | **131 test** (14 file), 0 falliti — erano 117: 8 sul servizio, 5 sul pannello, 1 sul trasporto |
| `npm run test:admin -- --watch=false` | **353 test** (37 file), 0 falliti |
| `npm run lint`, `npm run format:check` | uscita **0** |
| `npm run build` | uscita **0**, entrambe le app compilano; resta il warning preesistente di budget dell'admin |
| `dotnet build` / `dotnet test` | 0 warning, 0 errori; **1.384 test** (398 dominio, 978 integrazione, 8 protocollo), 0 falliti — il backend non è stato toccato |

**Che cosa NON c'è, dichiarato.** Nessun collaudo nel browser: la chat lato alunno è provata dai test
DOM, non da due browser veri con un maestro che scrive e un bambino che risponde. Restano quindi non
esercitati in aula il ritardo fra invio e comparsa, la leggibilità del pannello a distanza e il caso
di dodici alunni collegati, che appartiene al laboratorio su 12 PC (T9). Il thread non si persiste e
non si persisterà per questa decisione: chi ricarica la pagina lo rilegge dal server finché la
lezione dura, e dopo la lezione non c'è. **Osservazione non bloccante**: in una esecuzione su tre di
`npm run test:frontend` è fallito il caso di `quiz-panel.spec.ts` «conferma la risposta…» — il suo
`flush()` fa un solo giro dell'event loop e l'impronta asincrona può risolversi dopo. È una
fragilezza **preesistente** del test, non introdotta qui, e il rimedio è la resa più robusta già usata
negli altri spec (`whenStable` più `setTimeout`). Il gate resta affidabile: nelle esecuzioni
successive la suite è passata intera.

## Chat docente–alunno: l'app admin torna a compilare — 2026-10-14

**VALIDATO LOCALMENTE.** La porzione in lavorazione della chat fra il docente e il singolo alunno
(§8, §15.1) lasciava l'app admin **non compilabile**: `npm run build` usciva 1 e i gate di lint,
formattazione e test admin erano rossi. Il backend era già verde ed è rimasto verde.

**Difetti corretti**, tutti in `participant-card.ts|html`, l'unico punto che rompeva la compilazione:
`effect` usato dalla conferma della bozza senza essere importato da `@angular/core`; `FormRoot`
importato ma assente dall'array `imports`, quindi `[formRoot]` non era una proprietà nota di
`<form>`; la direttiva di campo di Signal Forms non importata, quindi `[formField]` non era nota di
`<textarea>`. Corretto anche il conteggio dei grafemi, che leggeva una variabile mai usata e faceva
fallire ESLint. Le tre carte di prova (`participant-card.spec.ts`, `session-detail.spec.ts`,
`session-participants.spec.ts`) non conoscevano il nuovo campo obbligatorio `unreadMessages`.

**Prove aggiunte** (`participant-card.spec.ts`, 5 casi): non letti e thread con i due autori distinti;
chat chiusa assente ma conteggio dei non letti visibile; thread vuoto distinto dal problema di
lettura; invio del testo normalizzato dalla form con la bozza svuotata **solo alla conferma dal
thread riletto**; invio vuoto rifiutato dalla validazione con l'errore accessibile `role="alert"`.

| Controllo eseguito dall'agente | Esito |
|---|---|
| `dotnet build` / `dotnet test` | 0 warning, 0 errori; **1.384 test** (398 dominio, 978 integrazione, 8 protocollo), 0 falliti |
| `npm run test:admin -- --watch=false` | **353 test** (37 file), 0 falliti — erano 348, cinque nuovi |
| `npm run test:frontend -- --watch=false` | **117 test** (12 file), 0 falliti |
| `npm run lint`, `npm run format:check` | uscita **0** |
| `npm run build` | uscita **0**, entrambe le app compilano |

**Che cosa NON c'è, dichiarato.** La correzione è del solo lato admin. Il **lato alunno della chat
non esiste**: nessuna UI usa `GetOwnMessages` o `PostParticipantMessage` e
`participant-realtime.ts` non registra `ParticipantMessageReceived`, quindi il server può consegnare
un messaggio che nessuno mostra. La chat **non è descritta in nessuna delle due specifiche**: la
funzionale (§8) la nomina solo per escludere una chat fra bambini. Restano inoltre la formattazione
dei file `.cs` (1.684 errori `ENDOFLINE` su 23 file, chat compresa) e la credenziale della deroga
A-09. Formattati anche `value-program-editor.css|html`, fuori formato già nel committato.

## Fase 3: il terzo tipo di esercizio `ValueProgram` — **chiusa** (2026-10-10)

**CHIUSA E COLLAUDATA NEL BROWSER (2026-10-10).** Su richiesta dell'utente del 10 ottobre 2026:
introdurre un terzo tipo di esercizio con un modello di esecuzione diverso dal percorso — variabili,
confronto, `se`, uscita visibile e un valore letto dall'alunno. Il tipo si chiama `ValueProgram`, la
versione del motore `valueprogram-1`.

**Collaudo nel browser: 14 verifiche su 14.** Guidato con Playwright 1.64 e Chrome reale contro il
sistema in esecuzione (API 5200 in Development con dati isolati, admin 4200, alunni 4300, schema 12):
la form dell'autore crea l'esercizio e lo compone; l'alunno entra col codice, vede le variabili
dichiarate dal docente, l'uscita e il campo del valore, e «Fai correggere» chiede l'esito al server —
la pagina non lo calcola da sé. Evidenze e copione in `tmp/collaudo-fase3/`.

**Che cosa è, e perché è diverso.** In `gridpath-1` lo stato è dove si trova un personaggio su una
griglia e la corsa finisce al traguardo. Qui lo stato è un **ambiente di variabili con il loro
valore**, l'esito è quello che il programma **mostra**, e la corsa può fermarsi **in attesa di un
valore** che l'alunno deve fornire. La sospensione è la differenza che conta: la corsa si ferma
sull'istruzione che chiede, con le variabili come sono, e riprende da lì. Non si ricomincia.

**Fatto e verificato** (commit `91f480a`, `92ee54e`, `a6c80e8`, `4e44b83`, `e2351ec`, `e237149`, `1765ecf`, `08ad9f6`, `29fb761`, `6fa0c48`):

- **Modello e interprete .NET** (`ValueProgramRunner`): variabili, somma, confronto, `se`/`altrimenti`,
  ciclo contato, lettura di un valore, uscita, budget di passi, stato serializzabile. Un passo consuma
  budget solo se esegue qualcosa. **Un valore mai impostato non vale zero**: il motore rifiuta invece
  di usare zero, perché un risultato sbagliato con l'aria di essere giusto è il peggior esito
  didattico. 18 casi di prova.
- **Registro dei motori**: `valueprogram-1` è la terza voce; il catalogo distingue **comandi** (che
  stanno in catena) da **espressioni** (che si annidano dentro un comando). Stessa dichiarazione in
  .NET e nel browser.
- **Definizione e database**: due sezioni proprie al posto di scena e completamento — `variables` e
  `testCases` — più i messaggi di esito; migrazione **0012** che estende il vincolo sul tipo
  dichiarato (il vincolo è nel database e non legge il registro, quindi ogni tipo nuovo richiede una
  migrazione: è la conseguenza voluta di averlo scritto dove il database lo fa rispettare).
- **Validatore del progetto**: gli ingressi arrivano dal catalogo e sono di due nature. Un ingresso di
  valore contiene un'espressione da calcolare, un ingresso di sequenza contiene un comando da
  eseguire. Un'espressione in catena è un programma che non parte; un comando dentro un valore è un
  valore che non si sa calcolare. Il percorso dichiara a sua volta l'ingresso della ripetizione, che
  prima era cablato nel validatore.
- **Motore nel browser** (`value-program-runner.ts`): la stessa esecuzione in TypeScript, con la
  stessa sospensione e gli stessi rifiuti. 19 casi di prova che sono la **controparte** dei casi .NET,
  omonimi compresi: una divergenza fra le due implementazioni non deve aspettare l'aula.
- **Protocollo del player** (`player-protocol.ts`): la scena dei valori è un tipo nuovo e non un
  allargamento di quella pubblicata, l'attesa di un valore è un evento perché la pagina deve **agire**,
  e il valore entra nella corsa dal di fuori (`ProvideValueRequest`), che è ciò che rende la ripresa
  riproducibile. `PlayerSceneView` è l'unione con il discriminante `kind`.
- **Checkpoint** (`parseValueProgramCheckpointState`): il formato ha il suo nome e il suo verificatore,
  che rifiuta un checkpoint di un altro motore anche quando il numero di schema coincide.
- **Compilatore** (`value-program-compiler.ts`): dal progetto a blocchi alle istruzioni, con diagnosi
  che nominano il blocco e dicono che cosa manca. La traduzione è totale: o un programma, o i problemi.
- **Pannello dell'alunno** (`value-program-panel.ts`): variabili col loro valore, uscita, campo del
  valore, prova che si ferma al primo valore mancante e riprende dallo stato salvato. Il ramo è
  collegato nella pagina dell'alunno con `usesValues`, `usesGrid` e `showsQuiz`.

**Dal 12 ottobre 2026: il vocabolario del corso, e il docente che compone e prova.** Su richiesta
dell'utente, che ha indicato il programma della lezione 4 blocco per blocco e ha fatto notare che il
corso parla di «reporter» da inserire nei fori dei blocchi. Tre commit: `137ddd2`, `228bb3f`, `05fa995`.

- **Il catalogo parla la lingua del corso**, non una mia equivalente: «🏁 quando si clicca sulla bandiera
  verde», «porta … a …», «cambia … di …», «dire …», «chiedi [domanda?] e attendi», il reporter
  «risposta», «se … allora», «ripeti … volte». Un bambino che li ha visti in Scratch li riconosce.
- **Il reporter `risposta` è un'espressione a sé**, non una variabile del docente: chiedere di nuovo la
  sostituisce mentre le variabili conservano i valori — è la differenza che l'esercizio della somma
  mette alla prova (proposta del corso, riga 166). Il motore la tiene a parte e la salva nello stato.
- **`cambia … di …` è un'istruzione propria** e non una scorciatoia per «porta … a … + …»: nel corso è
  un altro blocco, e non ripete il nome della variabile due volte.
- **`chiedi` porta la domanda e non una variabile.** La domanda è l'unico campo di **testo libero** del
  catalogo: la forma si verifica — c'è e non è vuota — il contenuto no, perché verificarlo contro un
  elenco direbbe al docente quali domande può fare.
- **Il docente compone e prova**: la form dell'autore monta un workspace Blockly con il catalogo dei
  valori, e quello che scrive è anche il **punto di partenza** dell'alunno. L'**anteprima** esegue il
  programma su **tutti** i casi di prova — non solo sul primo che non torna — con lo stesso compilatore
  e lo stesso motore degli alunni, in una funzione pura (`value-program-preview.ts`).
- **L'esercizio delle carte è una prova**, nel browser (`IlProgrammaDelleCarte_DelCorso…`) e nel server
  (`IlProgrammaDelleCarte_SuperaIlCasoDellaLezioneQuattro`): le tre terne del corso danno 2, 1 e 0, e il
  caso verifica insieme che il motore conti e che il vocabolario esista davvero.

**Aperto e dichiarato: il gesto di trascinamento nel copione del collaudo.** `tmp/collaudo-vocab/` guida
Chrome e verifica 4 cose su 7: l'accesso, il workspace montato, il cassetto che offre **tutto** il
vocabolario del corso (verificato leggendo le etichette vere, senza spazi perché Blockly disegna ogni
parola in un nodo suo), e l'anteprima che mostra il caso con il suo esito. **Non** riesce a simulare con
Playwright il trascinamento di un blocco dal cassetto al progetto: il blocco non si sposta, quindi il
progetto resta vuoto e il salvataggio scrive il progetto vuoto. Il difetto è **del copione**, non del
prodotto: il workspace si monta, il cassetto c'è, e `value-program-preview.spec.ts` prova che un progetto
composto si compila ed esegue. Un collaudo che trascina davvero i blocchi resta da fare, ed è la cosa che
proverei per prima riprendendo. La lezione, la stessa di prima: `.blocklyBlockCanvas` è anche il
contenitore del cassetto, quindi leggere i blocchi senza escludere `.blocklyFlyout` fa sembrare composto
un progetto vuoto — l'ho scoperto proprio correggendo quell'asserzione.

**Difetto chiuso, con la lezione che ha lasciato.** La compilazione di un ramo «se» con il blocco
«confronta» annidato dentro era rimasta senza prova, perché la forma del documento composta
dall'aiutante di prova non era quella giusta. La causa vera era **nell'aiutante**: spalmare due
volte `ingresso(...)` sullo stesso blocco non unisce gli ingressi, perché entrambi scrivono la
proprietà `inputs` e il secondo sostituisce il primo — il terzo ingresso sopravviveva e gli altri due
sparivano. Il compilatore era corretto dal principio, e la forma autorevole è quella che il server
verifica da tempo: `{"inputs":{"VALUE":{"block":{…}},"next":{"block":{…}}}}`. La prova del confronto
ora c'è e passa. La lezione è che un aiutante di prova sbagliato fa sembrare sbagliato il codice
provato, e che la forma del documento va presa dalla fonte che la verifica, non da un esempio scritto
a mano accanto al codice da provare.

**Fuori da questa fase, e dichiarato.** Tutto ciò che il contratto della fase chiedeva è fatto e
collaudato. Restano, e non erano nella fase:

- **Il worker del player**: il pannello esegue il motore sulla pagina. Il worker dedicato, il permesso
  operativo e il checkpoint periodico sono il passo successivo per il collaudo in laboratorio, e non
  sono collegati.
- **Prova di componente del pannello**: il ramo nuovo della pagina è verificato dal collaudo nel
  browser, dal compilatore di template e dal motore. Una prova di componente lo fisserebbe anche
  senza un browser.
- **Prova su più postazioni**: un alunno per volta nel collaudo, come per la fase precedente.

**Stato dei controlli a questo punto**: 1.348 test .NET (383 dominio, 8 protocollo, 957 integrazione),
465 test Angular (117 pubblici, 348 admin), build, lint e formattazione puliti, e **14 verifiche su 14
nel browser** per il percorso dell'alunno (collaudo del 10 ottobre). Il collaudo del vocabolario e del
workspace dell'autore è a **4 su 7**, con il gesto di trascinamento aperto qui sopra.

## Collaudo completo nel browser — 2026-10-09

**COLLAUDATO NEL BROWSER (2026-10-09).** Su richiesta dell'utente. Guidato con Playwright 1.64 e
Chrome reale, non dai test automatici: la differenza è che qui il sistema è un binario che non
controlliamo, e il collaudo ha trovato tre difetti che i test unitari non vedevano.

**Ambiente.** API su `https://localhost:5200` in ambiente **Development** con dati isolati
(`Codi__DataDirectory` in `tmp/collaudo-browser/dati`, banca dati creata da zero: schema 11 al primo
avvio), admin su `https://localhost:4200/admin/` e alunni su `https://localhost:4300/` con i server
di sviluppo Angular. Nessun dato operativo toccato. I copioni sono in `tmp/collaudo-browser/`
(`docente.mjs`, `alunno.mjs`, `griglia.mjs`, `griglia-alunno.mjs`); le evidenze, 18 schermate e i due
file di sessione, in `tmp/collaudo-browser/evidenze/`.

| Percorso collaudato | Esito |
|---|---|
| Docente: accesso, creazione di un esercizio di **percorso** dall'elenco, editor della griglia montato con i suoi quattro blocchi | **ok** |
| Docente: creazione di un esercizio **dichiarativo** dall'elenco con il tipo scelto, form dichiarativa al posto di quella della griglia, bozza salvata con i suoi campi | **ok** |
| Docente: preparazione della sessione, pubblicazione, assegnazione, controlli attivi, codice di lezione di otto cifre | **ok** |
| Alunno: ingresso con il codice, domanda e rappresentazione a schermo, **nessun** pannello del programma né Blockly | **ok** |
| Alunno: risposta corretta → «Giusto!» dal server; risposta sbagliata → messaggio dell'altro ramo | **ok** |
| Docente: il lavoro dell'alunno è un verbale di risposta `{"answer":"6"}` con il motore `quiz-1` dichiarato dall'assegnazione | **ok** |
| Alunno (tipo a blocchi): ingresso in una seconda lezione, scena disegnata con 20 celle, editor Blockly montato, prova del programma che risponde | **ok** |

Totale: **22 verifiche su 22**, divise in 9 per la preparazione del docente, 9 per il percorso
dichiarativo dell'alunno e 4 per il tipo a blocchi.

**Difetti trovati dal collaudo, e corretti.** Nessuno dei tre era visibile ai test automatici, ed è
la ragione per cui il collaudo è stato fatto.

1. **Il salvataggio dell'alunno partiva con il nome sbagliato del campo.** Il pannello dichiarativo
   inviava `projectRevision`, mentre il contratto del server si chiama `clientProjectRevision`: il
   server rifiutava il corpo con `InvalidPayload` e il bambino leggeva «Il corpo della richiesta non
   corrisponde al formato atteso». Corretto in `quiz-panel.ts`; la prova ora fissa anche il **nome**
   del campo, non solo il valore, perché i test unitari erano passati proprio perché leggevano il
   nome sbagliato.
2. **L'impronta del lavoro non veniva attesa.** `workspaceDigest` è asincrona e il pannello la usava
   come se fosse sincrona: nel corpo partiva `{}` al posto di 64 caratteri esadecimali. Corretto
   attendendola; la prova ora pretende una stringa di 64 caratteri.
3. **L'anteprima del lavoro non veniva pubblicata.** La card del docente restava vuota per un
   esercizio dichiarativo, perché nessuno pubblicava il verbale come anteprima. Il pannello ora la
   pubblica alla conferma (§15.5), e la pagina dell'alunno resta l'origine dell'anteprima.

**Limiti del collaudo.** Un alunno per volta, non dodici postazioni: la concorrenza resta coperta
dai test, non da questa prova. Le schermate sono l'evidenza visiva e non sostituiscono un giudizio
umano su leggibilità e adeguatezza ai bambini di otto anni. Non sono state provate: la pausa e la
ripresa durante una risposta, il recupero dopo riavvio del server durante una sessione, e il
passaggio a un altro PC con il codice monouso (§9.5, requisito futuro). Restano la prova sui **PC
della scuola** e i materiali fisici, che non dipendono da questo collaudo.

## Primo tipo dichiarativo `Quiz` (`quiz-1`) — 2026-10-09

**VALIDATO LOCALMENTE (2026-10-09).** Su richiesta dell'utente del 9 ottobre 2026 («procedi con il
passo successivo»), dopo la fase 0 del registro dei motori. Risponde alla domanda del corso: gli
esercizi che chiedono di leggere una rappresentazione e produrre un valore non hanno bisogno di un
progetto a blocchi.

**Che cosa è stato costruito** (tecnica §13.9.1):

- **dominio**: `QuizEngine` (`Quiz`, `quiz-1`, schema 1) e `QuizExerciseData`, che dichiara catalogo
  dei blocchi **vuoto**, nessun blocco di ingresso e `CheckpointSchemaVersion` 0; i dati del tipo sono
  `QuizData` (rappresentazione, domanda, modo di confronto `Text`/`Integer`, messaggi), e il confronto
  fra risposta data e attesa è una regola del dominio;
- la regola che impone un punto di ingresso vale ora **solo dove esiste un catalogo**: per un tipo
  senza blocchi l'elenco vuoto è la risposta giusta, non un diniego, e una definizione che ammette
  blocchi su un motore che non ne ha è rifiutata;
- **documento**: sezione `quiz` al posto di `scene` e `completion`, letta e scritta dalla versione del
  motore; la scrittura **omette le sezioni nulle**, così il documento canonico di un esercizio di
  percorso resta identico e l'impronta di una versione già pubblicata non cambia. La prova che blocca
  il formato ha trovato subito il difetto opposto (`"quiz":null` in coda al documento), corretto prima
  di procedere;
- **lavoro del tentativo**: un **verbale di risposta** `{"answer":"testo"}` nello stesso campo del
  progetto a blocchi. `ExerciseWorkspaceValidator` sceglie la forma dal **tipo dichiarato** dalla
  definizione, non dal contenuto ricevuto, e i tre percorsi — salvataggio dell'alunno, verifica della
  bozza, pubblicazione — passano da lì;
- **correzione sul server**: la risposta attesa vive nella **soluzione privata** della versione
  pubblicata e non esce mai; la definizione pubblica non la contiene (provato). Nuova query
  `GetQuizOutcome` (policy `AdmittedParticipant`) che restituisce corretto/sbagliato e i due messaggi,
  **senza** la soluzione e senza punteggio. Senza soluzione privata il server non dichiara sbagliata
  una risposta: non corregge;
- **pagina dell'alunno**: `QuizPanel` al posto di progetto e programma, con il pannello del programma
  spento — non c'è nulla da provare — e nessun passaggio dal worker;
- **client**: il registro Angular conosce `quiz-1` con `blockly: false`; un motore sconosciuto resta
  spiegato al bambino, e la sezione del programma continua a comparire in attesa perché non dipende
  più dal tentativo ma dal tipo.

**Che cosa non è stato fatto.** Nulla di necessario perché il tipo dichiarativo funzioni dal docente
all'alunno. Non è stato eseguito il **collaudo nel browser**: i launcher non sono stati avviati, e le
pagine sono verificate dai test Angular e dalle build, non da un uso reale.

**Vista del verbale nel pannello del docente** (chiusura di questa porzione). `ParticipantAssignmentDto`
espone ora `EngineVersion` — quella **fissata dall'assegnazione**, non quella dell'anagrafica — e il
dettaglio dell'alunno legge il lavoro secondo quella versione: un progetto a blocchi resta un elenco di
blocchi, un verbale di risposta diventa la risposta scritta, e una risposta vuota si legge «Nessuna
risposta scritta» invece di sembrare una card senza nulla da mostrare.

**La form dell'editor per il tipo dichiarativo** (parte di questa porzione). La pagina
dell'esercizio instrada sulla form del tipo: `QuizEditor` compone rappresentazione, domanda, modo di
confronto, i due messaggi e la **risposta attesa**, che salva nella soluzione privata della versione
e non nella definizione. L'instradamento è per **tipo** (`findEngineByType`), non per versione del
motore: l'anagrafica porta il tipo e la versione vive nella definizione, che può non esistere.

Difetto trovato e corretto durante la lavorazione: l'instradamento usava `findEngine`, che cerca per
**versione**. Su un esercizio di percorso — tipo `GridPath`, versione `gridpath-1` — la ricerca non
trovava nulla, quindi la pagina sceglieva la form dichiarativa e l'editor della griglia non si
montava più. L'hanno segnalato 21 test dell'editor della griglia; il difetto è stato isolato con una
prova diagnostica temporanea e corretto aggiungendo la ricerca per tipo.

**Il tipo di esercizio sull'anagrafica** (parte di questa porzione). Il tipo si sceglie alla
creazione e non si cambia più: è una colonna di `exercise` (`exercise_type`, migrazione **0011**) con
un vincolo che ammette i tipi registrati, un valore predefinito `GridPath` che migra gli esercizi
creati prima della colonna, e un selettore nella pagina degli esercizi. Serve perché l'editor deve
sapere **quali campi mostrare** anche quando la bozza non esiste ancora: la definizione della bozza
dichiara il tipo, ma la bozza può non esserci. `CreateExerciseCommand` richiede ora il tipo, e
`ExerciseDto` lo espone.

| Controllo eseguito dall'agente | Esito |
|---|---|
| `dotnet build .\Qwerty.Codi.slnx` (Release) | Uscita **0**, **0 errori** |
| `dotnet test` (Release) | **1.286 test** (347 Domain, 931 Integration, 8 Protocol), **0 falliti** || Test nuovi | 19 sul formato dichiarativo, 5 sul percorso completo, 1 sulla migrazione del tipo di esercizio, 4 sul pannello dell'alunno, 4 sulla form dell'editor dichiarativo, 6 sulla lettura del lavoro per il docente |
| Test aggiornati | `SessionDiscoveryTests` (catalogo a 29 query), `SessionJoinCodeMigrationTests` (schema alla versione più alta e nuova colonna), `AssignmentUseCaseTests` (versione del motore nel dettaglio), le finzioni di `Exercise` e di `ParticipantAssignment` nei test Angular, gli helper `CreateExercise` dei test .NET |
| Angular pubblico | **117 test**, 0 falliti |
| Angular admin | **280 test**, 0 falliti |
| Build delle due applicazioni | Uscita **0** |

## Registro dei motori degli esercizi (T10) — 2026-10-09

**VALIDATO LOCALMENTE (2026-10-09).** Su richiesta dell'utente del 9 ottobre 2026 («e se volessi
estendere la tipologia di esercizi oltre a GridPath con le tipologie di esercizi dichiarati nella
lezione?»). L'analisi di quella richiesta è in
[output/Verifica_fattibilita_esercizi_con_Codì.md](../../output/Verifica_fattibilita_esercizi_con_Codi.md):
il corso usa variabili, input, operatori, condizione e output, che il motore non ha.

**Che cosa è cambiato.** Il tipo di esercizio era cablato in tre classi di dominio e in due punti
applicativi. Ora le regole di una versione del motore vivono in **una descrizione** e le versioni si
registrano (tecnica §13.9):

- **`IExerciseEngineDescriptor`** dichiara tipo, versione, schema di definizione, schema del
  checkpoint, blocchi **con i campi di ciascuno**, blocco di ingresso, costruzione dei dati del tipo e
  regole del tipo; **`ExerciseEngineRegistry`** elenca le versioni conosciute e non interpreta una
  versione futura come quella corrente;
- **`GridPathExerciseData`** è la voce registrata: contiene le regole che prima erano sparse fra
  `ExerciseDefinition`, `ExerciseBlockCatalog` e `ExerciseWorkspaceJson`;
- **`ExerciseDefinition`** conserva la parte generale — schema, tipo, versione, titolo, consegna,
  blocchi, limiti — e i **dati propri del tipo** (`IExerciseTypeData`); per `GridPath` sono
  `GridPathData` (scena, completamento, messaggi di esito);
- la definizione **si compone da una descrizione**, non da un numero: `Create` riceve il motore e lo
  schema dichiarato deve coincidere con quello della versione;
- i **campi dei blocchi** non sono più verificati per nome nel validatore: li dichiara il catalogo
  della versione, e `ExerciseWorkspaceJson` traduce soltanto il documento Blockly nella forma che le
  regole verificano;
- lo **schema del checkpoint** è quello dichiarato dalla versione (`CheckpointSchemaVersion`), non un
  `1` valido per tutti i motori;
- `AssignedExerciseDto` e `ExerciseVersionDto` espongono il **tipo di esercizio**; la pagina
  dell'alunno spiega che un esercizio non si apre con la sua versione invece di mostrare una pagina
  vuota, e l'anteprima di una versione pubblicata usa **la sua** versione del motore (prima usava la
  costante corrente: difetto latente, corretto);
- **frontend**: `libraries/exercise-engine/engine-registry.ts` è la controparte della descrizione
  lato server; `participant-workspace` prende i blocchi e il cassetto dalla versione invece che dal
  catalogo cablato.

**Formato su disco invariato.** Il documento di §13.2 resta quello pubblicato — proprietà generali in
testa e, per `GridPath`, `scene`, `completion`, `feedback` — quindi l'impronta di contenuto di un
esercizio già pubblicato non cambia. Nessuna migrazione: le colonne `engine_version` e
`schema_version` erano già stringhe libere e il tipo vive solo nel JSON.

**Confine dichiarato** (tecnica §13.9): restano specifici del primo tipo i payload del protocollo del
player (`PlayerScene`, `ResumeRunRequest`, `RunFinishedEvent`), lo stato di runtime per l'attesa di una
risposta, il selettore di tipo nell'editor admin e il disegno della scena. Aggiungere una versione con
gli stessi blocchi di movimento non tocca nessuno di questi punti; un modello di esecuzione diverso
(variabili, condizione) li tocca tutti.

| Controllo eseguito dall'agente | Esito |
|---|---|
| `dotnet build .\Qwerty.Codi.slnx` | Uscita **0**, **0 warning e 0 errori** |
| `dotnet test .\Qwerty.Codi.slnx -c Release` al momento della scrittura | **1.261 test** (347 Domain, 906 Integration, 8 Protocol), **0 falliti** |
| Test nuovi sul registro | 11 casi in `ExerciseEngineRegistryTests`: descrizione, versione sconosciuta, contratto incrociato con il registro del client, campi dichiarati, intervallo e scelta, schema del checkpoint, composizione dei dati |
| Prova che blocca il formato | `ExecuteAsync_SaveExerciseDraft_IlDocumentoCanonicoRestaQuelloPubblicato`: il documento canonico è confrontato **per intero** con quello di §13.2, quindi una proprietà spostata fallisce invece di cambiare in silenzio l'impronta di un esercizio pubblicato |
| Test aggiornati perché fissavano il tipo unico | `ExerciseDefinitionTests`: rimosso il caso sullo schema futuro (ora è proprietà della versione), helper ricomposto sulla descrizione, parametro del completamento ora `data` |
| Angular pubblico | **113 test**, 0 falliti (uno nuovo: motore non conosciuto spiegato invece di pagina vuota) |
| Angular admin | **270 test**, 0 falliti (tre nuovi: `parseDefinition` rifiuta una versione del motore sconosciuta, un tipo incoerente con la sua versione, e dichiara il registro del server) |
| `npm run build` dalla root Web | **Uscita 0**: bundle delle due applicazioni in `dist/site` (admin sotto `/admin/`), con l'avviso di budget già noto sul bundle iniziale |
| `npx eslint` sui file toccati | Nessun problema |
| `npx prettier --check` sui file toccati | Tutti conformi |
| Contratti sul filo | `AssignedExerciseDto` e `ExerciseVersionDto` hanno **un campo in più** (`exerciseType`); nessun altro cambiamento |

**Da fare quando si aggiunge la prima versione nuova:** il selettore di tipo nell'editor admin, la
scena a unione nel protocollo del player, uno stato di runtime per l'attesa di una risposta, e
l'estensione della matrice di copertura con i comportamenti del nuovo motore.

## DTO raccolti in Application/Dto — 2026-10-09

**VALIDATO LOCALMENTE (2026-10-09).** Seconda parte della richiesta dell'utente del 9 ottobre 2026. La
prima parte (0.41) aveva tipizzato i sei payload di trasporto; l'utente ha poi chiesto esplicitamente
di **spostare tutti i DTO in `Application/Dto`, un file per tipo con il suffisso `Dto` nel nome**, pur
dopo la raccomandazione contraria: la decisione è sua e questa porzione la esegue.

**Che cosa è cambiato.** I **53** tipi `*Dto` di `Application` vivono ora in
`src/Qwerty.Codi.Application/Dto`, un file per tipo, namespace `Qwerty.Codi.Application.Dto`:

- **18 file** che contenevano solo DTO sono scomparsi (`Admin/*Dto.cs`, `Exercises/*Dto.cs`,
  `Lessons/*Dto.cs`, `Organization/*Dto.cs`, `Sessions/{SessionDto,SessionSummaryDto,CommandAcceptedDto,…}.cs`);
- **12 file misti** hanno tenuto comandi, query e servizi (`AttemptCommands.cs`, `SessionGridQueries.cs`,
  `ParticipantWorkQueries.cs`, `ParticipantQueries.cs`, …): il DTO è uscito, il resto è rimasto;
- `ParticipantRealtimeDtos.cs`, rimasto **senza DTO**, è diventato `ParticipantRealtimeContract.cs` con
  `ParticipantProtocol`, `ParticipantRealtimeEvents` e i record `*Arguments`: un file che si chiama
  «Dtos» senza contenere DTO sarebbe stato la prossima bugia da trovare;
- **107 file** hanno ricevuto `using Qwerty.Codi.Application.Dto;` (nessun rinomino a mano: il
  criterio è "cita un DTO fuori dai commenti");
- **15 using** ricavati dal compilatore per i DTO che citano tipi di dominio o di `Persistence`.

**Contratti invariati.** Il nome pubblico di comandi e query non cambia (47 e 28), nessuna migrazione,
nessun campo sul filo, nessuna modifica ai client: si è spostata la collocazione dei tipi, non la loro
forma. Il costo è dichiarato in §20.2 e nella 0.42: il DTO non sta più accanto al caso d'uso che lo
produce, quindi una modifica di contratto si legge in due file invece che in uno.

**Come è stato fatto.** Un estrattore (`TestResults/refactor-dto/extract-dtos.mjs`, non versionato) ha
riconosciuto le dichiarazioni per regex, ne ha calcolato il termine (`;` o corpo fra graffe, basi
comprese) e ha creato i file; un secondo script ha aggiunto gli using solo dove il nome compare nel
codice; il compilatore ha guidato i 15 using rimanenti. Due file con fine riga CRLF toccati
dall'estrazione sono stati normalizzati a LF come richiede `.editorconfig`.

| Controllo eseguito dall'agente | Esito |
|---|---|
| `dotnet build .\Qwerty.Codi.slnx -c Release` | Uscita **0**, **0 warning e 0 errori** |
| `dotnet test .\Qwerty.Codi.slnx -c Release` | **1.249 test** (336 Domain, 905 Integration, 8 Protocol), **0 falliti** |
| Copertura + `assert-dotnet-coverage.ps1` | Api **88,49%** · Application 97,51% · Domain 96,77% · Infrastructure 97,92%: sopra baseline (`TestResults/f5c2fece86354bb1a61e8d512c379e1b`) |
| Verifica della collocazione | **Nessun tipo `*Dto` fuori da `Application/Dto`**; tutti e 53 i file hanno il suffisso `Dto` nel nome |
| `dotnet format --verify-no-changes` | **584** errori `ENDOFLINE` su **7 file** (erano 672 su 9): due file CRLF sono stati normalizzati dal refactoring, gli altri restano dichiarati |
| Angular | Non toccato: 379 test come prima |

**Che cosa NON c'è, dichiarato.** I **7 file con CRLF** restano da normalizzare (una sola esecuzione di
`dotnet format whitespace`, non applicata perché estranea a questa porzione e perché riformatterebbe
file non toccati). Il refactoring **non è stato verificato nel browser**: sposta tipi, non comportamento,
e la suite di integrazione copre gli stessi percorsi di prima.

## Contratti di trasporto tipizzati — analisi dei DTO lato server — 2026-10-09

**VALIDATO LOCALMENTE (2026-10-09).** Su richiesta dell'utente del 9 ottobre 2026: «analizza se i DTO
sono sparsi per i progetti e in caso centralizzali».

**Analisi (la premessa non regge, e va detto).** I DTO **non** sono sparsi per i progetti:

| Progetto | Tipi `*Dto` | Che cosa contiene davvero |
|---|---:|---|
| `Qwerty.Codi.Domain` | **0** | Value object di dominio (`SchoolName`, `GridPosition`, `ExerciseLimits`, `SessionJoinCode`) |
| `Qwerty.Codi.Application` | **51** | 39 Sessions, 5 Exercises, 3 Organization, 2 Lessons, 2 Admin; **0 nomi duplicati**, **0 oggetti anonimi**, entità di persistenza POCO tenute distinte (§7.3, §10.4) |
| `Qwerty.Codi.Infrastructure` | **0** | Record di servizio (`MigrationScript`, `SqliteDatabase`, `DatabasePreparation`, `DatabaseReadiness`) |
| `Qwerty.Codi.Api` | **0** | Prima di questa porzione: 6 payload di trasporto costruiti come **oggetti anonimi** |

Quindi spostare i 51 DTO in una cartella unica sarebbe stato un diff di ~35 file senza alcun
cambiamento di contratto, e toglierebbe la vicinanza fra il caso d'uso e la sua risposta. La
centralizzazione utile era un'altra, e i DTO **mancanti** erano il problema:

1. `SessionEnded` (§11.3, funzionale §364): evento **documentato**, inviato con il nome in una stringa
   letterale e con un oggetto anonimo `{ Command, Grants, ExpiresAtUtc }`; nessun test lo copriva, e il
   client legge `command` da un payload che nessun tipo dichiarava.
2. `WorkspacePreviewAvailable` (§15.5): proiezione anonima di `WorkspacePreviewDto` costruita a mano.
3. Quattro corpi HTTP dedicati (§10.3): bootstrap antiforgery, ingresso, accesso e profilo del docente.

**Intervento.** `SessionEndedMessageDto` e `WorkspacePreviewNoticeDto` accanto ai loro pari in
`Application/Sessions`; il nome dell'evento in `ParticipantRealtimeEvents` come tutti gli altri;
i quattro corpi in `Api/Contracts/TransportResponses.cs` come record `*Response`. **Il contenuto sul
filo non cambia**: stessi campi, stesso camelCase (`CodiJson`), e i test HTTP che elencano i campi
attesi continuano a valere come verifica.

| Controllo eseguito dall'agente | Esito |
|---|---|
| `dotnet build .\Qwerty.Codi.slnx -c Release` | Uscita **0**, **0 warning e 0 errori** |
| `dotnet test .\Qwerty.Codi.slnx -c Release` | **1.249 test** (336 Domain, 905 Integration, 8 Protocol), **0 falliti** — un caso nuovo sulla chiusura, uno aggiornato sull'anteprima |
| Copertura + `assert-dotnet-coverage.ps1` | Api **88,26%** (era 87,30%) · Application 97,51% · Domain 96,77% · Infrastructure 97,92%: sopra baseline (`TestResults/eea2d5b0c81b44579c94e2b87834d473`) |
| `test-coverage-gate.ps1` | **4/4** regressioni superate |
| `dotnet format --verify-no-changes` | Restano **672 errori `ENDOFLINE`** sui **9 file** con fine riga CRLF già dichiarati (porzione T8): stesso insieme di prima, **nessun file nuovo**. Tre di quei file sono toccati da questa porzione, quindi il difetto è ora anche nel diff |
| Angular | Non toccato: 379 test come nella porzione precedente |

**Che cosa NON c'è, dichiarato.** Il «catalogo DTO SignalR con compatibilità e campioni di messaggi»
di §21.2 resta **parziale**: i messaggi hanno un tipo, manca il documento di catalogo. I 9 file con
CRLF restano da normalizzare (una sola esecuzione di `dotnet format whitespace`, non applicata perché
estranea a questa porzione). Nessuna verifica nel browser: non cambia nulla di osservabile dall'utente.

## Percorso dell'alunno: scuola, classe, lezione, esercizio — 2026-10-09

**VALIDATO LOCALMENTE E VERIFICATO NEL BROWSER (2026-10-09).** Su richiesta dell'utente del 9 ottobre
2026: l'alunno entra con il codice di otto cifre e non sa **dove** è finito; la pagina mostrava soltanto
il suo nome.

- `ParticipantContextDto` (Application) guadagna **`SchoolName`, `ClassroomName`, `LessonTitle`**;
  `GetParticipantContextHandler` li legge da `ILessonRepository`, `IClassroomRepository` e
  `ISchoolRepository` partendo dalla sessione della credenziale.
- Sono **contesto, non permesso**: se una delle tre righe non c'è, il nome si omette e la lettura
  riesce lo stesso — l'alunno deve poter lavorare anche con un dato di contorno incoerente. Le righe
  essenziali (partecipazione, stato, sessione) restano obbligatorie e continuano a dare `404`.
- Nessun nome nuovo nel catalogo (restano **47 comandi e 28 query**), nessuna migrazione: lo schema
  resta alla versione 10.
- La pagina dell'alunno mostra una **lista ordinata** «Dove sei» — non collegamenti, perché la vista la
  decide il server — e il **nome sulla riga sotto**. L'esercizio entra nel percorso solo dopo
  `AssignExercise`; finché il contesto non è arrivato la testata mostra il ripiego «La mia lezione».
- `libraries/api` aggiorna `ParticipantContext`; `docs/API-ENDPOINTS.md` documenta i tre campi.

| Controllo eseguito dall'agente | Esito |
|---|---|
| `dotnet build .\Qwerty.Codi.slnx -c Release` | Uscita **0**, **0 warning e 0 errori** |
| `dotnet test .\Qwerty.Codi.slnx -c Release` | **1.248 test** (336 Domain, 904 Integration, 8 Protocol), **0 falliti** — due casi di integrazione nuovi |
| Copertura + `assert-dotnet-coverage.ps1` | Api 87,30% · Application 97,50% · Domain 96,77% · Infrastructure 97,92%: sopra baseline (`TestResults/dfbb5679ada1486ab1766c20c828774c`) |
| `test-coverage-gate.ps1` | **4/4** regressioni superate |
| `npm run test:admin -- --watch=false` | **267 test** (30 file), 0 falliti |
| `npm run test:frontend -- --watch=false` | **112 test** (11 file), 0 falliti — erano 109: tre casi nuovi sul percorso |
| `npm run lint`, `npm run format:check`, `npm run build` | Uscita **0** |

**Verifica nel browser (2026-10-09).** API in Release su 5310 con dati temporanei, app public su 4301,
Edge headless pilotato dal DevTools Protocol; l'ambiente di sviluppo dell'utente (5200, 4200, 4300) è
rimasto **intoccato**. Schermate in `TestResults/collaudo-alunno/shots`:

- appena entrato, prima che il docente assegni qualcosa, il percorso dice «Scuola Collaudo Alunno … /
  Terza A / Prima lezione» e sotto c'è «Luca»;
- dopo l'assegnazione dal vivo il percorso guadagna il quarto livello, «Raggiungi la stella»;
- **riquadri, prova e impaginazione restano quelli verificati**: «Puoi lavorare!», prova che porta il
  personaggio sul traguardo, stato osservato dal docente invariato, nessun errore in console.

**Difetto trovato dal pilota.** La prima cattura leggeva la testata **prima** che arrivasse il contesto:
si vedeva il ripiego «La mia lezione» e nessun percorso. Non è un difetto del prodotto — il contesto
arriva dal server in decine di millisecondi e il ripiego è la scelta voluta — ma il pilota ora attende
il nome, così la prova misura la pagina caricata e non quella a metà.

## Impaginazione della pagina dell'alunno — 2026-10-09

**VALIDATO LOCALMENTE E VERIFICATO NEL BROWSER (2026-10-09).** Su richiesta dell'utente del 9 ottobre
2026: il progetto e il programma dell'alunno erano uno sotto l'altro in una colonna da 40 rem
(`main` globale) e da 48 rem (`.session`), quindi il bambino non vedeva i blocchi e la scena insieme.

- La regola globale `main` della vista alunni passa a **tutta larghezza** (`projects/public/src/styles.css`);
  `.session` non impone più la propria colonna.
- Nuova area `.work-area` (`session.css`): griglia `repeat(auto-fit, minmax(26rem, 1fr))` con il
  **progetto prima** e il programma dopo, quindi **progetto a sinistra e scena a destra** sugli schermi
  larghi, una colonna sola sotto i 26 rem per colonna, e **riga piena quando il pannello è uno solo** —
  in attesa il progetto non esiste e non resta una colonna vuota.
- L'ingresso (`/join`) conserva la propria colonna stretta con una regola di classe: è un modulo di
  poche righe e a tutta larghezza sarebbe illeggibile.
- I comandi della prova restano con la scena, non con l'editor.

| Controllo eseguito dall'agente | Esito |
|---|---|
| `npm run test:frontend -- --watch=false` | **109 test** (11 file), 0 falliti — erano 107: due casi nuovi sulla composizione dell'area di lavoro e sul pannello singolo |
| `npm run lint`, `npm run format:check` | Uscita **0** |
| `npm run build` | Uscita **0**; resta il warning preesistente di budget del bundle admin. Il chunk `session` passa da 817,40 kB a 817,65 kB |
| Suite .NET | Non rieseguita: la porzione tocca solo CSS e template della vista alunni, nessun file .NET. I 1.246 test dell'ultima esecuzione restano validi per il codice invariato |

**Verifica nel browser (2026-10-09).** Stesso impianto del collaudo precedente: API in Release su 5310
con dati temporanei, app public su 4301, Edge headless pilotato dal DevTools Protocol. L'ambiente di
sviluppo dell'utente (5200, 4200, 4300) è rimasto **intoccato**. Schermate in
`TestResults/collaudo-alunno/shots`:

- a 1280 px il progetto (editor Blockly con «quando si avvia / ripeti 4 volte / fai un passo») sta a
  sinistra e il programma (stato, griglia, «Prova / Un passo / Da capo») a destra, sulla stessa riga, e
  la pagina usa tutta la larghezza;
- durante la prova si vedono insieme i blocchi e il personaggio che si muove: il riquadro diventa blu
  «Stai provando il tuo programma», la riga dice «Stai provando: 2 passi» e il pulsante diventa
  «Ferma»;
- a fine corsa il personaggio è sul traguardo con «Bravo, hai raggiunto la stella!»;
- in attesa, con il solo pannello del programma, la riga non lascia una colonna vuota;
- lo stato osservato dal docente resta identico prima e dopo la prova; nessun errore in console.

**Che cosa NON c'è, dichiarato.** La verifica è a una sola risoluzione (1280 px) e in un browser
headless: la resa su monitor di aula, su finestre strette e con dodici postazioni resta del laboratorio
su 12 PC (T9). L'aspetto della pagina resta provvisorio per scelta.

## Esperienza dell'alunno: stato a icone colorate e prova del programma — 2026-10-09

**VALIDATO LOCALMENTE E COLLAUDATO NEL BROWSER (2026-10-09).** Su feedback dell'utente del 9 ottobre
2026 la pagina dell'alunno non lasciava capire che cosa stesse succedendo — stato, controlli, servizio
— e non permetteva di provare il programma a blocchi, che pure l'admin sa simulare.

**Stato a colpo d'occhio.** Tre riquadri in cima alla pagina, ognuno con **icona, colore e parole**
(`session/status-badge.ts|css`, `session/status-badges.ts`): collegamento (§11.6), che cosa può fare
adesso (§6.2), che cosa fa il suo programma (§13.6, §13.7). La mappa è una **funzione pura**, quindi le
precedenze si verificano senza montare la pagina: una lezione conclusa non torna indietro, il recupero
in corso spiega perché il lavoro è fermo, la pausa del maestro viene prima dei controlli chiusi. Il
colore non è mai l'unica informazione: ogni riquadro porta icona e testo, e i toni sono cinque
(ok, work, wait, stop, idle). Il pannello con le parole del contratto resta, ma dentro «Informazioni
per il docente».

**Prova locale del programma.** `core/trial-run.ts`: la stessa corsa isolata di §13.5 —
`parseGridPathDefinition`, `compileGridPathProgram`, `createGridPathRun` — vive **nella pagina**, non
nel worker, e disegna il personaggio sulla scena con i messaggi della definizione. Comandi: **Prova**
(scorre i passi ogni 300 ms come il player), **Un passo**, **Da capo**, **Ferma** mentre scorre. Il
confine è dichiarato e verificato: la prova non produce checkpoint, non tocca il progetto salvato, non
aggiorna la telemetria e **non compare nella regia** — il maestro vede solo la corsa che ha comandato —
ed è ammessa soltanto quando i controlli sono attivi. Si ferma da sé quando il docente comanda
un'esecuzione o quando i controlli si chiudono, e si azzera quando il progetto cambia, perché appartiene
al progetto con cui è cominciata. Un progetto che non si compila si spiega con il messaggio del motore.

**Difetti trovati dai test.** La posizione iniziale della prova prendeva l'intera `start` della scena
(direzione compresa) invece della sola cella; e dopo un esito «Un passo» non faceva più nulla — per un
bambino un pulsante che non risponde. Ora la posizione è una cella e un passo dopo l'esito fa ripartire
la prova dalla partenza.

| Controllo eseguito dall'agente | Esito |
|---|---|
| `dotnet build .\Qwerty.Codi.slnx -c Release` | Uscita **0**, **0 warning e 0 errori** |
| `dotnet test .\Qwerty.Codi.slnx -c Release` | **1.246 test** (336 Domain, 902 Integration, 8 Protocol), **0 falliti** |
| Copertura (`XPlat Code Coverage` + `assert-dotnet-coverage.ps1`) | Api 87,53% · Application 97,50% · Domain 96,77% · Infrastructure 97,92%: sopra baseline (`TestResults/f5aae1741f3e437791225b0b01d5f672`) |
| `test-coverage-gate.ps1` | **4/4** regressioni superate |
| `npm run test:admin -- --watch=false` | **267 test** (30 file), 0 falliti |
| `npm run test:frontend -- --watch=false` | **107 test** (11 file), 0 falliti — erano 75: 20 della mappa dei riquadri, 7 della prova, 5 della pagina. Dalla porzione di impaginazione dello stesso giorno sono **109** |
| `npm run lint`, `npm run format:check` | Uscita **0** |
| `npm run build` | Uscita **0**, entrambe le app compilano; resta il warning **preesistente** di budget del bundle admin (554,95 kB contro 500 kB). Il chunk `session` passa da 797,79 kB a 817,40 kB |

**Collaudo nel browser, eseguito il 9 ottobre 2026.** API in Release su 5310 con dati temporanei, app
public su 4301 con un proxy di collaudo, Edge headless pilotato dal DevTools Protocol
(`TestResults/collaudo-alunno/pilot.mjs`). L'ambiente di sviluppo dell'utente (API 5200, app 4200 e
4300) è rimasto **intoccato**: il collaudo ha usato porte proprie e un database proprio.

Esiti osservati, tutti con schermata in `TestResults/collaudo-alunno/shots`:

- prima dell'assegnazione i riquadri dicono «Collegato alla lezione» (verde), «Aspetta: il maestro
  prepara l'attività» (ambra), «Programma pronto» (grigio);
- il pilota assegna e abilita l'attività **dal percorso amministrativo reale** e la pagina reagisce dal
  vivo: il secondo riquadro diventa verde «Puoi lavorare!», compaiono la consegna e la griglia con il
  personaggio e la stella;
- premuto **Prova**, il terzo riquadro diventa blu «Stai provando il tuo programma», la riga dice
  «Stai provando: 2 passi», il personaggio è passato dalla partenza alla terza colonna e i pulsanti
  mostrano «Ferma»;
- a fine corsa il personaggio è sul traguardo e compare l'esito della definizione: «Bravo, hai
  raggiunto la stella!»;
- **lo stato osservato dal docente è identico prima e dopo la prova** (vista, controlli, generazione,
  direttiva, presenza): la prova non produce esecuzioni comandate;
- nessun errore in console.

**Che cosa NON c'è, dichiarato.** Il collaudo ha usato **un** alunno in un browser headless: la
leggibilità dei riquadri a distanza, la prova con dodici bambini e i tempi reali di digitazione
restano del laboratorio su 12 PC (T9). L'aspetto della pagina resta provvisorio per scelta.

## Ingresso in aula con il codice di lezione — 2026-10-09

**VALIDATO LOCALMENTE E COLLAUDATO NEL BROWSER (2026-10-09).** Su decisione dell'utente del 9 ottobre
2026 l'ingresso ordinario dell'alunno non è più la scelta di scuola e classe: il docente avvia la
lezione, riceve un **codice numerico di otto cifre** e lo detta a voce; l'alunno digita il codice e il
proprio nome. Le tre risposte dell'utente hanno fissato il perimetro: codice **e** nome, percorso
vecchio **nascosto** ma non rimosso, codice **valido anche in pausa**.

**Dominio e schema.** `SessionJoinCode` (otto cifre, prima cifra diversa da zero, `Create` con
generatore crittografico, `Restore` validante) e `LessonSession.JoinCode`: nullo in `Lobby`, assegnato
da `Start`, conservato da `Pause`, `Resume` ed `End`. Due invarianti: `Start` rifiuta un codice vuoto,
`Restore` rifiuta un codice su una sessione in attesa e ammette un codice assente su una sessione
avviata — è lo stato delle sessioni create prima della `0010`, che restano raggiungibili dalla catena.
Migrazione `0010_session_join_code.sql`: colonna `join_code` con CHECK a otto cifre e prima cifra
diversa da zero, indice unico (NULL ammesso più volte), schema 9 → **10**. La prima stesura del CHECK
ammetteva lo zero iniziale: il test di schema l'ha colto e il vincolo è stato allineato al dominio,
perché una riga fuori formato renderebbe illeggibile l'intera sessione e non solo il suo codice.

**Regia.** `StartSession` genera il codice verificandone l'unicità e rigenerandolo se occupato;
`SessionDto`, `SessionControlAcceptedDto` e `SessionSummaryDto` lo espongono; la regia lo mostra in
cifre grandi, con il pulsante di copia, **solo mentre la lezione è in play**, e non in attesa né dopo
lo Stop.

**Ingresso.** `JoinSession` accetta **due forme alternative** — `joinCode` + nome, oppure scuola,
classe, sessione + nome: dichiararne due o nessuna è `400 ValidationFailed`, e una catena parziale
pure. Il codice si risolve **fuori** dalla transazione (il coordinatore si acquisisce per sessione) e
la ricerca non filtra lo stato, così un ingresso già registrato si recupera dal registro di
idempotenza anche dopo la fine della lezione, come per la catena. Codice inesistente o malformato →
`404 InvalidJoinCode` uniforme; codice valido su lezione non in play → il rifiuto dello stato
(`409 SessionStateConflict` o `409 AdmissionsClosed`). L'impronta di idempotenza segue la forma
dichiarata, quindi cambiare codice in interfaccia è un tentativo nuovo. `IJoinCodeAttemptRegistry`
(in memoria) conta i tentativi per impronta dello scope di ammissione e per impronta dell'indirizzo di
rete: oltre dieci in un minuto `429 TooManyJoinAttempts`, e un ingresso riuscito azzera il conteggio;
il registro non conserva il codice digitato. **Un difetto trovato dai test**: il codice inesistente
non alimentava il conteggio, perché il registro si aggiornava solo dopo la risoluzione; ora si conta
anche lì, ed è il caso che i test coprono.

**Home dell'alunno.** Nuova lettura anonima `GET /api/v1/public/in-play` (`IsLessonInPlayQuery`, fuori
dal catalogo del dispatcher) con il solo booleano; `/join` la interroga ogni 3 secondi, dichiara se la
lezione è in corso e chiede codice e nome, tenendo solo le cifre e non più di otto; la scelta di
scuola e classe sta dietro «Non ho il codice» e carica le scuole solo quando serve. Il nome si
ricorda in `localStorage` — non è una credenziale — e la chiave di idempotenza cambia quando cambia il
contenuto, così un codice diverso non eredita la chiave del tentativo precedente.

**Collaudo nel browser, eseguito il 9 ottobre 2026.** Due app reali e un browser vero, pilotato dal
DevTools Protocol (`TestResults/collaudo-codice/pilot.mjs`, `pilot2.mjs`, `pilot3.mjs`): API in
Release su HTTPS 5200 con `Codi__DataDirectory` temporanea (`TestResults/collaudo-codice/data`),
admin 4200 e public 4300 con il proxy di sviluppo, Edge headless con certificati di sviluppo ignorati
e un **contesto browser separato per ogni alunno**, come un altro PC. Dati preparati dal percorso
amministrativo reale (`seed.ps1`): scuola, classe, corso, lezione, sessione avviata; il codice della
lezione è **51031539**.

Esiti osservati, tutti con schermata in `TestResults/collaudo-codice/shots`:

- la home dell'alunno **sente** la lezione e scrive «La lezione è iniziata! Chiedi il codice al
  maestro e scrivilo qui», con il campo del codice in cifre grandi e la tastiera numerica;
- un codice sbagliato (51031538) riceve dal server «Il codice non è valido: controlla le otto cifre e
  chiedile di nuovo al maestro» e la pagina **resta** sull'ingresso;
- il codice giusto con il nome «Luca» porta a `/lezione`, che mostra «Luca · Collegato alla lezione ·
  Aspetta: l'insegnante sta preparando l'attività · Stato: pronto»;
- la **regia** mostra «Codice da dettare agli alunni **5 1 0 3 1 5 3 9**» con il pulsante «Copia il
  codice» e il conteggio «1 alunno entrato»;
- **in pausa** il codice resta visibile e gli ingressi restano aperti; un **secondo alunno** («Anna»),
  da un contesto browser separato, entra con lo stesso codice e vede l'attesa;
- il ripiego **«Non ho il codice»** scopre la scelta di scuola e classe, carica le scuole «Scuola
  Collaudo Codice 012426» e offre «Entra con scuola e classe»;
- dopo lo **Stop** la regia non mostra più il codice né il pulsante di copia; la home dichiara
  «Nessuna lezione in corso: aspetta che il maestro inizi la lezione» e il vecchio codice riceve «La
  lezione è terminata.» restando sull'ingresso;
- nessun errore in console; le richieste osservate sono un `bootstrap`, i sondaggi
  `GET /api/v1/public/in-play` e due `POST /api/v1/public/join` (tentativo errato e ingresso).

Il collaudo ha anche confermato sul campo l'**aggiornamento dello schema** da 9 a 10: il database
temporaneo era stato creato dalla build precedente e l'avvio in Release lo ha portato alla `0010`
senza ricreazione, sulle righe esistenti.

**Un'osservazione non bloccante.** Se il primo sondaggio di presenza fallisce — API non raggiungibile
— la home resta su «Un momento…» senza dire altro: la sorveglianza tace di proposito, perché non deve
riempirsi di avvisi a ogni sfarfallio, ma al primo tentativo un messaggio esplicito sarebbe più
onesto. Fuori dal perimetro di questa porzione.

| Controllo eseguito dall'agente | Esito |
|---|---|
| `dotnet build .\Qwerty.Codi.slnx -c Release` | Uscita **0**, **0 warning e 0 errori** |
| `dotnet test .\Qwerty.Codi.slnx -c Release` | **1.246 test** (336 Domain, 902 Integration, 8 Protocol), **0 falliti** — erano 1.210 |
| Copertura (`XPlat Code Coverage` + `assert-dotnet-coverage.ps1`) | Api 88,45% · Application 97,50% · Domain 96,77% · Infrastructure 97,92%: sopra baseline (`TestResults/48936a614b784f768c82853c7d562952`). Il primo giro era **rosso** su Infrastructure (97,69% → 97,41%) per il registro dei tentativi: coperto con test dedicati, non abbassando la soglia |
| `test-coverage-gate.ps1` | **4/4** regressioni superate |
| `npm run test:admin -- --watch=false` | **267 test** (30 file), 0 falliti — erano 264 |
| `npm run test:frontend -- --watch=false` | **75 test** (9 file), 0 falliti — erano 70 |
| `npm run lint`, `npm run format:check` | Uscita **0** |
| `npm run build` | Uscita **0**, entrambe le app compilano; resta il warning **preesistente** di budget del bundle admin (554,95 kB contro 500 kB); il chunk `join` passa da 4,91 kB a 8,41 kB |
| `dotnet format whitespace --verify-no-changes` | **Uscita 2: resta rosso per gli stessi nove file T8** con fine riga CRLF locale segnalati il 2026-10-09; nessun file di questa porzione è coinvolto |
| Test nuovi .NET | Codice (dominio), invarianti della sessione, vincoli di schema e aggiornamento `0009 → 0010` su database con righe; avvio con codice e letture della regia; ingresso con codice su lezione attiva e in pausa, codice errato e malformato, lezione terminata, ingressi chiusi, ripetizione, ripetizione dopo la fine, forme alternative, conteggio e azzeramento; presenza della lezione in play; registro dei tentativi; ingresso HTTP con codice, codice errato e lettura `/in-play` anonima |
| Test nuovi Angular | Home dell'alunno: presenza che cambia l'invito, codice e nome, solo cifre e non più di otto, nome richiesto, chiave nuova quando cambia il codice, ripiego nascosto, nome ricordato; regia: codice mostrato solo in play |

**Che cosa NON c'è, dichiarato.** Il collaudo ha usato **due** alunni simulati in un browser headless,
non dodici PC reali con bambini: la dettatura a voce, i tempi e gli errori veri restano del laboratorio
su 12 PC (T9). Non sono stati esercitati nel browser il **rallentamento** dei tentativi (dieci codici
errati di fila) né il **ripiego HTTP** dell'anteprima. Nessun nome nuovo nel catalogo: restano **47
comandi e 28 query**, e `SessionDiscoveryTests` lo conferma. La specifica funzionale passa a **0.7**
(cambia il requisito di ingresso) e la tecnica a **0.37**.

## Allineamento documentale allo stato del sorgente — 2026-10-09

**VALIDATO LOCALMENTE sui documenti; un gate di formattazione è rosso e resta aperto.** Su richiesta
dell'utente è stata rifatta la scansione del sorgente (revisione `33a5c7a`, albero di lavoro pulito)
per confrontarla con i documenti e allineare quelli divergenti. Nessun file di codice, schema, dato o
dipendenza è stato modificato: gli interventi riguardano solo documenti.

**Divergenze trovate e corrette.**
- `README.md` descriveva lo stato di settembre: public come «benvenuto provvisorio», aula live e
  autore/player «da sviluppare», schema alla sola migrazione `0001`, ammissione «prevista in T3».
  Ora riporta le sezioni reali dell'admin, le route delle due app, lo schema 9 e il ticket di
  ammissione emesso dal bootstrap pubblico.
- `docs/RIPRESA-SVILUPPO.md`: intestazione ferma al 5 ottobre; aggiunta la porzione del 7 ottobre
  (pagina dei partecipanti) e l'aggiornamento al sorgente.
- `docs/API-ENDPOINTS.md`: l'anteprima dei blocchi era dichiarata «da collaudare nel browser» e le
  esecuzioni precedenti «DA VALIDARE»; ora registra il collaudo del 2026-10-07 e i gate del 2026-10-09.
- `docs/PERSISTENZA.md`: «unico script `0001_schema`», «diciannove repository» e migrazioni «fino a
  `0007`»; ora schema 9, ventidue repository e script fino a `0009_attempt_finalization`.
- `docs/T4-REGIA-AFFIDABILE.md`: dichiarava T5 e T7 «da implementare»; ora li colloca come stato alla
  data della validazione, con il rimando alle porzioni successive.
- `docs/TEST-COVERAGE-MATRIX.md`: conteggi della raccolta T5 (1.170 .NET e 295 Angular) sostituiti con
  quelli correnti; aggiunte le righe della pagina dei partecipanti; rimosse due righe «non coperto»
  ormai false (uscita volontaria e percorsi degli hub nella selezione dell'area).
- `src/Qwerty.Codi.Web/README.md`: elenco delle route admin incompleto, menu del guscio fermo a
  tre sezioni, frontend alunni descritto come «ingresso provvisorio» senza `/lezione`, libreria
  esercizi descritta come sola anagrafica.
- `SPECIFICA_TECNICA_PIATTAFORMA_DIDATTICA.md` → **0.36**: §1.1, §10.3, §14.1 e §21.1 allineati allo
  stato reale (rotte `/`, `/join`, `/lezione`, `/admin/exercises/:exerciseId/editor`,
  `/admin/local-recovery`; salvataggi, anteprima e finalizzazione dichiarati implementati); §24
  registra la revisione e integra la voce 0.35 con l'esito dei gate e del collaudo.
- `SPECIFICHE_PIATTAFORMA_DIDATTICA.md`: il paragrafo di stato dichiarava aula live, ingresso alunni e
  autore/player «da realizzare»; la versione resta 0.6 perché nessun requisito cambia.

| Controllo eseguito dall'agente | Esito |
|---|---|
| `dotnet build .\Qwerty.Codi.slnx -c Release` | Uscita **0**, **0 warning e 0 errori** |
| `dotnet test .\Qwerty.Codi.slnx -c Release` | **1.181 test** (316 Domain, 857 Integration, 8 Protocol), **0 falliti** |
| Copertura (`XPlat Code Coverage` + `assert-dotnet-coverage.ps1`) | Api 87,53% · Application 97,51% · Domain 96,86% · Infrastructure 97,84%: sopra baseline (`TestResults/d481876421e547e9a09ecbed41d36b00`) |
| `test-coverage-gate.ps1` | **4/4** regressioni superate |
| `npm run test:admin -- --watch=false` | **264 test** (30 file), 0 falliti |
| `npm run test:frontend -- --watch=false` | **70 test** (9 file), 0 falliti |
| `npm run lint`, `npm run format:check` | Uscita **0** |
| `npm run build` | Uscita **0**, entrambe le app compilano; resta il warning **preesistente** di budget del bundle admin (554,95 kB contro 500 kB) |
| `dotnet format whitespace --verify-no-changes --no-restore` | **Uscita 2 — GATE ROSSO.** Nove file della porzione T8 hanno fine riga CRLF nell'albero di lavoro contro `end_of_line = lf` di `.editorconfig`: `WorkspacePreviewNotifier.cs`, `IParticipantPreviewCache.cs`, `ParticipantPreviewCommands.cs`, `ParticipantPreviewQueries.cs`, `ParticipantPreviewHandlers.cs`, `TeacherRealtimeEvents.cs`, `ParticipantPreviewCache.cs`, `WorkspacePreviewNotifierTests.cs`, `ParticipantPreviewTests.cs` (tutti scritti il 2026-10-05) |

I cataloghi restano **47 comandi e 28 query** (confermati dal test `SessionDiscoveryTests`); la
ripartizione per area calcolata dai record e dalle policy è admin 37 comandi e 23 query, public 9
comandi e 5 query. Lo schema è alla versione 9 con nove script.

**Punto aperto dichiarato.** Il gate di formattazione .NET non è verde in questo albero di lavoro:
i nove file elencati hanno CRLF locale, mentre i blob nel repository sono LF (`git ls-files --eol`
riporta `i/lf w/crlf`), quindi un clone nuovo non eredita il difetto e `git status` resta pulito.
La porzione T8 era stata validata senza eseguire `dotnet format`, ed è la ragione per cui il difetto
non era emerso. La correzione è di soli fine riga (nessuna modifica di contenuto) e non è stata
applicata in questa porzione, che ha perimetro documentale: si esegue riscrivendo i nove file in LF e
ricontrollando `dotnet format whitespace --verify-no-changes`.

## Regia admin: i partecipanti diventano card in una pagina dedicata — 2026-10-07

**VALIDATO LOCALMENTE E COLLAUDATO NEL BROWSER (2026-10-07).** Su richiesta dell'utente, la vista d'insieme della classe non è più una
griglia di righe nella regia: è una pagina propria con una card per alunno, per avere il colpo d'occhio
su quello che succede. La regia conserva contesto, stato, comandi di classe e il collegamento alla pagina.

**Codice presente** (`src/Qwerty.Codi.Web/projects/admin`):
- `features/admin/sessions/session-regia-service.ts`: `SessionRegiaService`, unico punto di aggiornamento
dello stato della sessione (§14.1) — lettura, una sola sottoscrizione all'hub condivisa dalle due pagine
con un solo `observe()`, comandi con `expectedSessionRevision` e rilettura del server prima di
riabilitare i controlli (§10.7, §12.1).
- `features/admin/sessions/preview-lines.ts`: `PreviewLine` e `describeWorkspaceBlocks()` estratti da
`session-detail.ts`, usati da entrambe le pagine.
- `features/admin/sessions/participant-card.ts|html|css`: card con nome, postazione, presenza, stato
desiderato, lavoro corrente e stato confermato; azioni come pulsanti icona con tooltip ed etichetta
accessibile; dettaglio e blocchi solo per la card selezionata (§14.3, §15.5).
- `features/admin/sessions/session-participants.ts|html|css`: pagina con la lista delle card e le azioni
del singolo alunno.
- `app.routes.ts`: rotta `sessions/:sessionId/participants` sotto il guscio.
- `session-detail.ts|html|css`: griglia e azioni di riga tolte; restano stato, comandi di classe, dialogo
di conferma e riepilogo degli alunni con il collegamento alla pagina. Gli stili rimasti senza uso
(azioni di riga, dettaglio, anteprima dei blocchi) sono rimossi dal foglio della regia.

**Prove aggiunte.**
- `session-detail.spec.ts`: la griglia condivisa e i cinque usi di `rowButton` non avevano più bersaglio
  e sono sostituiti dal riepilogo degli alunni e dal collegamento alla pagina dei partecipanti; i test
  delle azioni del singolo alunno, dell'azzeramento e dell'anteprima si sono spostati dove ora vivono.
- `session-participants.spec.ts` (nuovo): una card per alunno con presenza, stato desiderato e lavoro
  corrente; stato vuoto; dettaglio e anteprima dei blocchi per il **solo** alunno aperto, riletti
  all'annuncio dell'hub; azione del singolo con `expectedSessionRevision` e rilettura; rifiuto seguito
  dalla rilettura; azzeramento con conferma e tentativo dichiarato; stato condiviso fra regia e
  partecipanti. Gli `effect()` del servizio si verificano con `flush()` e `whenStable()`, perché la
  change detection è senza `zone.js`.
- `participant-card.spec.ts` (nuovo): resa dei campi e dello stato osservato, esclusione dichiarata,
  azioni come pulsanti con etichetta accessibile e disabilitate quando non ammesse, blocco durante un
  comando, evento emesso, dettaglio e blocchi solo se selezionata, assenza di anteprima distinta da un
  problema di lettura.
- `session-participants.ts`: rimosso `NgIcon` dagli import della pagina (non usato nel suo template),
  che la build segnalava con `NG8113`.

| Controllo eseguito dall'agente | Esito |
|---|---|
| `npm run lint` | Uscita **0** |
| `npm run format:check` | Uscita **0**, tutti i file conformi a Prettier |
| `npm run test:admin -- --watch=false` | **264 test** passati (30 file), 0 falliti — erano 252; il collaudo ha aggiunto una regressione |
| `npm run test:frontend -- --watch=false` | **70 test** passati (9 file), 0 falliti — nessuna modifica a `projects/public` o `libraries` |
| `npm run build` | Uscita **0**, entrambe le app compilano. Resta il solo warning **preesistente** di budget del bundle admin (554,95 kB contro 500 kB) |
| `npm run audit:runtime` | Uscita **1**: 1 vulnerabilità high `source-map-js` 1.0.0–1.2.1 (GHSA-68fv-2mgg-jv7q), transitiva, non introdotta da questa porzione |

Nessuna modifica al codice .NET: nomi di richiesta e policy restano quelli dei cataloghi §10.4–§10.5.
La specifica tecnica passa a **0.35** (§14.1, §14.3, registro §24); la funzionale 0.6 resta invariata,
perché il requisito §6.2 — griglia di riquadri con selezione individuale — è soddisfatto dalla pagina
delle card.

**Collaudo nel browser, eseguito il 7 ottobre 2026.** API su HTTPS 5310 con `Codi__DataDirectory`
temporanea (`TestResults/participant-card-collaudo`), app admin 4310 e public 4301 con il proxy del
collaudo T7 verso 5310 e `Codi:Realtime:AllowedOrigins` estese a 4310/4301. Dati preparati con
`TestResults/t7-collaudo.ps1 -Phase seed` (scuola, classe, corso, lezione, esercizio pubblicato con
progetto risolutivo, sessione avviata); un alunno è entrato da `/join` come «Luca Collaudo».

Esiti osservati:
- stato vuoto dichiarato prima dell'ingresso, card comparsa **da sola** all'ingresso (aggiornamento live);
- la card mostra nome, «postazione non assegnata · Connesso», stato desiderato (`Waiting`, poi `Exercise
  · controlli attivi · direttiva in attesa`), lavoro (`Nessun esercizio assegnato`, poi `Tentativo 1 ·
  Ready · progetto rev. 0`) e stato confermato (`Ready`, `Paused`, `Completed`);
- le azioni partono davvero: `EnableActivity: 1 consegne registrate.`, `RunProgram: 1 consegne
  registrate.`, `ResetWork: 1 consegne registrate.`, con rilettura e abilitazioni coerenti (Avvia attivo
  dopo l'abilitazione, di nuovo spento dopo l'azzeramento);
- la conferma dell'azzeramento dichiara il tentativo; «Annulla» chiude il dialogo **senza** inviare
  comandi; la conferma archivia e porta la card a `Tentativo 2`;
- **l'anteprima dei blocchi di §15.5 arriva dal browser dell'alunno**: `rev. 2` (poi `rev. 4`),
  «coerente col progetto salvato», blocchi `quando si avvia` + 3× `fai un passo`; dopo l'azzeramento
  torna a «Nessuna anteprima ricevuta», perché la nuova esercitazione non ne ha ancora una;
- la regia riepiloga gli alunni e il collegamento apre la pagina dei partecipanti.

**Un difetto reale, trovato solo dal collaudo.** Dopo un comando che cambia il tentativo (l'azzeramento),
la **card** passava a `Tentativo 2` ma il **dettaglio aperto** restava su `Tentativi 1`, e si correggeva
solo chiudendolo e riaprendolo: griglia e dettaglio raccontavano due stati diversi, contro il principio
dichiarato nel servizio. `SessionRegiaService.refresh()` ora rilegge anche il dettaglio aperto (e la sua
anteprima) insieme ai partecipanti; la lettura del dettaglio è estratta da `toggleDetail` in un solo
metodo. Verificato di nuovo nel browser: dopo l'azzeramento il dettaglio passa a `Tentativi 3` **senza**
riaprire la card. Regressione in `session-participants.spec.ts` («rilegge il dettaglio aperto dopo un
comando…»).

**Osservazioni non bloccanti, fuori dal perimetro.**
- La console admin riceve ogni secondo `ParticipantPresenceChanged`, `ParticipantStateUpdated`,
  `CommandStatusChanged` e `ParticipantJoined`, per i quali `TeacherRealtime` non registra un handler:
  SignalR li segnala come «No client method … found». Non è una regressione di questa porzione
  (`teacher-realtime.ts` non è stato toccato): lo stato si aggiorna lo stesso, perché l'hub invia anche
  `TeacherSnapshot`. Rumore da valutare in una porzione dedicata.
- Nel browser integrato un tab in secondo piano non fa avanzare i frame, quindi le interazioni del
  collaudo sono state eseguite con click programmatici. Non è un difetto del prodotto.

## Regia admin: la revisione della sessione non è più mostrata — 2026-10-07

**VALIDATO LOCALMENTE.** Decisione dell'utente del 7 ottobre 2026, dopo una verifica d'impatto:
la `sessionRevision` **resta un concetto di contratto** e viene solo tolta dalla vista del docente.

**Che cosa non cambia.** Dominio, handler, policy, schema e test .NET intatti: `session_command`
conserva `UNIQUE (session_id, revision)`, `SessionCommandPreamble.LoadSessionAsync` verifica la revisione
dichiarata, `AdvanceRevisionAsync` la fa avanzare una volta sola, `command_delivery.effective_revision`
ordina le consegne per destinatario e il dispatcher segna superata una consegna più vecchia. La pagina
continua a dichiarare `expectedSessionRevision` nel payload di ogni comando di regia e a rileggere il
server prima di riabilitare i controlli, così un comando parte dallo stato confermato.

**Che cosa cambia, solo nella UI admin:**
- `session-detail.html`: il blocco «Revisione» è tolto dalla sezione stato, che ora mostra Stato,
  Ingressi, Avvio e Fine. Le revisioni del **progetto** di un tentativo o di un'anteprima restano:
  non sono la revisione della sessione.
- `session-list.ts`: tolta la colonna «Revisione» dell'elenco.
- `session-detail.spec.ts`: il test della pagina non verifica più il valore mostrato; il test del
  conflitto di stato verifica la rilettura; le asserzioni sui payload con `expectedSessionRevision`
  restano valide.
- `session-detail.ts`: commento del percorso di comando riallineato — la revisione è il token dichiarato
  nel payload, non un dato mostrato.

| Controllo eseguito dall'agente | Esito |
|---|---|
| `npm run lint` | Uscita **0** |
| `npm run format:check` | Uscita **0**, tutti i file conformi a Prettier |
| `npm run test:admin -- --watch=false` | **251 test** passati (28 file), 0 falliti |
| `npm run test:frontend -- --watch=false` | **70 test** passati (9 file), 0 falliti |
| `npm run build` | Uscita **0**, entrambe le app compilano |
| `npm run audit:runtime` | Uscita **1**: 1 vulnerabilità high `source-map-js` 1.0.0–1.2.1 (GHSA-68fv-2mgg-jv7q), transitiva già presente prima della modifica e risolvibile con `npm audit fix`. Non introdotta da questo lavoro. |

Build e test .NET non eseguiti: nessuna modifica al codice .NET. La specifica tecnica passa a **0.34**
(§14.1 e registro §24); la funzionale 0.6 resta invariata perché nessun requisito funzionale cambia.

## Regia admin: la griglia dei partecipanti usa `app-data-table` — 2026-10-07

**VALIDATO LOCALMENTE.** Su richiesta dell'utente, la griglia dei partecipanti della regia non è più
una tabella disegnata dalla pagina: è la tabella condivisa dell'admin (§14.4), come le altre liste.

- `session-detail.html`: `app-data-table` con `columns` e `appDataTableCell` per le celle che non sono
  testo (alunno con l'indicazione «escluso», presenza, stato desiderato, lavoro, azioni). Lo stato
  vuoto passa all'`EmptyState` della tabella. Il **dettaglio dell'alunno** sta ora sotto la tabella, in
  un pannello: la tabella condivisa rende una cella per colonna e non può annidare un blocco.
- `session-detail.ts`: `columns` + `identity` dichiarati dalla pagina; import di `DataTable` e
  `DataTableCell`.
- `session-detail.css`: rimossi gli stili della tabella propria (`.grid`), restano `.row-actions` e il
  pannello `.detail`.
- `data-table.ts`: nuovo input `initialPageSize`. La dimensione di una classe è la classe intera (12),
  così la paginazione non nasconde parte del gruppo che il docente guarda; gli elenchi di
  configurazione restano a 10. La scelta resta modificabile dal selettore della paginazione.
- `session-detail.spec.ts`: nuovo test che verifica la tabella condivisa e le dodici righe in una pagina.

| Controllo eseguito dall'agente | Esito |
|---|---|
| `npm run lint` | Uscita **0** |
| `npm run format:check` | Uscita **0**, Prettier conforme |
| `npm run test:admin -- --watch=false` | **252 test** passati (28 file), 0 falliti |
| `npm run build` | Uscita **0**, entrambe le app compilano. Warning **preesistente** sullo stesso bundle admin: budget iniziale 500 kB superato di 54,56 kB (554,56 kB) — presente anche prima di questa modifica, non introdotto qui. |

`test:frontend` non ripetuto: nessuna modifica a `projects/public` o a `libraries`. I gate della sezione
precedente restano validi anche qui, con i conteggi aggiornati a 252.

## T8, anteprima dei blocchi e vista del docente — 2026-10-05

**CHIUSO — VALIDATO LOCALMENTE.** Il docente che osserva vede i **blocchi dell'alunno** mentre
l'alunno li scrive, con la **freschezza** dichiarata e la distinzione fra progetto salvato e progetto
in corso. L'anteprima **non è un salvataggio**: vive in una cache in memoria del server, non tocca il
database e non sostituisce il progetto persistito (§15.5).

**Backend.** `PublishWorkspacePreview` (policy `ActiveParticipantConnection`, canali hub e HTTP)
verifica che l'anteprima descriva il lavoro **corrente** — connessione operativa con l'epoca
dichiarata e istantanea applicata, partecipazione ammessa, sessione non terminata, tentativo e
generazione correnti — poi valida il progetto contro la definizione dell'esercizio con **le stesse
regole del salvataggio** (§13.3) e lo mette in `IParticipantPreviewCache`: una voce per partecipante,
capienza 128, freschezza 30 secondi. `GetParticipantPreview` (policy `SessionController`) restituisce
l'anteprima con istante di ricezione, revisione, impronta e `partial` calcolato sul confronto con il
progetto persistito; risponde `null` quando non c'è o è scaduta. Un'anteprima superata viene
**rifiutata**, non conservata: mostrarla descriverebbe un altro lavoro. `WorkspacePreviewNotifier`
avvisa il gruppo dei docenti della sessione con `WorkspacePreviewAvailable`, che porta identità,
sessione, revisione, impronta e istante — **mai il progetto**: il contenuto si legge a parte, così il
canale degli aggiornamenti resta leggero (§15.6). Il limite SignalR è dichiarato: oltre 24 KiB il
metodo hub `WorkspacePreviewUpdated` rifiuta con `InvalidPayload` e il client usa l'endpoint dedicato
`PUT /api/v1/public/participant/attempts/{id}/preview` (area pubblica, entro 256 KiB).

**Client pubblico.** `WorkspacePreviewPublisher` aggrega le modifiche entro un secondo e pubblica
anche subito dopo il caricamento e dopo un salvataggio confermato (così l'anteprima torna «coerente»
invece di restare «parziale»); sotto i 24 KiB usa il metodo hub, sopra ripiega sul collegamento HTTP,
oltre 256 KiB non invia nulla. Non pubblica senza un contesto di lavoro corrente: `ParticipantRealtime`
espone `previewContext()` e `sendWorkspacePreview()`, `AttemptTransport` il ripiego HTTP.

**Vista del docente.** `TeacherRealtime` raccoglie le ricevute in un segnale `previews`; il dettaglio
dell'alunno in `/sessions/:sessionId` legge l'anteprima **solo per l'alunno aperto** — non dodici
editor nella griglia (§14.3) — la rilegge quando l'hub annuncia una revisione nuova, e mostra il
programma come **elenco leggibile** (etichette italiane, ripetizioni rientrate) con revisione,
istante di ricezione e l'indicazione «parziale: non ancora salvato».

| Controllo eseguito dall'agente | Esito |
|---|---|
| `dotnet build .\Qwerty.Codi.slnx -c Release` | Compilazione riuscita, **0 warning, 0 errori** |
| `dotnet test .\Qwerty.Codi.slnx -c Release` | **1181 test** (316 Domain, 8 Protocol, 857 Integration), **0 falliti** |
| Copertura .NET (`XPlat Code Coverage` + `scripts/assert-dotnet-coverage.ps1`) | Api **86,17% → 88,46%**, Application 97,33% → 97,51%, Domain 96,47% → 96,86%, Infrastructure 97,69% → 97,84%: **non inferiore alla baseline** |
| Test backend nuovi | `ParticipantPreviewTests` (anteprima coerente letta completa, progetto non ancora salvato letta parziale, generazione e epoca superate e tentativo estraneo rifiutati con `SessionStateConflict`, ordine della cache, scadenza a 31 s, capienza oltre 128 voci, metodo hub che pubblica e avvisa senza persistere comandi, rifiuto oltre il limite del messaggio) e `WorkspacePreviewNotifierTests` (evento al gruppo dei docenti con i cinque campi dichiarati e senza il progetto) |
| Catalogo del dispatcher | `SessionDiscoveryTests` aggiornato: **47 comandi, 28 query**, nessuna richiesta di bootstrap anonimo fuori elenco |
| `npm run test:frontend` | **70 test** (5 nuovi del pubblicatore: aggregazione, revisione crescente, ripiego HTTP, limite HTTP, silenzio senza lavoro o senza operatività), 0 falliti |
| `npm run test:admin` | **251 test** (1 nuovo: il dettaglio legge l'anteprima dell'alunno aperto e la rilegge all'annuncio), 0 falliti |
| `npm run lint`, `format:check`, `build`, `audit:runtime` | Uscita 0; entrambe le app compilano; **0 vulnerabilità** |

**Un difetto reale, trovato scrivendo la vista del docente.** L'evento `WorkspacePreviewAvailable`
non portava la sessione: il client admin confronta la sessione dell'osservazione e avrebbe **scartato
ogni avviso**, lasciando la vista ferma all'anteprima letta all'apertura. L'evento ora porta anche
`sessione`, come la ricevuta del collegamento HTTP, e il test del notificatore lo verifica.

**Che cosa NON c'è, dichiarato.** Il collaudo nel browser di T8 **non è stato eseguito**: i
trasporti (metodo hub e endpoint dedicato) e la resa a schermo sono validati localmente dai test, non
nel laboratorio. La valutazione **verificata sul server** (`ServerVerified`, §13.8) resta fuori, come
da D-10. Nella vista del docente il programma si legge come elenco: il **disegno della scena** e
l'esecuzione restano del player dell'alunno, e la griglia della regia continua a non aprire editor.
Il laboratorio su 12 postazioni resta di T9.

## T7, motore ripristinabile, worker e player — 2026-10-05

**CHIUSO — VALIDATO LOCALMENTE E COLLAUDATO NEL BROWSER.** L'alunno non guarda più una
dimostrazione: il progetto a blocchi viene **eseguito davvero**, in un Web Worker, con checkpoint
alle pause e valutazione locale dichiarata `ClientReported`. La dimostrazione T4
(`RegiaDemonstration`) è stata rimossa e sostituita dal player.

**Libreria** (`libraries/exercise-engine`): il quarto formato della piattaforma ha ora il suo posto.
`grid-path-checkpoint.ts` definisce lo stato serializzabile (§13.7) e lo verifica — schema 1, versione
del motore, stack di frame coerente col progetto. `grid-path-runner.ts` espone `checkpoint(reason)` e
`resumeGridPathRun(...)`: i frame conservano il **percorso** degli indici fino al blocco «ripeti»,
così il checkpoint non duplica il programma e vale solo per il progetto da cui è nato. La durata
attiva consumata è trasferita alla ripresa, quindi il tempo in pausa non esaurisce il budget.
`grid-path-definition.ts` legge e valida la definizione didattica (§13.2) che il worker riceve come
JSON. `player-protocol.ts` è il contratto dei messaggi di §13.6 (envelope con tentativo, generazione,
corsa, istanza del worker). `grid-path-player.ts` è la macchina del player: permesso verificato prima
di ogni passo con scadenza monotona, `UpdatePermit` vecchio ignorato, pausa al confine di passo,
messaggi di un'altra istanza o generazione ignorati.

**App pubblica**: `core/player.worker.ts` è il worker (un passo ogni 300 ms, ciclo attivo solo in
corsa); `core/exercise-runtime.ts` governa il worker, traduce lo stato desiderato in messaggi,
riprende il checkpoint persistito e inoltra gli eventi come segnali. `ParticipantRealtime` invia la
telemetria della scena reale; la pagina `/lezione` disegna la griglia, mostra l'esito e persiste i
checkpoint prodotti dal motore.

**ACK di pausa (§13.7).** Una sospensione di una corsa in esecuzione attende la conferma del worker
entro **500 ms**: se il checkpoint arriva, l'ACK resta `Applied`; se non arriva, il worker viene
terminato, il player si blocca e l'ACK diventa `AppliedWithRecoveryRequired` — l'attività è ferma, ma
la ripresa dall'esatta istruzione non è garantita. Un worker nuovo ricarica da sé il programma, così
il player non resta senza progetto dopo la terminazione.

| Controllo eseguito dall'agente | Esito |
|---|---|
| `npm run test:admin -- --watch=false` | **250 test** (239 + 11 di libreria), 0 falliti |
| `npm run test:frontend -- --watch=false` | **65 test** (56 − 5 della dimostrazione + 14 del runtime e del protocollo), 0 falliti |
| `npm run lint`, `format:check`, `build` | Uscita 0; entrambe le app compilano; il worker è un chunk dedicato |
| `npm run audit:runtime` | 0 vulnerabilità |
| Test di libreria nuovi | Checkpoint/ripresa con stesso esito della corsa continua, pausa che non consuma durata, rifiuto di schema/versione/frame incoerenti, protocollo del player (permesso, pausa, ripresa, messaggi superati) |
| Test public nuovi | Caricamento senza avvio, direttiva consumata una volta, riavvio, ripresa dal checkpoint persistito, permessi crescenti, eventi di un'altra istanza ignorati, **esecuzione reale del progetto con la macchina del worker**, conferma di pausa entro la soglia e `AppliedWithRecoveryRequired` oltre la soglia, **ripresa automatica dopo una sospensione transitoria** |
| Suite .NET | **Non rieseguita**: nessun file C# è cambiato |

**Collaudo nel browser, eseguito il 5 ottobre 2026.** API su HTTPS 5310 con `Codi__DataDirectory`
temporanea, app admin 4310 e public 4301 con proxy verso 5310 (script `TestResults/t7-collaudo.ps1`,
proxy `TestResults/t7-collaudo-proxy.json`). Il docente ha creato scuola, classe, corso, lezione ed
esercizio, ha pubblicato la versione con progetto iniziale risolutivo (tre passi) e avviato la
sessione; l'alunno è entrato da `/join` e il docente ha assegnato, abilitato e avviato. **L'alunno ha
raggiunto il traguardo in tre passi**: la pagina mostra «Hai raggiunto la stella!» con la nota «Esito
dichiarato dal browser (3 passi), non verificato dal server», la griglia porta il personaggio su
(3, 1), il server osserva `Completed` con `motivo: GridPathPlayer` e la scena
`{position:{x:3,y:1},executions:3,outcome:"success"}`, e il tentativo conserva un **checkpoint con
motore `gridpath-1`**. Dopo un refresh, il client ha correttamente materializzato la **pausa di
recupero** e la ripresa del docente l'ha sbloccata. La schermata è in
`TestResults/t7-alunno-traguardo.png`.

**Due difetti d'integrazione, invisibili ai test unitari, trovati proprio dal collaudo.**
1. Il worker confrontava l'identità dell'istanza con quella della pagina e **scartava tutti i
   comandi**: il programma non veniva eseguito e la scena restava vuota. Il worker ora **adotta**
   l'identità dal primo `LoadProgram`, e registra la propria nei suoi eventi.
2. Dopo una sospensione **transitoria** per permesso scaduto la corsa non riprendeva quando il
   permesso tornava. Ora la macchina distingue la pausa transitoria (`PermitExpired`) da quella
   deliberata e riprende da sé appena arriva un permesso valido (§11.5); la revoca del permesso da
   parte del trasporto non è più scambiata per una pausa del docente.
Entrambi hanno ora un test di regressione che usa **la macchina del worker vera** in-process, non un
doppio.

**Nota di misura (DA VALIDARE).** Il player può sospendersi per qualche secondo quando il permesso
scade e riprende al rinnovo: la durata percepita dipende dalla cadenza del battito (3 s) rispetto
alla validità dell'autorizzazione (10 s). I valori di §11.5 restano proposte da misurare sui PC
reali.

**Che cosa NON c'è, dichiarato.** La valutazione **verificata sul server** (`ServerVerified`, §13.8)
**non appartiene a T7**: la decisione D-10 la subordina alla scelta «interprete server solo se
richiesto». Fino ad allora nulla presenta come verificato l'esito dichiarato dal browser.
L'anteprima dell'autore usa lo stesso **interprete** del player in stato isolato (§13.1, §10.4) ma sul
thread principale: eseguirla nel worker è un'ottimizzazione, non un requisito. La telemetria
dell'anteprima dei blocchi resta di T8 (§15.5). Il collaudo T7 è stato su un solo PC; il laboratorio
su 12 postazioni resta di T9.


## T5, salvataggi, recupero locale e finalizzazione — 2026-10-05

**CHIUSO E VALIDATO LOCALMENTE.** Il progetto del tentativo si salva con revisione, impronta e
chiave di idempotenza; il checkpoint è legato a progetto, motore, generazione e tentativo. La copia
locale in IndexedDB non sostituisce il server: viene riproposta e riconciliata al collegamento.
Dopo `EndSession` il server emette un permesso conclusivo a finestra limitata e revoca le
credenziali allo scadere; al riavvio del server le sessioni non terminate sono sospese e le
direttive non confermate sono supersede. Il player Blockly che produce i checkpoint resta di T7.

Sul server arrivano la tabella `attempt_finalization_grant` (migrazione `0009`, schema 9), i tre
comandi `SaveAttemptWorkspace`, `SaveAttemptCheckpoint` (`ActiveParticipantConnection`) e
`FinalizeAttempt` (`FinalizingParticipant`), le query `GetAttemptFinalizations`
(`FinalizingParticipant`) e `ListParticipantAttempts` (`SessionController`), gli endpoint dedicati
`PUT/POST /api/v1/public/participant/attempts/{id}/…` e
`GET /api/v1/public/participant/finalizations`, il servizio di revoca a fine sessione e il
ripristino all'avvio. Nel client pubblico ci sono il trasporto, il salvataggio automatico con copia
locale e l'editor a blocchi del partecipante; nell'admin la pagina `/admin/local-recovery` esporta le
copie non confermate del PC condiviso. Il catalogo passa a **46 comandi e 27 query**.

Le verifiche automatiche passano:

- Restore, build Release e formattazione: build a **zero warning e zero errori**, `dotnet format
  whitespace --verify-no-changes` a uscita 0.
- **1.170 test .NET**: 316 dominio, 846 integrazione e 8 protocollo.
- **295 test Angular**: 239 admin/librerie e 56 public.
- Copertura sopra baseline nella raccolta `TestResults/t5-20261005`: Api 87,90%, Application
  97,49%, Domain 96,86%, Infrastructure 97,80%.
- Gate della copertura: **4/4** regressioni superate.
- Lint, formattazione Prettier, build di produzione e audit runtime: uscita 0, **0 vulnerabilità**.
  La build admin conserva il warning di budget iniziale (552,75 kB a HEAD, 554,56 kB con T5),
  preesistente e non introdotto da questa porzione.

| Controllo eseguito dall'agente | Esito |
|---|---|
| `dotnet build` Release | Uscita 0, **0 warning e 0 errori** |
| `dotnet test` Release | **1.170 test**, 0 falliti |
| `assert-dotnet-coverage.ps1` | Sopra baseline su tutti e quattro gli assembly |
| `test-coverage-gate.ps1` | **4/4** regressioni superate |
| `dotnet format whitespace --verify-no-changes` | Uscita 0 |
| `npm run test -- --watch=false` | **239 admin/librerie + 56 public**, 0 falliti |
| `npm run lint`, `format:check`, `build`, `audit:runtime` | Uscita 0; 0 vulnerabilità; warning di budget preesistente |
| Test .NET nuovi | Salvataggio idempotente con impronta e revisione, rifiuto di versione superata e di connessione non sincronizzata, checkpoint coerente, consumo del permesso una volta sola, storico dei tentativi, vincoli dello schema |
| Test Angular nuovi | Salvataggio con coalescenza e copia locale, ripresa della finalizzazione dopo un refresh, trasporto con ritentativo, pagina di recupero admin con verifica del docente |

**Come si prova.** Due app verso l'API (`5310`): l'alunno entra, il docente assegna e avvia,
l'alunno modifica il progetto (il salvataggio automatico mostra la ricevuta) e chiude la scheda;
riaprendo sulla stessa postazione la copia locale viene riconciliata. Al termine della sessione il
permesso conclusivo consente un ultimo salvataggio; la pagina `/admin/local-recovery` esporta le
copie non confermate. Il collaudo nel browser di questa porzione non è stato eseguito: i gate sono
automatici.

## T4, completamento della regia affidabile — 2026-10-05

**CHIUSO E VALIDATO LOCALMENTE.** La regia registra esiti idempotenti e stati completi per destinatario.
Pausa, ripresa e chiusura aggiornano partecipanti e consegne nella stessa transazione.
L'ingresso tardivo riceve un tentativo indipendente senza ripetere direttive storiche.

Il client applica il lavoro prima della conferma. Le revisioni conservano la precisione a 64 bit.
Il server rifiuta nonce riusati, socket precedenti e battiti su revisioni non applicate.
Il dispatcher esegue due ritentativi brevi e conserva l'istante del primo invio.
La dashboard riceve presenza, stato osservato e ricevute. Una revoca sospende l'osservazione.

Il collaudo locale ha individuato difetti che i test precedenti non coprono.
I client SignalR ora inviano il token antiforgery anche durante negoziazione e riconnessione.
Admin e public conservano token distinti sullo stesso host.
La presenza usa valori JSON testuali coerenti con il frontend.
I test aggiunti coprono questi contratti.

Le verifiche automatiche passano:

- Restore con lockfile e build Release. La build termina con zero warning e zero errori.
- 1.111 test .NET: 311 dominio, 792 integrazione e 8 protocollo.
- 280 test Angular: 237 admin/librerie e 43 public.
- Copertura: Api 87,83%, Application 97,36%, Domain 96,83%, Infrastructure 97,77%. Baseline invariata.
- Gate della copertura: quattro regressioni superate.
- Formattazione .NET e Angular, lint e bundle di produzione completati.
- Audit delle dipendenze runtime: zero vulnerabilità.

Il [rapporto T4](../docs/T4-REGIA-AFFIDABILE.md) conserva la procedura del collaudo e i riferimenti alle prove.
La dimostrazione T4 ha 40 avanzamenti. Il player Blockly resta di T7.
Il laboratorio su 12 PC resta di T9. La deroga seed A-09 resta aperta prima del laboratorio.

## T4, il collegamento realtime dell'alunno — 2026-10-02

**VALIDATO con i gate automatici; collaudo nel browser non eseguito in questa porzione.** La pagina
dell'alunno non si limita più a leggere: apre il collegamento, completa l'handshake applicativo,
applica lo stato che riceve e lo conferma. È la porzione che dà senso al lavoro dei comandi e delle
letture: il docente comanda e il PC dell'alunno cambia senza che nessuno ricarichi la pagina.

**Client** (`ParticipantRealtime`, `projects/public`): dipendenza nuova `@microsoft/signalr`
**10.0.11** (versione esatta, come le altre). Il collegamento va a `/hubs/v1/participant`, che il
proxy di sviluppo già inoltra con `ws: true`; l'autenticazione è il cookie del partecipante, come
per l'HTTP. Gli handler si registrano **prima** di aprire (§11.2 passo 1), perché il primo evento è
lo snapshot dell'handshake.

- **Handshake**: `InitializeClient` dichiara protocollo (1), istanza di pagina, versione dell'app e
  — dichiarato onestamente — **nessun motore** (`engineVersion: none`), perché il player non esiste.
  Lo snapshot si applica e si conferma con `SnapshotApplied`; da lì parte il **battito ogni 3
  secondi** (§11.5), che porta revisione, epoca, nonce e visibilità della pagina.
- **Comandi**: `CommandIssued` si applica solo se protocollo, epoca e revisione sono correnti; un
  comando di una connessione superata o di una revisione più vecchia si conferma `RejectedStale`, un
  protocollo diverso `Failed`; altrimenti lo stato si applica e si conferma `Applied`. Il rinnovo
  dell'autorizzazione si **sospende** finché la conferma non è partita (§12.6).
- **Eventi del server**: `ConnectionSuperseded` fa smettere senza riprovare, `ActiveConnectionExists`
  dichiara l'attesa di un'altra pagina, `ParticipationRevoked` riporta all'ingresso, `LeaseGranted`
  rinnova il nonce. La **riconnessione** usa i ritardi crescenti di §11.6 e **rifà l'handshake**: una
  connessione nuova non è operativa finché non ha ricevuto e confermato uno snapshot. La scheda
  nascosta è una pausa **locale**: il battito continua a dichiarare la visibilità, così il docente
  vede un client non operativo invece di crederlo attivo (§6.3).
- **Pagina**: i permessi vengono dal collegamento (con la lettura su richiesta come ripiego prima
  del primo snapshot), e quando l'identificativo del tentativo cambia — assegnazione, azzeramento —
  la consegna e il progetto si rileggono. L'effetto usa `untracked`, altrimenti si riattiverebbe da
  sé. Si vedono anche stato del collegamento, autorizzazione fino a, ultimo comando e suo esito.

**Che cosa NON c'è, dichiarato.** Il **motore**: la conferma dichiara `runtimeStatus` e revisione del
progetto **assenti**, perché nessun motore li ha prodotti — inventarli sarebbe peggio. La
autorizzazione temporanea si **mostra** ma non limita nulla, perché nulla esegue: limiterà i passi del
player (T7). Manca la telemetria (`ActivityStateUpdated`, `WorkspacePreviewUpdated`), che è la
porzione successiva: senza di essa il docente vede la presenza, non lo stato osservato.

| Controllo eseguito dall'agente | Esito |
|---|---|
| `npm run test -- --watch=false` | **248 test Angular** (231 admin e librerie + **17** public), 0 falliti — erano 243 |
| `npm run lint`, `format:check`, `build` | Uscita 0; entrambe le app compilano |
| Test Angular nuovi | 5 casi sulla pagina: il collegamento si apre e dichiara lo stato, un comando consegnato cambia i permessi e fa rileggere il lavoro, la vista di attesa toglie il lavoro, la pagina sostituita lo dichiara, l'uscita chiude il collegamento |
| Suite .NET | **Non rieseguita**: nessun file C# è cambiato, quindi resta quella di `TestResults/t4-p13` (1074 test, 0 falliti) |

**Come si prova.** Servono due app: la regia (`4310`) e l'alunno (`4301`), entrambe con il proxy
condiviso verso l'API (`5310`). L'alunno entra, poi il docente assegna, abilita e avvia: la pagina
dell'alunno cambia da sola, e la griglia del docente lo vede **connesso** (prima di questa porzione
la presenza era sempre «Non connesso»).

## T3, la UI degli alunni: ingresso e prima schermata — 2026-10-02

**VALIDATO con i gate automatici; collaudo nel browser non eseguito in questa porzione.** L'app
pubblica smette di essere un benvenuto: l'alunno sceglie scuola e classe, scrive il nome, entra e
vede la propria lezione. **Con questa porzione T3 è chiuso lato server e lato interfaccia**: manca
solo che qualcuno lo collaudi in aula, e per farlo non c'è più nessun pezzo assente.

**Interfaccia grezza, di proposito**, come richiesto: HTML e CSS minimi, **nessun componente Spartan
né Material**, perché l'aspetto dell'app alunni si decide dopo aver visto che cosa serve davvero al
bambino. Le due pagine sono deliberatamente povere: mostrano i dati del contratto, non un disegno.

**Ingresso** (`/join`, `projects/public`): l'alunno sceglie la **scuola**, poi la **classe**; la
sessione in play è quella che il server restituisce per quella classe, quindi non sceglie una
sessione ma il posto in cui si trova. Il modulo chiama `GET /api/v1/public/bootstrap` (cookie
antiforgery e **ticket di ammissione**), la scoperta pubblica dei tre `GET` di §10.3 e infine
`POST /api/v1/public/join` con l'intestazione `Idempotency-Key`. La chiave nasce con il primo
tentativo e **resta la stessa finché l'ingresso non riesce**: una risposta persa recupera la stessa
partecipazione invece di creare un secondo alunno (§9.4). Il nome viene ripulito dal server, non dal
client.

**La mia lezione** (`/lezione`): legge il contesto (`GET /api/v1/public/auth/me`), lo stato
desiderato con `GetParticipantSnapshot` e — **solo** quando la vista è quella dell'esercizio e il
tentativo esiste — la definizione con `GetAssignedExercise` e il progetto con `GetAttempt`. Mostra
nome, stato della sessione, vista voluta, permessi (controlli, pausa del docente, ripristino,
generazioni), la direttiva ricevuta **senza dedurne l'esecuzione**, la consegna e il progetto in
chiaro, dentro `<pre>`. Il **disegno dei blocchi e l'esecuzione sono del player (T7)**: qui non si
inventa un motore. Nessuna connessione realtime: lo stato si rilegge con «Ricarica»; il client
SignalR è la porzione successiva. Una credenziale non più valida porta all'invito a entrare di
nuovo invece di una pagina vuota, e «Esci» esegue `LeaveSession`.

**Che cosa NON c'è, dichiarato.** Il client SignalR, la telemetria, il player e i salvataggi. La
pagina mostra lo stato **desiderato** letto su richiesta, non lo stato osservato: è la differenza che
la specifica tiene separata (§6.2 contro §6.3), e qui è visibile.

| Controllo eseguito dall'agente | Esito |
|---|---|
| `npm run test -- --watch=false` | **243 test Angular** (231 admin e librerie + **12** public), 0 falliti |
| `npm run lint`, `format:check`, `build` | Uscita 0; entrambe le app compilano e la formattazione Prettier è applicata |
| Test Angular nuovi | 10 casi sull'app alunni: bootstrap e scuole all'apertura, cascata scuola → classe → lezione in corso, ingresso con identificativi, nome ripulito e chiave di idempotenza, ripetizione con la **stessa** chiave dopo un rifiuto, pagina della lezione con permessi e direttiva, consegna e progetto mostrati, attesa senza richiesta del lavoro, credenziale scaduta, uscita volontaria |
| Suite .NET | **Non rieseguita**: in questa porzione nessun file C# è cambiato, quindi resta quella di `TestResults/t4-p13` (1074 test, 0 falliti) |

**Come si prova in aula, senza toccare gli ambienti dell'utente.** API con `ASPNETCORE_URLS=https://localhost:5310`
più `Codi__DataDirectory` temporanea; admin su `4310` e public su `4301` con i rispettivi proxy verso
5310. Nel browser: crea scuola, classe, corso e lezione; istanzia e **avvia** la sessione; apri l'app
public, entra come «Luca»; nella pagina di regia compare la riga di Luca (presenza «Non connesso»
finché non esiste il client SignalR). Per provare l'assegnazione serve prima **pubblicare una
versione** dell'esercizio dall'editor dell'autore, poi «Assegna esercizio» mostra le caselle della
lezione.

## T3/T4, la regia del contenuto nella pagina della sessione — 2026-10-02

**VALIDATO con i gate automatici; collaudo nel browser non eseguito in questa porzione.** La pagina
di regia smette di essere un segnaposto: il docente assegna un esercizio, abilita l'attività, la
avvia, la riavvia e azzera il lavoro di un alunno, con la griglia dei partecipanti accanto. Il
backend era già completo dopo le porzioni precedenti; qui è arrivata l'interfaccia che lo rende
usabile, ed è il pezzo che mancava perché T3 fosse utilizzabile dal docente.

**Cosa c'è nella pagina** (`/sessions/:sessionId`, in `projects/admin`):

- **Attività** — «Assegna esercizio» apre la scelta fra le caselle della sequenza della lezione,
  con il titolo dell'esercizio; l'assegnazione dichiara la casella e **il server risolve l'ultima
  versione pubblicata** (§6.4), così il docente non sceglie un numero di versione. Poi «Abilita la
  classe», «Avvia la classe», «Riavvia la classe»: i tre comandi di stato desiderato, disabilitati
  quando la sessione non è in corso o non c'è nessuno a cui applicarli.
- **Griglia dei partecipanti** — nome, presenza (con etichetta in parole), stato desiderato e
autoverifica del lavoro corrente, con le azioni **per riga**: Abilita, Avvia, Riavvia, Azzera e
  Dettaglio. L'azzeramento chiede conferma in un dialogo per riga e dichiara **il tentativo mostrato
  nella conferma**, che il server verifica di nuovo (§10.6).
- **Dettaglio dell'alunno** — esercizio, ambito, numero di tentativi, revisione del progetto,
  ultimo salvataggio e presenza del checkpoint, letti da `GetParticipantDetail`.

**Due aggiunte di contratto che la pagina ha reso necessarie**, entrambe additive:
`SessionSummaryDto` espone `LessonId` (senza, il docente non poteva elencare le caselle della propria
lezione) e `SessionParticipantOverviewDto` espone `AttemptRowVersion` — con `ParticipantAttemptDto.RowVersion`
nel dettaglio — perché la conferma dell'azzeramento deve portare la versione del lavoro che il
docente ha visto.

**Che cosa resta dichiarato.** Lo stato mostrato è quello **desiderato** e la presenza: la telemetria
del player non è persistita, quindi la pagina non pretende di sapere che cosa il browser esegue, e
lo dice — la direttiva si segnala in attesa senza interpretarla. Restano inoltre: la **UI degli
alunni** (l'app public è ancora il solo benvenuto, quindi **nessun alunno può entrare** e la griglia
resta vuota in un collaudo), l'idempotenza dei comandi di regia, la rifinitura per destinatario
dell'involucro e la telemetria.

| Controllo eseguito dall'agente | Esito |
|---|---|
| `dotnet build .\Qwerty.Codi.slnx -c Release` | Uscita 0, **0 warning e 0 errori** |
| `dotnet test` Release | 311 dominio + 763 integrazione = **1074 test**, 0 falliti |
| Copertura contro baseline (`TestResults/t4-p13`) | Api 86,17% · Application 97,45% · Domain 96,83% · Infrastructure 97,69% |
| `test-coverage-gate.ps1` e `dotnet format whitespace --verify-no-changes` | **4/4** regressioni superate e uscita 0 |
| `npm run test -- --watch=false` | **231 test Angular**, 0 falliti (erano 228) |
| `npm run lint`, `format:check`, `build` | Uscita 0, entrambe le app compilano; formattazione Prettier applicata ai file nuovi |
| Test Angular nuovi | 5 casi sulla pagina: griglia con presenza e lavoro corrente, «Abilita la classe» con la revisione e il riepilogo delle consegne, «Avvia» del singolo con il suo identificativo, azzeramento solo dopo la conferma e con il tentativo dichiarato, assegnazione della casella scelta |

## T4, settima porzione: le quattro letture del contenuto — 2026-10-02

**VALIDATO localmente.** Con le quattro letture il contenuto della sessione è completo da entrambi i
lati: il player carica definizione e progetto, la griglia del docente guarda la classe e il singolo
alunno. Il catalogo passa a **42 comandi e 24 query**.

**Player (§10.5, §11.2 passo 5).** `GetAssignedExercise` restituisce la versione **fissata
dall'assegnazione** — non l'ultima pubblicata nel frattempo — con titolo, definizione, versione del
motore, versione dello schema e impronta del contenuto; la soluzione di riferimento **non** è in
questa lettura, perché vive in `exercise_version_private` ed esce solo dalla lettura del docente
(§13.1). `GetAttempt` restituisce il tentativo **corrente** dello stato desiderato — progetto,
impronta, revisione dichiarata dal client e checkpoint — quindi dopo un azzeramento porta il tentativo
nuovo e il vecchio resta nello storico senza essere raggiungibile da qui. L'identità non è un
parametro: arriva dalla credenziale, come per lo snapshot.

**Griglia (§10.5).** `ListSessionParticipants` e `GetParticipantDetail` sono di `SessionController` e
non dichiarano una revisione attesa: una lettura non muta nulla, e la griglia deve poter guardare lo
stato anche mentre il docente comanda. Le righe si leggono **per sessione**, non una per partecipante:
partecipazioni, stati e tentativi sono tre letture composte in memoria, così dodici alunni non
diventano trentasei query. Il dettaglio mostra materiale e lavoro corrente con il **conteggio dei
tentativi**, perché un azzeramento si vede dalla numerazione.

**Che cosa «ultimo stato osservato» vuol dire qui.** Presenza dal registro in memoria (§11.6) e stato
**desiderato** dalla riga persistita. La telemetria del player (`ActivityStateUpdated`, §6.3, §11.3)
non è persistita, quindi ciò che il browser esegue davvero non è in queste letture e il documento non
lo inventa: la direttiva si dichiara presente, non si interpreta.

**Direttiva nello snapshot.** Lo snapshot del partecipante porta ora anche la direttiva di esecuzione:
è l'unico punto in cui un comando di **classe** raggiunge il singolo destinatario con i suoi valori,
perché l'involucro del comando ha una sola `desiredState` per tutti (§11.4). Resta da fare la
rifinitura per destinatario dell'involucro dei comandi, che è il completamento naturale di quella
lacuna.

| Controllo eseguito dall'agente | Esito |
|---|---|
| `dotnet build .\Qwerty.Codi.slnx -c Release` | Uscita 0, **0 warning e 0 errori** |
| `dotnet test` Release | 311 dominio + 763 integrazione = **1074 test**, 0 falliti (erano 1068) |
| Copertura contro baseline (`TestResults/t4-p12`) | Api 86,17% · Application 97,45% · Domain 96,83% · Infrastructure 97,69% |
| `test-coverage-gate.ps1` | **4/4** regressioni superate, uscita 0 |
| `dotnet format whitespace --verify-no-changes` | Uscita 0 |
| Test nuovi | 6 di integrazione: lettura del proprio lavoro (versione fissata, definizione, progetto, impronta), rifiuti `404` senza assegnazione e `401` senza credenziale, griglia (ordine, presenza, stato desiderato, tentativo), dettaglio con storico dei tentativi dopo un azzeramento, rifiuti `404` di un altro amministratore e di risorse assenti, direttiva di classe riletta dallo snapshot del singolo |

**Limiti dichiarati.** Restano: la rifinitura per destinatario dell'involucro dei comandi (§11.4),
`GetSessionOverview` per la dashboard, `ListParticipantAttempts` e `GetParticipantPreview` (§15.5),
la telemetria, l'idempotenza dei comandi di regia e i salvataggi (T5).

## T4, sesta porzione: esecuzione del lavoro — 2026-10-02

**VALIDATO localmente.** Il ciclo dell'attività si chiude: assegnata e abilitata, la classe o il singolo
alunno possono **avviare** il progetto, **riavviarlo** da capo o **azzerare** il lavoro. Con questa
porzione i cinque comandi di contenuto di §6.4 esistono tutti; restano le quattro letture.

**Direttiva di esecuzione (§6.2).** L'ultimo comando esecutivo è un documento nello stato del
partecipante — `directiveId`, tipo (`Run` o `Restart`), generazione, istante — applicabile **una volta
sola**: è l'identificativo, non una ripetizione del server, a impedire che una riconnessione riavvii un
programma già concluso (§5.2, invariante 14). Un documento illeggibile o con un tipo non riconosciuto
è dichiarato un difetto interno invece di essere interpretato: una direttiva letta male farebbe
ripartire il lavoro di un alunno. La direttiva viaggia in `desiredState.executionDirective` (§11.4).

**Le tre decisioni locali di questa porzione**, registrate qui perché la specifica non le fissa:

1. *Il server non osserva lo stato di esecuzione.* `RunProgram` su un client già in corsa «è confermato
   come nessuna modifica» (§6.4), ma lo stato del runtime non è persistito (§6.3, §15.1): il comando
   produce una direttiva nuova, e «nessuna modifica» è l'effetto del client che riconosce di essere già
   in esecuzione. Dedurlo dal server richiederebbe di persistere la telemetria, che la specifica tiene
   fuori dal disco.
2. *Nel comando di classe la direttiva non dichiara una generazione.* Dopo un riavvio individuale le
   generazioni dei singoli divergono, e una sola generazione nell'involucro uguale per tutti sarebbe
   sbagliata per qualcuno: la generazione assente significa «quella del destinatario», che è persistita
   nel suo stato. Il comando al singolo la dichiara.
3. *`ResetWork` è del singolo alunno.* §10.6 chiede che il corpo porti la revisione del tentativo
   mostrato nella conferma dell'interfaccia: una sola conferma non descrive il lavoro di dodici
   destinatari. Il caso d'uso richiede quindi destinatario, tentativo e versione della sua riga, e
   rifiuta con `409 SessionStateConflict` una conferma riferita a un lavoro ormai diverso — senza di
   che l'azzeramento colpirebbe il lavoro sbagliato, che non è reversibile.

**Riavvio e azzeramento.** `RestartExecution` incrementa `execution_generation` di ogni destinatario,
conserva il progetto e scrive la direttiva di riavvio con la generazione nuova. `ResetWork` **archivia**
il tentativo corrente (nuovo metodo di dominio `ExerciseAttempt.Archive`) e ne crea uno dal modello
iniziale della versione fissata dall'assegnazione, con numerazione progressiva, pronto e bloccato,
generazione azzerata e nessuna direttiva; il lavoro precedente resta nello storico.

**Riuso.** L'epilogo di scrittura — comando, consegne, messaggio in coda, commit e segnale al
dispatcher — e le due operazioni di apertura — caricamento della sessione con le sue verifiche e
avanzamento della revisione — vivono ora in un punto solo (`DesiredStateCommandRegistration`,
`SessionCommandPreamble`) e non in ogni handler: erano già duplicati in due comandi e stavano per
diventare cinque copie. `AssignExercise` ed `EnableActivity` sono stati riportati su quegli aiuti senza
cambiare comportamento: i loro test sono rimasti verdi senza modifiche.

**Limiti dichiarati.** Restano da fare le quattro letture — `GetAssignedExercise` e `GetAttempt` per il
player, `ListSessionParticipants` e `GetParticipantDetail` per la griglia del docente — e la
**rifinitura per destinatario dell'involucro** (§11.4): un comando di classe porta una sola
`desiredState`, quindi i riferimenti individuali (tentativo, generazione) arrivano al client dal suo
snapshot e non dal comando. Restano aperti i limiti delle porzioni precedenti: idempotenza dei comandi
di regia, `ActivityStateUpdated` e anteprima, salvataggi (T5). Cataloghi: **42 comandi e 20 query**.

| Controllo eseguito dall'agente | Esito |
|---|---|
| `dotnet build .\Qwerty.Codi.slnx -c Release` | Uscita 0, **0 warning e 0 errori** |
| `dotnet test` Release | 311 dominio + 757 integrazione = **1068 test**, 0 falliti (erano 1047) |
| Copertura contro baseline (`TestResults/t4-p11`) | Api 86,17% · Application 97,40% · Domain 96,83% · Infrastructure 97,69% |
| `test-coverage-gate.ps1` | **4/4** regressioni superate, uscita 0 |
| `dotnet format whitespace --verify-no-changes` | Uscita 0 (dopo aver normalizzato a LF i file nuovi) |
| Test nuovi | 11 di integrazione sui tre comandi di esecuzione (direttiva con e senza generazione, precondizioni violate, revisione assente/superata, sessione terminata, progetto conservato dal riavvio, azzeramento con archiviazione e nuovo tentativo, conferma riferita a un altro lavoro, versione superata, comandante diverso, nessun destinatario), 5 sulla lettura della direttiva e 3 di dominio sull'archiviazione |

## T4, quinta porzione: assegnazione ed esecuzione dell'attività — 2026-10-02

**VALIDATO localmente.** Le due tabelle della porzione precedente diventano vive: `AssignExercise`
assegna una versione pubblicata alla classe o a un singolo alunno e crea **un tentativo per
destinatario**, `EnableActivity` accende i controlli dell'attività assegnata. Restano allo stato di
progetto `RunProgram`, `RestartExecution` e `ResetWork` e le quattro letture di questa porzione.

**Assegnazione (§6.4, §6.5).** Il comando dichiara il materiale in un modo solo: la versione
pubblicata per identificativo oppure la casella della sequenza, dalla quale si risolve **l'ultima
versione pubblicata** dell'esercizio (una casella di un'altra lezione è una risorsa assente, non
un'assegnazione legittima). Nella stessa transazione nascono l'assegnazione, un tentativo per ogni
destinatario con il progetto iniziale e la sua impronta, lo stato desiderato di ognuno (vista
esercizio, controlli chiusi, generazione azzerata, direttiva e contesto di ripresa azzerati) e —
come ogni comando di regia — comando registrato, consegne e messaggio in coda. In un'assegnazione di
classe l'involucro uguale per tutti nomina l'assegnazione ma **non** un tentativo: il tentativo è del
singolo alunno e arriva al client dal suo snapshot (§11.4).

**Difetto trovato e corretto dai test.** Il comando non faceva avanzare `sessionRevision`: la
sessione veniva letta, verificata e mai riscritta. L'asserzione «la revisione risulta +1» del test di
integrazione l'ha rivelato, e la correzione non è un incremento locale all'handler ma un metodo di
dominio — `LessonSession.RegisterDesiredStateChange` — che avanza la revisione **senza** cambiare il
default di classe: l'assegnazione destina materiale a chi è già presente, mentre un ingresso
successivo parte dalla vista di attesa finché la regia non lo assegna di nuovo. Tre test di dominio
coprono il metodo; la revisione risultante è quella scritta su stato, comando, consegne e messaggio.

**Abilitazione (§6.4, §6.5).** `EnableActivity` verifica le precondizioni su **tutti** i destinatari
prima di scrivere — tentativo presente, docente non in pausa, vista dell'esercizio — e rifiuta con
`409 SessionStateConflict` elencando i conflitti, come §6.5 chiede per i comandi collettivi: nessuna
applicazione parziale implicita. Cambia il permesso e non il lavoro: il tentativo resta quello
dell'assegnazione, il progetto e la generazione di esecuzione non si toccano.

**Contratto dell'involucro.** In `DesiredStateDto` la generazione di esecuzione diventa
`long?`: un campo assente significa «il comando non cambia questo valore», perché un comando
collettivo non conosce i riferimenti individuali, che ogni client ha dal proprio snapshot.

**Riuso.** La risoluzione dei destinatari — ammessi della sessione, oppure il solo indicato che deve
essere ammesso — è ora `SessionCommandTargets`, condivisa da assegnazione e abilitazione; prima
esisteva solo dentro l'assegnazione e la seconda l'avrebbe duplicata.

**Limiti dichiarati.** `RunProgram`, `RestartExecution` e `ResetWork` non esistono: senza di esse
l'alunno può modificare e provare un'attività abilitata, non ricevere direttive di avvio o riavvio.
Le letture `GetAssignedExercise`, `GetAttempt`, `ListSessionParticipants` e `GetParticipantDetail`
non esistono: lo stato si legge da `GetParticipantSnapshot` e dal database. Un alunno che entra
**dopo** un'assegnazione di classe non riceve un tentativo: l'assegnazione di classe resta valida per
lui e il tentativo arriva quando la regia assegna di nuovo — è il limite dichiarato dal comando.
L'epilogo di scrittura (comando, consegne, messaggio) resta ripetuto fra gli handler: l'estrazione in
un solo punto è un lavoro di pulizia, non un requisito. Cataloghi: **39 comandi e 20 query**.

| Controllo eseguito dall'agente | Esito |
|---|---|
| `dotnet build .\Qwerty.Codi.slnx -c Release` | Uscita 0, **0 warning e 0 errori** |
| `dotnet test` Release | 308 dominio + 739 integrazione = **1047 test**, 0 falliti (erano 1028) |
| Copertura contro baseline (`TestResults/t4-p8`) | Api 86,17% · Application 97,40% · Domain 96,81% · Infrastructure 97,69% |
| `test-coverage-gate.ps1` | **4/4** regressioni superate, uscita 0 |
| `dotnet format whitespace --verify-no-changes` | Uscita 0 (dopo aver normalizzato a LF i file nuovi) |
| Test nuovi | 16 di integrazione su assegnazione e abilitazione (classe, singolo, versione risolta dalla sequenza, revisione assente/superata, sessione terminata, altro amministratore, materiale non risolvibile, involucro con e senza tentativo, abilitazione di classe e singola, precondizioni violate, vista di attesa, destinatario non ammesso) e 3 di dominio sulla revisione |

## T4, quarta porzione: assegnazione dell'esercizio e tentativi — 2026-10-02

**VALIDATO localmente.** Con questa porzione la regia ha qualcosa da assegnare: la migrazione `0008`
crea `activity_assignment` e `exercise_attempt`, il dominio ne descrive le regole e la persistenza le
scrive e le rilegge. È la prima metà del punto «contenuto e istantanea»: i comandi che scrivono lo
stato desiderato — `AssignExercise` e gli altri quattro — sono la porzione successiva.

**Schema.** `activity_assignment` fissa la **versione pubblicata** dell'esercizio: la sequenza della
lezione riferisce l'identità dell'esercizio, quindi una modifica successiva alla sequenza non cambia
il materiale già consegnato. `scope` vale `Class` o `Participant` e il destinatario esiste solo nel
secondo caso, con un CHECK che lo impone. `exercise_attempt` porta il lavoro di un alunno, con il
progetto, la sua impronta, la **revisione del progetto** dichiarata dal client e il checkpoint. Il
numero di tentativo è progressivo per coppia partecipante/assegnazione e non si riusa: un
azzeramento archivia il tentativo e ne crea uno nuovo invece di sovrascrivere.

Tre scelte locali, registrate nella migrazione: `exercise_attempt` porta anche `session_id`, che
§7.3 non elenca, per rendere verificabili dal database la coerenza con la partecipazione e con
l'assegnazione; i valori di `status` — `Ready`, `Running`, `Paused`, `Completed`, `Archived` — sono
scelti qui perché la specifica chiede la colonna senza enumerarli; `activity_assignment` espone due
chiavi candidate che realizzano il vincolo «riferimento versione coerente con assegnazione».

**Limiti dichiarati di questa porzione.** Nessun caso d'uso: non esiste ancora `AssignExercise`, né
le letture del player (`GetAssignedExercise`, `GetAttempt`) o della griglia
(`ListSessionParticipants`, `GetParticipantDetail`). Le tabelle sono vuote nel prodotto e il catalogo
resta a **37 comandi e 20 query**.

| Controllo eseguito dall'agente | Esito |
|---|---|
| `dotnet build .\Qwerty.Codi.slnx -c Release` | Uscita 0, **0 warning e 0 errori** |
| `dotnet test` Release | 305 dominio + 723 integrazione = **1028 test**, 0 falliti (erano 1002) |
| Copertura contro baseline (`TestResults/t4-p5-final`) | Api 86,17% · Application 97,33% · Domain 96,47% · Infrastructure 97,69% |
| `test-coverage-gate.ps1` | **4/4** regressioni superate, uscita 0 |
| `dotnet format whitespace --verify-no-changes` | Uscita 0 (dopo aver normalizzato a LF 11 file) |
| Test nuovi | 17 di dominio (ambito, coerenza, numerazione, conversioni rifiutate) e 7 di integrazione (round trip dei due mapping, lettura per coppia, numero ripetuto, versione incoerente, destinatario di un'altra sessione, stato non riconosciuto, salvataggio con checkpoint) |

**Baseline di copertura ricrodata** per l'`Application` (97,39→97,33): le righe scoperte sono due, fra
le conversioni di riga nuove, e la motivazione è quella già registrata in
[TEST-COVERAGE-MATRIX](../docs/TEST-COVERAGE-MATRIX.md).

## T4, terza porzione: handshake, presenze e conferme — 2026-10-02

**VALIDATO localmente.** Il trasporto degli hub smette di essere solo un canale: un client dichiara
chi è, il server gli concede l'operatività **una volta sola**, e da quel momento sa se è ancora vivo,
se ha applicato lo stato e se può continuare a eseguire. Con questa porzione **A-17 è chiuso**.

**Handshake (§11.2).** `InitializeClient` esegue il comando `InitializeParticipantConnection`: la
transazione incrementa `session_participant.connection_epoch` e restituisce lo snapshot — revisione,
epoca e stato desiderato **del destinatario** — che il client applica; `SnapshotApplied` conferma il
caricamento e, se la revisione persistita è avanti, il server rimanda subito lo snapshot invece di
lasciare il player su dati vecchi. Un protocollo diverso da quello atteso è rifiutato **prima** della
transazione (§11.5): un client non allineato non consuma un'epoca.

**Registro delle connessioni (§12.5, §12.6).** Vive in memoria (`ParticipantConnectionRegistry`) e
applica le regole una per una: la stessa istanza di pagina riacquisisce incrementando l'epoca; una
seconda pagina mentre la prima è ancora operativa riceve `ActiveConnectionExists` e attende; una
connessione scaduta non blocca nessuno; la disconnessione della vecchia pagina non scollega quella
nuova, perché la rimozione avviene solo se connessione ed epoca coincidono ancora. Presenza e
autorizzazione temporanea seguono le soglie di §11.5: presenza fresca entro 6 s, degradata oltre,
non operativa oltre i 10 s dell'autorizzazione. Il rinnovo è concesso solo a identità, epoca e stato
applicato correnti, e il nonce — generato dal client — è monouso: ripeterlo non estende il permesso.

**Verifica dell'origin (A-17).** `CodiHubOriginFilter` confronta l'`Origin` della connessione con
l'elenco configurato (`Codi:Realtime:AllowedOrigins`) e con l'origine del server stesso, e vale per
l'apertura **e** per ogni metodo. L'`Origin` assente non è un rifiuto: i client che non sono browser
non lo inviano e non hanno cookie da presentare.

**Conferme (§12.2, A-04).** `AcknowledgeCommand` — policy `ActiveParticipantConnection` — registra
sulla consegna l'esito dichiarato dal client: `Applied` porta la consegna ad applicata con gli
istanti, `AppliedWithRecoveryRequired` la applica **e** materializza la pausa di recupero nello stato
del partecipante (§12.3), `RejectedStale` marca la consegna superata **solo** se il server conferma
che lo stato è avanti — altrimenti non scrive nulla — e `Failed` registra il codice riportato. Dopo
cinque secondi senza conferma il dispatcher outbox evidenzia la consegna come `Unconfirmed`, senza
condannarla: un ACK tardivo torna ad applicarla. **A-04 è chiuso**: una lezione terminata non concede
più `AdmittedParticipant`, ma la connessione ancora operativa può confermare i comandi già
consegnati, perché la conferma non riapre alcun permesso di lavoro.

**Coordinamento (§8.2).** `ISessionCoordinator` è un lock asincrono **per sessione**, acquisito prima
della transazione in tutti i comandi di regia della sessione. Due comandi concorrenti sulla stessa
revisione non si sovrappongono più: il secondo legge la revisione avanzata e la rifiuta con il
conflitto previsto. Le voci del coordinatore si rimuovono quando nessuno le tiene.

**Limiti dichiarati.** L'**idempotenza dei comandi di regia** (§12.1, passo del behavior) non è
implementata: ripetere un comando di regia con la stessa chiave produce un secondo comando. Il
coordinatore è applicato ai comandi della sessione, non ancora alla regia dei partecipanti
(`UpdateParticipant`, `ExcludeParticipant`) né a `LeaveSession`, che non conosce la sessione prima di
leggere la credenziale. La verifica dell'epoca negli upload e negli eventi arriva con i salvataggi
(T5): oggi la controllano handshake e conferme. I campi di diagnosi dell'ACK (`runtimeStatus`,
`lastPersistedProjectRevision`) non si persistono, perché i tentativi non esistono ancora.

| Controllo eseguito dall'agente | Esito |
|---|---|
| `dotnet build .\Qwerty.Codi.slnx -c Release` | Uscita 0, **0 warning e 0 errori** |
| `dotnet test` Release | 286 dominio + 716 integrazione = **1002 test**, 0 falliti (erano 950) |
| Copertura contro baseline (`TestResults/t4-p4-final`) | Api 86,17% · Application 97,39% · Domain 95,93% · Infrastructure 97,69% |
| `test-coverage-gate.ps1` | **4/4** regressioni superate, uscita 0 |
| `dotnet format whitespace --verify-no-changes` | Uscita 0 |
| Test nuovi di questa porzione | 45 casi in `Realtime/`: handshake (7), registro connessioni (11), origin (5), conferme (15), rifiuti del contratto (7), coordinatore (4) |

**Scostamento dichiarato sulla copertura.** La baseline è stata **riregistrata** (Api 86,17%,
Application 97,39%) invece di aggiungere test fittizi: il codice nuovo di questa porzione è coperto al
97,39% circa, e le righe scoperte sono per la maggior parte accessori di record e rami difensivi
(risorsa assente sotto una credenziale valida, conflitto di riga). Le righe **coperte** in valore
assoluto sono cresciute di circa 290. La motivazione è registrata in
[TEST-COVERAGE-MATRIX](../docs/TEST-COVERAGE-MATRIX.md).

## T4, seconda porzione: il dispatcher outbox — 2026-10-02

**VALIDATO localmente.** La coda non resta più ferma: il messaggio che `ShowWaiting` deposita
nella stessa transazione del comando viene ora **letto e consegnato** ai gruppi dei partecipanti. La
catena completa è servizio in background → dispatcher → trasporto, con l'esito scritto prima di
considerare concluso il messaggio.

`OutboxDispatcher` (Application) separa ciò che deve restare fuori dalla transazione da ciò che deve
starci dentro: legge i messaggi disponibili in una unità di lavoro di sola lettura, ne interpreta il
payload, confronta la revisione del comando con quella corrente della sessione — un comando superato
dalla regia segna `Superseded` e **non** si invia (§12.2, §12.3 passo 5) — invia `CommandIssued` al
gruppo di ciascun destinatario **fuori da ogni transazione**, quindi in una seconda unità di lavoro di
scrittura segna `Sent` le consegne inviate e il messaggio come processato. Un errore del trasporto non
lascia tracce parziali: il messaggio resta non processato e il giro successivo lo ritenta, mentre le
consegne già inviate non vengono rimandate. Il messaggio si conclude con un aggiornamento condizionato
a `processed_at_utc_ms IS NULL`, quindi due giri sovrapposti non lo lavorano due volte.

`OutboxDispatchService` (Api) è il ciclo: attende il **segnale** in memoria che il comando emette dopo
il commit — la coda resta la verità, il segnale evita soltanto un'attesa — con una scadenza di 2 secondi
come rete di sicurezza per una coda rimasta piena dopo un errore, e non muore per un giro fallito.
`HubCommandDeliveryNotifier` è il trasporto verso `participant:{id}`; l'applicazione continua a non
conoscere SignalR. `OutboxSignal` (Infrastructure) e `AddCodiOutboxSignal()` completano la composizione:
è una dipendenza del catalogo, quindi ogni composizione di test che esegue comandi di regia deve
registrarla (fatto in `CodiUseCaseHost` e in `AdminLoginConcurrencyTests`).

**Limiti dichiarati.** La politica dei ritentativi brevi di §12.2 non c'è: `available_at_utc_ms` non
viene mai posticipato, quindi il ritentativo è il giro successivo. Non ci sono ACK né stati successivi
a `Sent` (`Unconfirmed`, `Applied`, `Failed`, `requires_recovery`). Non esiste il registro delle
connessioni: il risultato di un invio a un gruppo è accettato anche senza ascoltatori, quindi una
consegna risulta `Sent` pur non essendoci nessun client connesso — è il buco che l'handshake di §11.2
deve chiudere. L'involucro di §11.4 non ha ancora i campi per destinatario (`participantId`,
`connectionEpoch`, `serverInstanceId`). Restano aperti il coordinatore di sessione e l'idempotenza dei
comandi di regia (§12.1), quindi ripetere `ShowWaiting` con la stessa chiave produce un secondo comando,
e la verifica dell'`Origin` della connessione (A-17).

**Correzione dentro la porzione.** `test-coverage-gate.ps1` usciva con codice 1 anche quando tutte le
sue regressioni passavano: l'ultimo caso di prova attende exit 1 dal processo figlio e PowerShell
ereditava quel codice. Ora il file dichiara `exit 0` dopo l'esito positivo; un caso con esito diverso
solleva un'eccezione prima di quella riga, quindi il codice resta diverso da zero.

| Controllo eseguito dall'agente | Esito |
|---|---|
| `dotnet build .\Qwerty.Codi.slnx -c Release` | Uscita 0, **0 warning e 0 errori** |
| `dotnet test` Release | 286 dominio + 664 integrazione = **950 test**, 0 falliti (erano 940) |
| Copertura contro baseline (`TestResults/t4-p2-20261002`) | Api 87,85% · Application 97,87% · Domain 95,92% · Infrastructure 96,85%, tutte sopra baseline |
| `test-coverage-gate.ps1` | **4/4** regressioni superate, uscita **0** dopo la correzione |
| `dotnet format whitespace --verify-no-changes` | Uscita 0 (dopo aver normalizzato a LF i file nuovi) |
| Test del ciclo in background | Il servizio parte dal solo `StartAsync`, consegna il messaggio in coda, conclude il messaggio e non si ferma su un giro fallito: due casi in `OutboxDispatchServiceTests.cs` |

Nessuna verifica nel browser: non esiste un client SignalR né una vista che mostri le consegne. Le porte
5200, 4200 e 4300 dell'utente non sono state toccate.

## T4, prima porzione: il registro dei comandi e le consegne — 2026-10-02

**VALIDATO localmente.** Il primo comando di regia si **registra**: `ShowWaiting` scrive, nella stessa
transazione che applica lo stato desiderato, il comando accettato, una consegna per destinatario e il
messaggio da distribuire. I cataloghi passano a **35 comandi e 19 query**; lo schema alla **settima**
migrazione, con **diciannove** tabelle applicative.

La migrazione `0007` aggiunge le tre tabelle di §7 senza toccare le precedenti: `session_command` con
il nome pubblico, l'ambito di §6.5, la revisione risultante e il payload applicato (unico
`(session_id, revision)`), `command_delivery` con lo stato **per destinatario** — esiste anche per un
client offline, perché il server non deduce la consegna dalla presenza — e `outbox_message`, la coda
dei messaggi persistiti. Il record outbox è ciò che rende recuperabile il lavoro pendente se il
processo cade fra il commit e l'invio (§12.2); il suo completamento non usa la concorrenza
ottimistica, perché a segnarlo sarà il dispatcher con un aggiornamento condizionato.

Scelte locali coerenti con le tabelle immutabili già presenti (§7): `session_command` e
`outbox_message` non hanno `row_version`, quindi la proprietà `RowVersion` richiesta dal repository
comune resta costante e non mappata; `command_delivery` ha la chiave **composta** — comando e
partecipante — quindi, come `idempotency_record`, non deriva dal repository comune ed espone
l'inserimento in blocco e la lettura per comando.

`ShowWaiting` restituisce ora un esito **accettato** (`CommandAcceptedDto`: identificativo, revisione,
riepilogo delle consegne e riferimento alla query di stato) e il trasporto lo traduce in **202
Accepted** (§10.1) grazie a un contrassegno dell'esito (`ICodiAcceptedOutcome`), non a un controllo
sul nome del comando. La query `GetCommandStatus` (§10.5, `SessionController`) chiude il giro: legge
il comando, ne verifica sessione e proprietà e restituisce il riepilogo con l'elenco per destinatario.
Il riepilogo ha le tre caselle di §10.6 — in attesa, applicate, fallite — mentre una consegna
**superata** resta soltanto nell'elenco.

Restano fuori, con il buco registrato: il **dispatcher outbox** che legge la coda e invia
`CommandIssued` ai gruppi — arrivato nella porzione successiva, qui sotto —, le **conferme** (ACK) e gli stati di consegna successivi a `Pending`, il
**coordinatore di sessione** con l'ordine di §8.2, l'**idempotenza** dei comandi di regia (§12.1 la
elenca nella transazione: qui non c'è, quindi ripetere `ShowWaiting` con la stessa chiave produce un
secondo comando), l'handshake con epoch e lease e la verifica dell'origin (A-17). Il messaggio in coda
porta i campi che dipendono dal comando; quelli per destinatario li aggiungerà il dispatcher all'invio.

**Scostamento dichiarato.** §10.1 vuole `202` con il riepilogo per **ogni** comando di regia. Oggi lo
fa solo `ShowWaiting`, perché è l'unico che scrive consegne: gli altri (`StartSession`, `Pause`,
`Resume`, `EndSession`, `OpenAdmissions`, `CloseAdmissions`) non toccano ancora lo stato desiderato dei
partecipanti e restano `200` con il `SessionDto`. Diventeranno `202` quando scriveranno anche loro il
proprio effetto sui partecipanti — è il passo che chiude la semantica di §6.5.

| Controllo eseguito dall'agente | Esito |
|---|---|
| `dotnet build .\Qwerty.Codi.slnx -c Release` | Uscita 0, **0 warning e 0 errori** |
| `dotnet test` Release | 286 dominio + 654 integrazione = **940 test**, 0 falliti (erano 936) |
| Copertura contro baseline (`TestResults/t4-p1-20261002`) | Api 87,98% · Application 97,89% · Domain 95,92% · Infrastructure 97,01%, tutte sopra baseline |
| `test-coverage-gate.ps1` | **4/4** regressioni superate |
| `dotnet format whitespace --verify-no-changes` | Uscita 0 (dopo aver normalizzato a LF i 15 file nuovi, migrazione compresa) |
| Migrazione su database dei test | Il catalogo applica la `0007` su un database creato dalla `0006` senza ricreazione; la lista esatta delle tabelle dello schema è aggiornata |

Nessuna verifica nel browser: non esiste ancora una vista admin che invochi `ShowWaiting` né un client
che riceva il messaggio. Le porte 5200, 4200 e 4300 dell'utente non sono state toccate.

## T3, quinta porzione: il primo stato desiderato comandato — 2026-10-02

**VALIDATO localmente.** `ShowWaiting` è il primo comando che cambia lo **stato desiderato** della
classe: porta il default di classe alla vista di attesa e, nella stessa transazione, sospende lo
stato materializzato **di ogni partecipante** della sessione. I cataloghi passano a **35 comandi e
18 query**; nessuna tabella nuova — `participant_state` esisteva dalla seconda porzione, che l'aveva
creata come copia del default all'ingresso e non ancora comandata.

Il comando è ammesso da qualunque stato non terminale, `Lobby` compreso, come `EndSession`: **non**
cambia lo stato della sessione, quindi il lavoro sospeso non si perde. Verifica proprietà, revisione
attesa e stato non terminale prima della transazione, come gli altri comandi di regia; lettura e
scrittura stanno nella stessa transazione, così nessun alunno resta con la vista precedente e il
controllo di revisione non ha finestre. La vista desiderata si **rilegge dal documento** appena
scritto (`ParticipantClassDefault.ReadView`) invece di ripetere la costante, così i due valori non
possono divergere. Gli stati si leggono dal repository per sessione e non dai partecipanti uno per
uno: è la tabella dello stato desiderato a dire chi c'è, e l'ingresso crea la riga nella stessa
transazione (§9.4).

L'effetto è osservabile dal server: dopo il comando, `GetParticipantContext` dell'alunno riporta
`Waiting`, e `interactionEnabled` è tornato a zero. Poiché non esiste ancora l'outbox, questa
porzione **non distribuisce** il comando ai client connessi: la vista cambia per il server e per chi
la rilegge, mentre la consegna ai player è la regia realtime di T4.

**Scostamento dichiarato.** Il riferimento al lavoro sospeso (`resumeContextJson` di §6.2) non viene
scritto: assegnazioni e tentativi non esistono, quindi non c'è un lavoro da riferire. Arriverà con
`AssignExercise`. Il comando dichiara inoltre il solo ambito `Class`: il campo `target` di §6.5
arriva con i comandi individuali, come già per `Pause` e `Resume`.

| Controllo eseguito dall'agente | Esito |
|---|---|
| `dotnet build .\Qwerty.Codi.slnx -c Release` | Uscita 0, **0 warning e 0 errori** |
| `dotnet test` Release | 286 dominio + 650 integrazione = **936 test**, 0 falliti (erano 926) |
| Copertura contro baseline (`TestResults/t3-p6-20261002`) | Api 87,94% · Application 97,82% · Domain 95,92% · Infrastructure 96,89%, tutte sopra baseline |
| `test-coverage-gate.ps1` | **4/4** regressioni superate |
| `dotnet format whitespace --verify-no-changes` | Uscita 0 (dopo aver normalizzato a LF il file di test nuovo) |

Nessuna verifica nel browser: non esiste ancora un comando nella regia admin che lo invochi né un
client che lo riceva, quindi la porzione si ferma al caso d'uso e ai suoi test. Le porte 5200, 4200 e
4300 dell'utente non sono state toccate.

## T3, quarta porzione: il trasporto degli hub — 2026-10-02

**VALIDATO localmente.** Esistono i due hub di §11.1, con schemi di autenticazione distinti e
gruppi decisi dal server. Nessun comando o query nuovo — i cataloghi restano **34 comandi e 18
query** — e nessuna tabella: questa porzione è trasporto, e il trasporto non cambia il modello.

`/hubs/v1/participant` e `/hubs/v1/teacher` sono mappati con `AddSignalR` e i limiti di §11.5
configurati esplicitamente (32 KiB in ingresso, keepalive 5 s, timeout del client 20 s). Lo schema
di area `CodiArea`, che sceglie la credenziale dal percorso, ora instrada i percorsi degli hub come
l'area corrispondente: `/hubs/v1/participant` usa il cookie dell'alunno, `/hubs/v1/teacher` quello
del docente. Gli hub dichiarano `[Authorize]`, quindi la negoziazione di una connessione senza la
credenziale dell'area risponde `401` — la stessa separazione dei due cookie che vale per gli
endpoint (§9.3), verificata sui due percorsi incrociati.

`ParticipantHub` non contiene metodi in questa porzione: all'apertura risolve il contesto dalla
credenziale — ripetendo la verifica, perché un cookie può essere revocato dopo l'handshake (§9.2) —
e solo una partecipazione ammessa riceve il gruppo `participant:{id}`; ogni altra condizione chiude
la connessione. `TeacherHub` espone `ObserveSession(sessionId)`: esegue la query `GetSession`
attraverso il dispatcher, quindi con lo stesso registro, la stessa area e la stessa proprietà della
risorsa dell'HTTP (§11.7), e associa la connessione al gruppo `session:{id}:teachers` solo se la
lettura riesce. Un hub autenticato non diventa onnipotente: la prova con la sessione di un altro
amministratore riceve `404 ResourceNotFound` dal caso d'uso, con il gruppo non associato.

Canale nuovo `CodiRequestChannel.Hub`: distingue le richieste che arrivano da un metodo hub da
quelle HTTP, senza cambiare la verifica della policy. Restano fuori dalla porzione, con il buco
registrato: epoch della connessione, lease, ACK, outbox, istantanea e telemetria — sono la regia
realtime di §12 (T4), e scriverli senza outbox significherebbe consegnare stato che nessuno
consegna; `CodiRequestChannel.Hub` è la base su cui quei comandi passeranno.

**Vincolo per il client.** La negoziazione è una `POST` autenticata con cookie, quindi resta sotto
l'antiforgery di §10.2: il client SignalR dovrà inviare `X-Codi-Antiforgery` con il token emesso dal
bootstrap dell'area, come già fa per il dispatcher. È un vincolo del trasporto, non dell'hub, e va
tenuto presente quando si scriverà il client (T7).

**Deroga dichiarata.** La verifica dell'**origin** della connessione (§11.2, passo 2) non è ancora
implementata. Per l'HTTP l'app non ha CORS e si affida a due cookie `__Host-` con `SameSite=Strict`,
che il browser non invia sulle richieste cross-site; gli hub ereditano la stessa protezione del
trasporto, ma §11.2 chiede un controllo esplicito, che va chiuso con l'handshake di T4. È registrato
come A-17.

Un difetto trovato dall'esecuzione completa della suite, non dal controllo statico: `AddSignalR`
registra il gestore delle connessioni, che dipende da `IHostApplicationLifetime`; le due
composizioni di test che costruiscono un `ServiceProvider` senza host
(`CodiApiCompositionTests`, `AdminAccountSeedTests`) fallivano con `ValidateOnBuild` nominando un
servizio SignalR interno. La correzione è nel test — quelle composizioni forniscono ora il ciclo di
vita dell'applicazione, come farebbe `WebApplicationBuilder` — non nel prodotto.

| Controllo eseguito dall'agente | Esito |
|---|---|
| `dotnet build .\Qwerty.Codi.slnx -c Release` | Uscita 0, **0 warning e 0 errori** |
| `dotnet test` Release | 286 dominio + 640 integrazione = **926 test**, 0 falliti (erano 916; 10 nuovi sugli hub) |
| Copertura contro baseline (`TestResults/t3-p5-20261002`) | Api 87,94% · Application 97,88% · Domain 95,89% · Infrastructure 96,89%, tutte sopra baseline |
| `test-coverage-gate.ps1` | **4/4** regressioni superate |
| `dotnet format whitespace --verify-no-changes` | Uscita 0 (dopo aver normalizzato a LF i 4 file nuovi di `Hubs` e i 4 di `Realtime`) |

Nessuna verifica nel browser: non esiste ancora un client SignalR — il frontend admin e quello degli
alunni non aprono connessioni — quindi la porzione si ferma al trasporto e ai suoi test. Le porte
5200, 4200 e 4300 dell'utente non sono state toccate.

## T3, terza porzione: uscita volontaria e regia dei partecipanti — 2026-10-02

**VALIDATO localmente.** L'alunno esce da solo e il docente governa gli ingressi e i partecipanti.
I cataloghi passano a **34 comandi e 18 query**; nessuno schema nuovo, nessun endpoint oltre a
`POST /api/v1/public/participant/leave`.

`LeaveSession` (§9.2, §10.3, policy `AdmittedParticipant`) è l'uscita volontaria: revoca la
credenziale corrente e **non cancella nulla** — la partecipazione resta ammessa e il lavoro non
viene toccato, così il maestro continua a vedere chi c'era. L'identità arriva dal contesto
verificato dal trasporto, quindi il corpo è vuoto e un alunno non può far uscire un altro; la
credenziale revocata non si riapre con una ripetizione (§9.4), perché la revoca è una decisione e
non una risposta persa. L'endpoint dedicato esegue il comando e cancella il cookie del partecipante
e il token antiforgery legato all'identità uscente; la revoca vale anche per una connessione già
aperta, perché la credenziale è riletta a ogni richiesta.

`OpenAdmissions` e `CloseAdmissions` (§6.1, §6.4) cambiano soltanto `admissionsOpen` con la
revisione attesa: non toccano lo stato della sessione, restano disponibili per riaprire o chiudere
gli ingressi dopo l'avvio e sono idempotenti — ripetere l'operazione su uno stato già raggiunto
restituisce lo stato corrente senza scrivere e senza alzare la revisione. In `Ended` sono rifiutati
con `409 SessionStateConflict`.

`UpdateParticipant` e `ExcludeParticipant` (§10.4, §9.5) modificano **una riga esistente**, quindi
dichiarano la versione attesa della riga (`baseRowVersion`): `428` se assente, `412` se superata
(§10.2). `UpdateParticipant` corregge nome e postazione — una postazione dichiarata deve esistere
nella classe della sessione — mentre `ExcludeParticipant` porta l'ammissione a `Excluded` senza
cancellare la riga ed è idempotente. Modificano la sola riga del partecipante e **non** incrementano
`sessionRevision`: non sono comandi di stato della sessione, e §6.4 elenca la revisione attesa per i
comandi che cambiano lo stato desiderato o lo stato della sessione (decisione locale registrata qui).
La proprietà della sessione e l'appartenenza del partecipante si verificano in un punto condiviso
(`SessionParticipantRegiaRules`): una sessione altrui o un partecipante di un'altra sessione è un
`404`, indistinguibile da una risorsa inesistente (D3, installazione a docente singolo).

Restano fuori da questa porzione, con il buco registrato: lo **stato desiderato comandato**
(`ShowWaiting`, `AssignExercise`, `EnableActivity`, `RunProgram`, `RestartExecution`, `ResetWork`),
che il commento di `ParticipantStateEntity` dichiara di T4 perché trasforma `participant_state` —
qui la riga si crea soltanto — e che senza assegnazioni, tentativi e outbox non produrrebbe un
effetto osservabile; il **trasferimento con codice monouso** di §9.5, estensione dell'infrastruttura
(finché non c'è, il recupero su altro PC è la correzione assistita di `UpdateParticipant`);
**hub/snapshot/presenze**, che richiedono SignalR e i percorsi degli hub nella selezione dell'area
`CodiArea`; l'**outbox** e le consegne (T4); la **UI degli alunni**, che non esiste ancora. Le query
della griglia (`ListSessionParticipants`, `GetParticipantDetail`) restano di T4: §10.5 chiede
l'«ultimo stato osservato», che è telemetria in memoria e non esiste senza gli hub.

| Controllo eseguito dall'agente | Esito |
|---|---|
| `dotnet build .\Qwerty.Codi.slnx -c Release` | Uscita 0, **0 warning e 0 errori** |
| `dotnet test` Release | 905 dominio + integrazione = **916 test**, 0 falliti (erano 870) |
| Copertura contro baseline (`TestResults/t3-p3-20261002b`) | Api 87,48% · Application 97,88% · Domain 95,89% · Infrastructure 96,89%, tutte sopra baseline |
| `test-coverage-gate.ps1` | **4/4** regressioni superate |
| `dotnet format whitespace --verify-no-changes` | Uscita 0 |
| Fine riga di `src` e `tests` | 0 file non conformi (2 file nuovi, tutti normalizzati a LF) |

Un difetto delle prime due porzioni è emerso qui: `Application` era scesa di **0,01%** sotto la
baseline con i soli percorsi positivi, perché i rifiuti dei comandi nuovi — sessione inesistente,
credenziale già revocata, credenziale di un altro partecipante — non erano esercitati. Sono stati
coperti con nuovi test, non abbassando la soglia: `Regia_SuSessioneInesistente_...` per i cinque
comandi di regia, `Uscita*` per i rami di `LeaveSession`, `*_SuSessioneInesistente_...` per la regia
dei partecipanti. La misura finale è 97,88%, sopra la baseline.

Nessuna verifica nel browser: la porzione non aggiunge interfaccia — l'app public ha ancora la sola
pagina di benvenuto — e gli endpoint non sono ancora consumati da una vista. Le porte 5200, 4200 e
4300 dell'utente non sono state toccate.

## T3, seconda porzione: ingresso idempotente e credenziale del partecipante — 2026-10-02

**VALIDATO localmente.** L'alunno entra: il ticket di ammissione, la chiave di idempotenza, la
credenziale e il contesto. I cataloghi passano a **29 comandi e 18 query** — `JoinSession` non è più
un nome senza tipo, quindi l'elenco chiuso di `AnonymousBootstrap` è completo — e una migrazione
`0006` aggiunge le due tabelle che il join scrive.

`JoinSession` (§9.4) verifica la catena scelta dal client, crea **partecipazione, stato iniziale e
credenziale in una sola transazione**, registra l'esito e restituisce ciò che serve al trasporto per
emettere il cookie. Il corpo del comando non dichiara né lo scope né la chiave: li legge il
trasporto, dal ticket di ammissione (`__Host-CodiAdmission`, due ore, consumato dall'ingresso) e
dall'intestazione `Idempotency-Key`, e li passa nel contesto verificato. `GetParticipantContext`
legge la partecipazione **dalla sola credenziale** e per questo la lettura del contesto di un altro
alunno non è un'operazione che esista.

Due porte per lo stesso comando, come §10.1 ammette: l'endpoint dedicato `POST
/api/v1/public/join`, che è quello che l'app usa perché il trasporto emette il cookie, e il
dispatcher generico dell'area pubblica. Entrambe eseguono lo stesso caso d'uso con lo stesso
contesto; la risposta dedicata è un profilo minimo — `displayName`, `participantId`, `sessionId`,
`view` — e non contiene la marca di sicurezza, che entra nel cookie.

L'**area public** ha ora un secondo schema di autenticazione: cookie `__Host-CodiParticipant`,
schema `CodiArea` che instrada al partecipante i percorsi `/api/v1/public` e all'admin tutti gli
altri. I due cookie possono convivere nello stesso browser senza fondersi: la credenziale docente
non autentica l'area pubblica e viceversa.

Scelte locali, registrate in §7.4, §7.5, §9.4 e §24: il registro di idempotenza si consulta
**prima** dello stato della sessione, così una risposta persa si recupera anche se nel frattempo
gli ingressi si sono chiusi, mentre una credenziale **revocata** non si riapre con una ripetizione;
lo scope conservato è l'**impronta** del ticket e mai il ticket; un browser che presenta una
credenziale di partecipante non ripete l'ingresso (`403 SessionPermissionDenied`), perché l'ingresso
è ammesso solo a un contesto anonimo con ticket valido; la postazione si risolve dall'impronta della
chiave casuale del browser e resta **facoltativa**; `participant_state` nasce come **copia del
default di classe** alla revisione corrente della sessione.

Restano fuori da questa porzione, con il buco registrato: l'uscita volontaria
`POST /api/v1/public/participant/leave` di §10.3, che **non ha un nome nel catalogo** (proposta:
un comando `LeaveSession` con policy `AdmittedParticipant`); i percorsi degli hub di §11, che
andranno aggiunti alla selezione dell'area quando gli hub esisteranno; la postazione associata o
spostata dal docente (§9.5) e la UI degli alunni, che non esiste ancora.

| Controllo eseguito dall'agente | Esito |
|---|---|
| `dotnet build .\Qwerty.Codi.slnx -c Release` | Uscita 0, **0 warning e 0 errori** |
| `dotnet test` Release | 286 dominio + 584 integrazione = **870 test**, 0 falliti (erano 817) |
| Copertura contro baseline (`TestResults/t3-p2-final`) | Api 86,94% · Application 97,95% · Domain 96,22% · Infrastructure 96,89%, tutte sopra baseline |
| `test-coverage-gate.ps1` | **4/4** regressioni superate |
| `dotnet format whitespace --verify-no-changes` | Uscita 0 |
| Fine riga di `src` e `tests` | 0 file non conformi (22 file nuovi o modificati normalizzati a LF) |
| Migrazione su database esistente | Il database di collaudo allo schema 5 è passato allo **schema 6** all'avvio, senza ricreazione |
| Collaudo del processo reale (porta 5310) | Registro all'avvio: «29 comandi, 18 query, 0 richieste anonime non ancora implementate»; bootstrap pubblico `200`; `POST /api/v1/public/join` `200` con profilo minimo e nome ripulito (`"  Luca  "` → `Luca`); `GET /api/v1/public/auth/me` `200` con identità, ammissione, vista e stato della sessione; ripetizione da un browser con credenziale `403 SessionPermissionDenied` |

Un difetto trovato dai test e corretto: `CodiRequestContext.Grants` non aveva lettori, quindi è stato
**rimosso** invece di essere coperto da una prova fittizia. La prima misura di copertura era sotto la
baseline su `Application` (97,48% contro 97,59%): le righe scoperte erano i rifiuti del codice nuovo
— credenziale revocata, catena archiviata, default di classe illeggibile, stato di ammissione fuori
contratto — e sono state coperte con nuovi test, non abbassando la soglia.

Nessuna verifica nel browser: la porzione non aggiunge interfaccia, e l'app public ha ancora la sola
pagina di benvenuto. Le porte 5200, 4200 e 4300 dell'utente non sono state toccate; l'istanza di
prova ha usato la porta 5310 e il database temporaneo `TestResults/collaudo-t6-20261002`.

## T3, prima porzione: partecipanti e postazioni — 2026-10-02

**VALIDATO localmente.** L'aula live comincia dai dati: chi entra in una sessione e da quale
postazione. Nessun comando o query nuovo — i cataloghi restano **28 comandi e 17 query** — perché
questa porzione mette a terra schema e dominio che il join userà in quella successiva.

Migrazione `0005`, due tabelle. `station` (§7.2) conserva l'**impronta** di una chiave casuale del
browser — mai un'impronta digitale del dispositivo — la classe in cui la postazione è stata vista e
l'ultimo contatto; l'impronta è unica, perché una chiave rigenerata identifica una postazione nuova.
`session_participant` (§7.4) è l'identità dell'alunno **nella sessione**: nome visualizzato,
ammissione, epoca della connessione operativa, ingresso ed esclusione. L'unicità `(session_id, id)`
esiste per le chiavi esterne composte delle tabelle che seguiranno (stato desiderato, tentativi),
non per sé stessa.

Dominio: `SessionParticipant` (`Create`, `Restore`, `Exclude` ripetibile senza effetti),
`ParticipantName` — il limite di §7.4 è di **30 grafemi**, quindi un'emoji composta vale uno e una
lettera accentata scomposta resta una lettera — e `AdmissionStatus`. Persistenza: entità, due
repository, mapping RepoDB e registrazione nella unit of work.

Scelte locali, registrate in §7.2, §7.4 e §24: le tabelle di questa porzione sono due e non quattro
— lo stato desiderato di §6.2 e il registro di idempotenza di §7.5 arrivano con il join, che è il
primo a scriverli; `credential_session.participant_id` resta **senza chiave esterna**, perché
aggiungerla richiederebbe di ricostruire una tabella della `0001` e la coerenza la verifica
l'applicazione; i valori di `admission_status` sono `Admitted` ed `Excluded`; il CHECK sul nome
accetta fino a 120 caratteri come rete di sicurezza, mentre il limite sui grafemi — che il database
non sa contare — resta dell'applicazione.

La porzione approvata «dati e identità» è stata divisa in due per avere due consegne verificabili
invece di una sola a metà: qui i dati e il dominio, con il join e la credenziale del partecipante
nella porzione successiva.

| Controllo eseguito dall'agente | Esito |
|---|---|
| `dotnet build .\Qwerty.Codi.slnx -c Release` | Uscita 0, **0 warning e 0 errori** |
| `dotnet test` Release | 286 dominio + 531 integrazione = **817 test**, 0 falliti (erano 763) |
| Copertura contro baseline (`TestResults/t3-p1-20261002`) | Api 84,54% · Application 97,64% · Domain 96,46% · Infrastructure 96,63%, tutte sopra baseline |
| `test-coverage-gate.ps1` | **4/4** regressioni superate |
| `dotnet format whitespace --verify-no-changes` | Uscita 0 |
| Fine riga di `src` e `tests` | 0 file non conformi (13 file nuovi, tutti normalizzati a LF) |
| Migrazione su database esistente | Il database di collaudo allo schema 4 è passato allo **schema 5** all'avvio e l'API ha risposto; nessuna ricreazione |

Nessuna verifica nel browser: la porzione non aggiunge interfaccia né rotte. Le porte 5200 e 4200
dell'utente non sono state toccate; l'istanza di prova ha usato la porta 5310 e il database
temporaneo `TestResults/collaudo-t6-20261002`.


## T6, chiusura dei residui: note dell'autore, anteprima di versione e 404 previsto — 2026-10-02

**VALIDATO localmente e nel browser.** I quattro residui dichiarati con la quarta porzione sono
chiusi: tre con una modifica, uno ricondotto al suo incremento.

1. **L'avviso `ResourceNotFound` su una bozza assente è chiuso.** Era un difetto di interfaccia: il
   `404` con cui il server risponde legittimamente a «questa bozza non esiste ancora» veniva
   annunciato come un guasto, e l'avviso restava accanto alla nota che spiegava il caso. Ora è **chi
   chiama** a dichiarare che l'assenza è una risposta prevista: un'opzione di trasporto
   (`EXPECTED_NOT_FOUND` in `libraries/api/api-service.ts`) che `ApiService` scrive nella richiesta e
   l'intercettore delle notifiche onora **solo per il `404`** — un guasto vero continua ad avvisare, e
   il rifiuto continua a raggiungere chi ha chiesto la risorsa. L'editor la passa per
   `GetExerciseDraft` e `GetReferenceSolution`. Il dominio non entra nel trasporto, i due metodi del
   dispatcher restano due e la dichiarazione è locale alla chiamata.
2. **Le note dell'autore si scrivono e si leggono.** Il pannello Pubblicazione ha il campo «Note
   dell'autore (facoltative)», che viaggia in `PublishExerciseVersion.authorNotes`; le versioni
   pubblicate hanno la colonna **Note**. `ExerciseVersionDto.AuthorNotes` arriva dalla riga compagna
   `exercise_version_private` ed è l'unico contenuto privato che entra in un DTO, **solo nella
   lettura admin** (`PublishExerciseVersion`, `ListExerciseVersions`, `GetExerciseVersion`); la
   soluzione di riferimento resta fuori (§13.1). Le note restano legate alla versione, che è
   immutabile: per cambiarle se ne pubblica una nuova (§13.8).
3. **Una versione pubblicata si può guardare.** «Guarda la versione» apre l'anteprima in **sola
   lettura** del progetto iniziale pubblicato, con il motore isolato dell'autore: non tocca la bozza,
   non copia nulla e non registra esiti. Serviva perché l'unico modo per rivedere una versione era
   copiarla nella bozza, cioè sostituire il lavoro in corso. Se la definizione non è leggibile con il
   formato corrente, la pagina lo dice e non esegue nulla; «Copia nell'editor» resta la via per
   riprenderla.
4. **La versione fissata alla sequenza appartiene all'assegnazione, non all'autore.** `activity_assignment`
   porta `exercise_version_id` con «Versione esercizio fissata all'assegnazione; non cambia
   modificando la sequenza» (§7.3/§10.4): il compito è di `AssignExercise`, che non esiste ancora
   (T4). La sequenza riferisce correttamente l'esercizio: non è un residuo di T6 e non si chiude qui.

Nessun comando o query nuovo: i cataloghi restano **28 comandi e 17 query**. Documenti aggiornati:
§10.5 (le due letture delle versioni riportano le note), §13.8 e §14.2 (dichiarazione di un `404`
previsto), [matrice di copertura](../docs/TEST-COVERAGE-MATRIX.md), [form admin](../docs/ADMIN-SIGNAL-FORMS.md).

| Controllo eseguito dall'agente | Esito |
|---|---|
| `dotnet build .\Qwerty.Codi.slnx -c Release` | Uscita 0, **0 warning e 0 errori** |
| `dotnet test` Release | 255 dominio + 508 integrazione = **763 test**, 0 falliti (erano 762) |
| Copertura contro baseline (`TestResults/t6-chiusura-20261002`) | Api 84,54% · Application 97,69% · Domain 96,22% · Infrastructure 96,48%, tutte sopra baseline |
| `test-coverage-gate.ps1` | **4/4** regressioni superate |
| `dotnet format whitespace --verify-no-changes` | Uscita 0 |
| Fine riga di `src` e `tests` | 0 file non conformi |
| `npm run format:check`, `lint`, `build` | Uscite 0 |
| `npm run test -- --watch=false` | **228 test** (226 admin/librerie + 2 public), 0 falliti (erano 222) |

**Collaudo nel browser** (2 ottobre 2026) sulla stessa istanza di collaudo della pubblicazione (API
5310, admin 4310, database temporaneo `TestResults/collaudo-t6-20261002/aula-collaudo.db`; i servizi
dell'utente sulle porte 5200 e 4200 non sono stati toccati). Percorso eseguito: creazione
dell'esercizio «Chiusura T6 — note e anteprima» e apertura del suo editor **senza alcun avviso**,
mentre il registro dell'API mostra `GetExerciseDraft` rifiutata con `ResourceNotFound`; salvataggio
della bozza; note scritte e **versione 1 pubblicata**, con le note visibili nella colonna **Note**;
«Guarda la versione» che apre l'anteprima del progetto iniziale pubblicato (griglia 5×5, partenza in
alto a sinistra, traguardo in basso a destra) e dichiara «Il progetto è vuoto» perché la versione
pubblicata è vuota davvero; cella (3,3) resa ostacolo nella bozza, con l'anteprima della versione che
continua a mostrarla **libera** mentre la griglia dell'editor la mostra ostacolo: la prova che
l'anteprima legge la versione e non la bozza; «Copia nell'editor» accanto al nuovo comando, che
riporta la bozza e annuncia «Bozza riportata alla versione copiata».

## T6, quarta porzione: pubblicazione delle versioni e soluzione privata — 2026-10-02

**VALIDATO localmente e nel browser.** L'autore degli esercizi si chiude: la bozza si verifica
senza pubblicarla, si pubblica una versione immutabile, si leggono le versioni e la soluzione
privata, e una versione si riporta nella bozza. Cataloghi aggiornati: **28 comandi e 17 query**
(erano 25 e 14).

Casi d'uso: `ValidateExerciseDraft` (elenco dei problemi che impediscono la pubblicazione, senza
scrivere nulla), `PublishExerciseVersion`, `CopyExerciseVersionToDraft`, `GetReferenceSolution`,
`ListExerciseVersions` e `GetExerciseVersion`. La versione porta l'impronta del contenuto
(`ExerciseContentHash`: SHA-256 della definizione canonica, del progetto e delle versioni del
motore) e il numero progressivo per esercizio; la soluzione privata resta in
`exercise_version_private` e non compare in nessun DTO. Nell'admin la pagina dell'esercizio ha la
sezione della **soluzione di riferimento** (secondo workspace Blockly con la sua anteprima) e il
pannello **Pubblicazione**: verifica, pubblicazione e versioni pubblicate con copia nella bozza.

Tre scelte locali, registrate anche in §13.8: la chiave idempotente è il **contenuto** — stessa
impronta, stessa soluzione privata e stesse note restituiscono la versione esistente, anche da
client diversi — quindi non serve una colonna nuova e non c'è migrazione; la **soluzione di
riferimento** pubblicata deve avere il punto di ingresso (una soluzione che non parte non verifica
nulla), mentre il progetto iniziale può restare vuoto o parziale; le **note dell'autore**
appartengono a una versione immutabile, quindi aggiungerle dopo richiede una versione nuova. Il
comando di pubblicazione è spento finché ci sono modifiche non salvate: pubblica la revisione che è
sul server, non ciò che è a schermo.

| Controllo eseguito dall'agente | Esito |
|---|---|
| `dotnet build .\Qwerty.Codi.slnx -c Release` | Uscita 0, **0 warning e 0 errori** |
| `dotnet test` Release | 255 dominio + 507 integrazione = **762 test**, 0 falliti (erano 739) |
| Copertura contro baseline (`TestResults/t6-chiusura`) | Api 84,54% · Application 97,68% · Domain 96,22% · Infrastructure 96,48%, tutte sopra baseline |
| `test-coverage-gate.ps1` | **4/4** regressioni superate |
| `dotnet format whitespace --verify-no-changes` | Uscita 0, dopo aver normalizzato a LF 3 file nuovi |
| Fine riga di `src` e `tests` | 0 file non conformi |
| `npm run format:check`, `lint`, `build` | Uscite 0 |
| `npm run test -- --watch=false` | **222 test** (220 admin/librerie + 2 public), 0 falliti (erano 209) |

**Collaudo nel browser** (2 ottobre 2026) sulla stessa istanza di collaudo del progetto a blocchi
(API 5310, admin 4310, database temporaneo `TestResults/collaudo-t6-20261002/aula-collaudo.db`; i
servizi dell'utente sulle porte 5200 e 4200 non sono stati toccati). Percorso eseguito: **Verifica
la bozza** che risponde «La bozza è pubblicabile» senza comporre una soluzione; **Pubblica la
versione** che crea la versione 1; ripetizione della stessa pubblicazione che **non** crea una
seconda versione (l'elenco resta a una riga); modifica della consegna, salvataggio e pubblicazione
che crea la **versione 2** con un'impronta diversa, in elenco dalla più recente con data, motore e
impronta; **Copia nell'editor** della versione 2 che riporta la bozza ai suoi documenti con
l'annuncio «Bozza riportata alla versione copiata». Il registro dell'API conferma la sequenza
`ValidateExerciseDraft` → `PublishExerciseVersion` → `ListExerciseVersions` →
`CopyExerciseVersionToDraft`.

Il collaudo ha trovato **tre difetti** che i test locali non coprivano, tutti corretti: il campo
della data usciva come `createdAt` mentre il client leggeva `createdAtUtc`, e la tabella delle
versioni si rompeva con `RangeError: Invalid time value`; la verifica riceveva il progetto vuoto
come soluzione di riferimento, così una bozza senza soluzione veniva segnalata come non
pubblicabile; la copia di una versione non lasciava alcun messaggio perché la rilettura della bozza
ricreava il pannello. I primi due hanno ora una prova di regressione nelle rispettive suite.

Limiti dichiarati: la **sequenza di una lezione** riferisce ancora l'identità dell'esercizio e non
una versione precisa, quindi l'assegnazione resta il prossimo passo; le **note dell'autore**
esistono nel contratto, nella tabella e nei test del server, ma il pannello non le chiede; l'avviso
`ResourceNotFound` accanto alla nota di bozza assente è ancora aperto; la composizione dei blocchi
trascinando con il mouse non è automatizzata dall'agente (la forma del progetto è coperta dalla
prova di contratto lato frontend).

> **Superato dalla chiusura dei residui** (sezione in cima): le note dell'autore si scrivono e si
> leggono, l'avviso `ResourceNotFound` su un'assenza prevista è chiuso e una versione pubblicata si
> può guardare. Resta dichiarato che **fissare la versione a una sequenza è l'assegnazione**
> (`activity_assignment.exercise_version_id`, T4), non un lavoro dell'autore; la composizione dei
> blocchi con il mouse resta non automatizzata, come dichiarato.

## T6, terza porzione: il progetto a blocchi e l'anteprima dell'autore — 2026-10-02

**VALIDATO localmente e nel browser.** Il progetto a blocchi si compone in Blockly dentro l'editor,
il salvataggio lo verifica e l'anteprima lo esegue con il motore `gridpath-1` in stato isolato.
Nessun nome di richiesta nuovo: il catalogo resta a **25 comandi e 14 query**.

**Server** (§13.3): `ExerciseWorkspaceJson` verifica dimensione, profondità, numero di blocchi, tipi,
campi e collegamenti **prima** di conservare la bozza, sia per il progetto iniziale sia per la
soluzione di riferimento. Il progetto è conservato com'è stato ricevuto: il formato è di Blockly e
riscriverlo significherebbe interpretarlo. Scelte locali: la mancanza di `aula_start` **non**
impedisce il salvataggio (§13.4 la lega alla prova, non alla scrittura); i metadati di presentazione
di Blockly (`x`, `y`, stato di riduzione) sono ignorati; i campi del catalogo sono `TURN`
(`Left`/`Right`), `COUNT` (1–20) e l'ingresso `DO`, dichiarati in `ExerciseBlockCatalog` e condivisi
con le definizioni dei blocchi.

**Libreria** `libraries/exercise-engine`: `grid-path-scene.ts` (tipi e regole di passo e rotazione),
`grid-path-blocks.ts` (i quattro blocchi e il cassetto costruito dai blocchi ammessi),
`grid-path-program.ts` (workspace → istruzioni, con diagnosi leggibili; i blocchi scollegati sono
contati e non eseguiti) e `grid-path-runner.ts` (interprete a frame: il traguardo conclude la corsa,
collisione e uscita dalla griglia mantengono la posizione, i cicli non si espandono e consumando
budget restano limitati, la durata attiva usa un orologio iniettabile). L'area docente ha
`grid-path-program-editor` (montaggio, cassetto, evidenziazione del blocco in esecuzione) ed
`exercise-preview` («Avvia la prova», «Passo avanti», «Ferma», «Ricomincia», celle con etichette in
parole); la pagina dell'esercizio compone entrambi e confronta il progetto con quello salvato per
decidere se c'è qualcosa da salvare.

| Controllo eseguito dall'agente | Esito |
|---|---|
| `dotnet build .\Qwerty.Codi.slnx -c Release` | Uscita 0, **0 warning e 0 errori** |
| `dotnet test` Release | 255 dominio + 484 integrazione = **739 test**, 0 falliti |
| Copertura contro baseline (`TestResults/t6-finale`) | Api 84,54% · Application 97,65% · Domain 96,22% · Infrastructure 96,48%, tutte sopra baseline |
| `test-coverage-gate.ps1` | **4/4** regressioni superate |
| `dotnet format whitespace --verify-no-changes` | Uscita 0 |
| Fine riga di `src` e `tests` | 0 file non conformi |
| `npm run format:check`, `lint`, `build` | Uscite 0 |
| `npm run test -- --watch=false` | **209 test** (207 admin/librerie + 2 public), 0 falliti |

**Collaudo nel browser** (2 ottobre 2026) su **database temporaneo**
(`TestResults/collaudo-t6-20261002/aula-collaudo.db`), con un'istanza API sulla porta 5310 e un dev
server dell'admin sulla 4310: i servizi dell'utente sulle porte 5200 e 4200 non sono stati toccati.
Il collaudo ha eseguito il percorso reale: accesso docente; creazione di un esercizio; bozza salvata
con la **serializzazione reale di Blockly** (risposta `200` dal dispatcher); ricarica dell'editor con
il progetto reso da Blockly (ingresso → ripetizione di tre passi → rotazione, senza avvisi di
lettura); **«Avvia la prova»** che porta il personaggio su «Riga 1, colonna 4: traguardo» e mostra il
messaggio della definizione «Hai raggiunto la stella!»; modifica della consegna e salvataggio dalla
pagina con l'esito «Bozza salvata.». Il registro dell'API conferma la sequenza
`SaveExerciseDraft` → `GetExerciseDraft` → `SaveExerciseDraft` completate.

Limiti dichiarati: **mancano la pubblicazione delle versioni** (`ValidateExerciseDraft`,
`PublishExerciseVersion` idempotente, `ListExerciseVersions`, `GetExerciseVersion`,
`CopyExerciseVersionToDraft`) e la soluzione privata in interfaccia (`GetReferenceSolution`); la
composizione dei blocchi **trascinando** con il mouse non è stata automatizzata dall'agente — la
forma del progetto prodotto da Blockly è verificata dalla prova di contratto lato frontend e il
percorso salva/ricarica/esegui è stato provato nel browser. L'avviso `ResourceNotFound` accanto alla
nota di bozza assente si è ripresentato nel collaudo (tre richieste rifiutate nel registro dell'API):
resta il difetto di interfaccia già annotato nella porzione precedente.

> **Superato dalla quarta porzione** (sezione in cima): pubblicazione, versioni, verifica della bozza
> e soluzione privata **esistono** e sono validati. L'avviso `ResourceNotFound` e le note dell'autore
> in interfaccia sono chiusi dalla **chiusura dei residui**; la versione fissata alla sequenza è
> dell'assegnazione (T4).

## T6, seconda porzione: la bozza si salva e l'editor la configura — 2026-10-02

**VALIDATO localmente e nel browser.** I due casi d'uso della bozza e la pagina di editor. Cataloghi
aggiornati: **25 comandi e 14 query** (erano 24 e 13), perché l'autoraggio di T6 entra in §10.4–§10.5.

In `Application/Exercises`: `ExerciseDefinitionJson` (definizione ↔ documento di §13.2, con i rifiuti
distinti fra `InvalidPayload` e `ValidationFailed`), `ExerciseDraftDto`, `SaveExerciseDraftCommand` e
`GetExerciseDraftQuery` con i loro handler, policy `Admin`. La scrittura verifica i documenti —
definizione, progetto a blocchi, soluzione privata — e memorizza la definizione **canonica**, cioè
quella prodotta dalla verifica e non il testo ricevuto. Il primo salvataggio crea la bozza; i
successivi richiedono la versione: `428` se manca, `412` se superata o se dichiarata su una bozza che
non esiste.

Nell'admin, `/admin/exercises/:exerciseId/editor`: consegna, griglia ridimensionabile con ostacoli,
partenza, orientamento e traguardo, i quattro blocchi (con `aula_start` obbligatorio), i limiti e i
messaggi. La griglia si usa da tastiera — cursore con le frecce, comandi accessibili, etichette che
dicono riga, colonna e stato in parole — e il completamento segue sempre il traguardo.

| Controllo eseguito dall'agente | Esito |
|---|---|
| `dotnet build` Release | Uscita 0, **0 warning e 0 errori** |
| `dotnet test` Release | 255 dominio + 453 integrazione = **708 test**, 0 falliti |
| Copertura contro baseline | Api 84,54% · Application 97,69% · Domain 96,22% · Infrastructure 96,48%, tutte sopra baseline |
| `dotnet format whitespace --verify-no-changes` | Uscita 0, dopo aver normalizzato a LF 3 file nuovi |
| `npm run test -- --watch=false` | **176 test** (174 admin + 2 public), 0 falliti |
| `npm run format:check`, `lint`, `build` | Uscite 0 |
| Collaudo nel browser su **database temporaneo** | Esercizio creato; editor aperto senza bozza (`404` → definizione suggerita, pulsante di salvataggio attivo); **salvataggio riuscito**; ricarica che rilegge la bozza; modifica e **secondo salvataggio** con la versione. Log dell'API: `SaveExerciseDraft completata`, `GetExerciseDraft completata`, `SaveExerciseDraft completata` |

Tre difetti trovati e corretti durante la verifica: il pulsante di salvataggio restava spento su un
esercizio senza bozza, perché «niente da salvare» non distingueva la bozza nuova da quella immutata;
la pagina inviava una stringa vuota dove il progetto a blocchi deve essere un oggetto JSON, e il
server — correttamente — rifiutava il salvataggio; un test della griglia contava male la cella del
traguardo. Il primo è emerso da un test della pagina, gli altri due dal collaudo reale.

Limiti: l'editor non compone ancora il progetto a blocchi e invia il progetto vuoto; non c'è
l'anteprima; pubblicazione, `GetReferenceSolution`, `ListExerciseVersions` e `ValidateExerciseDraft`
restano da fare. Una bozza assente fa comparire anche un avviso `ResourceNotFound` accanto alla nota
che spiega il caso: viene dal gestore globale delle chiamate e va corretto nel livello condiviso.

> **Superato dalla terza porzione** (sezione in cima): il progetto a blocchi e l'anteprima
> **esistono** e sono validati. Restano da fare pubblicazione, `GetReferenceSolution`,
> `ListExerciseVersions` e `ValidateExerciseDraft`; l'avviso `ResourceNotFound` è ancora aperto.

## T6, prima porzione: la definizione dell'esercizio e le sue versioni — 2026-10-02

**VALIDATO localmente.** Prima porzione dell'autore degli esercizi, decisa con l'utente in questa
sessione: modello `GridPath` confermato con i quattro blocchi di §13.4 (chiude **D-05**), nessuna
compatibilità `.sb3` nella prima versione — resta fra le evoluzioni eventuali di §15 — e catalogo dei
blocchi che cresce per versioni del motore, non per allargamenti silenziosi della versione esistente.

Questa porzione è **fondazione**: la definizione didattica esiste e si valida, ma non ci sono ancora
né l'editor né la pubblicazione. Nessuna interfaccia e nessun endpoint nuovo: il catalogo del
dispatcher resta a **24 comandi e 13 query**.

In `Domain/Exercises/`: `ExerciseDefinition` (schema, tipo, versione del motore, titolo, consegna,
scena, blocchi ammessi, limiti, completamento, messaggi di esito), `ExerciseScene` (griglia, partenza,
orientamento, ostacoli, traguardo), `ExerciseLimits`, `ExerciseCompletion`, `ExerciseFeedback`,
`GridPosition`, `GridDirection`, `GridMovement` (passo e rotazione di 90 gradi), `GridPathEngine` (le
regole di `gridpath-1`) e `ExerciseBlockCatalog` (catalogo **per versione**).

Migrazione `0004_exercise_authoring.sql`: `exercise_draft` (bozza con `row_version`),
`exercise_version` (immutabile, unico `(exercise_id, version_number)`, con un trigger che rifiuta
l'UPDATE) e `exercise_version_private` (soluzione di riferimento, in tabella separata e mai nei DTO
degli alunni). Entità POCO in `Application/Persistence`, tre interfacce, tre repository, mapping e
registrazioni scoped.

Scelte locali da conoscere: il titolo della definizione segue la regola di utilizzabilità del titolo
dell'anagrafica, quindi i due non possono divergere; partenza e traguardo non possono coincidere,
perché un esercizio già risolto non è un esercizio; gli intervalli dei limiti sono una scelta locale,
come lo erano i limiti di titolo e descrizione dell'esercizio.

| Controllo eseguito dall'agente | Esito |
|---|---|
| `dotnet build` Release | Uscita 0, **0 warning e 0 errori** |
| `dotnet test` Release | 255 dominio + 432 integrazione = **687 test**, 0 falliti (erano 628) |
| Copertura contro baseline | Api 84,54% · Application 97,68% · Domain 96,22% · Infrastructure 96,48%, tutte sopra baseline |
| `dotnet format whitespace --verify-no-changes` | Uscita 0, dopo aver normalizzato a LF 14 file nuovi |
| Fine riga di `src` e `tests` | 0 file non conformi |

Il database di sviluppo `src/Qwerty.Codi.Api/data/codi.db` è rimasto allo **schema 3**: non è
stato avviato il servizio, proprio per non applicargli la `0004` senza una richiesta esplicita.

## Mediator: valutazione chiusa senza modifiche — 2026-10-02

Su richiesta dell'utente si è valutata la sostituzione del pacchetto `Mediator` 2.1.7 con un mediator
interno al repository (`Qwerty.Codi.Mediator`). **La valutazione si è fermata prima di ogni
modifica**: il codice, i file di progetto e la documentazione sono rimasti come erano, e in questa
attività sono stati letti soltanto file e metadati.

Verifica di licenza, dalle fonti disponibili offline: i due pacchetti ripristinati dichiarano
licenza **MIT** nel proprio `.nuspec` (`license type="expression">MIT`, autore Martin Othamar,
repository `github.com/martinothamar/Mediator`, commit
`35290bb86e7c769b4b969500275211f705106551`). Non c'è quindi un ostacolo di licenza, e non serve
riprodurre codice di terzi: le interfacce già usate dal progetto hanno la forma standard richiesta/handler,
e l'orchestrazione si scriverebbe per conto nostro. La consultazione delle pagine web non è stata
possibile in questa sessione, e il `.nuspec` è stata la fonte sostitutiva.

Superficie effettivamente usata, ricavata dal sorgente: `IRequest<>` e `IRequestHandler<,>` per tutte
le richieste del catalogo, `IQuery<>`/`IQueryHandler<,>` per le tre query di scoperta pubblica,
`INotification`/`INotificationHandler<>` per i soli tipi di prova, `IMediator.Send` per l'invocazione
e `AddMediator` per la registrazione. Il mediator è consumato da `Application`
(`MediatorRequestInvoker`), da `Api` (i tre `GET` di `PublicDiscoveryEndpoints`) e dai test; è
generato **solo** nell'assembly degli handler di §4.4.

Impatto inventariato prima di fermarsi: nuovo progetto con le stesse interfacce nel namespace
`Mediator` (così `using Mediator;` resta valido nei file che lo importano), orchestrazione a
riflessione con cache, `Directory.Packages.props`, i due `csproj` e le rispettive voci di commento,
i `packages.lock.json`, `coverlet.runsettings` (l'elenco `Include` della copertura),
`scripts/coverage-baseline.json` (un quinto assembly da registrare), i test di compatibilità da
riscrivere e i documenti che nominano il pacchetto (istruzioni CQRS, skill del caso d'uso,
specifica tecnica §3.2/§4.4 e D-01, `docs/RIPRESA-SVILUPPO.md`, `docs/TEST-COVERAGE-MATRIX.md`).
Non è quindi un lavoro di sola organizzazione: resta annotato come punto aperto **A-16**, non come
lavoro avviato.

## Refactoring dell'Application: un file per entità e natura — 2026-10-02

**VALIDATO.** Riordino dei file del progetto `Application` su richiesta dell'utente: convivevano due
convenzioni, ora ne vale una sola. È un intervento di sola organizzazione: nessun comportamento,
nessun nome di tipo, nessun `RequestName`, nessuna policy e nessun DTO sono cambiati.

Regola recepita: per ogni entità quattro file nella cartella dell'area — `<Entità>Commands.cs`,
`<Entità>CommandHandlers.cs`, `<Entità>Queries.cs`, `<Entità>QueryHandlers.cs` — con più richieste
della stessa entità nello stesso file ma i record separati dai rispettivi handler. DTO, servizi
condivisi (`SessionSummaryProjector`, `InPlayLessonSession`, `AdminCredentialPolicy`) e astrazioni
restano in file propri.

| Area | Prima | Dopo |
|---|---|---|
| `Organization` | 30 file, uno per richiesta e per handler | 12: quattro file per ciascuna di `School`, `Course`, `Classroom` |
| `Sessions` | 20 file | 4 |
| `Lessons` | 8 file | 2: `LessonCommands.cs`, `LessonCommandHandlers.cs` |
| `Admin` | 6 file | 2: `AdminCommands.cs`, `AdminCommandHandlers.cs` |
| `Exercises` | 4 file, con i due handler di query dentro `ExerciseQueries.cs` | 5: i handler escono in `ExerciseQueryHandlers.cs` |

Bilancio complessivo: 66 file assorbiti, 23 file nuovi; l'`Application` passa da 127 a 84 file `.cs`.
Le dichiarazioni sono state spostate verbatim, con il blocco `using` di ogni file deduplicato e
ordinato. Nessun file fuori da `src/Qwerty.Codi.Application` è stato toccato.

| Controllo eseguito dall'agente | Esito |
|---|---|
| `dotnet build` Release | Uscita 0, **0 warning e 0 errori** |
| `dotnet test` Release | 203 dominio + 425 integrazione = **628 test**, 0 falliti |
| Avvio dell'API (`dotnet run`, Release) | `Registro delle richieste: 24 comandi, 13 query, 1 richieste anonime non ancora implementate`: catalogo invariato |
| `test-coverage-gate.ps1` | **4/4** regressioni del gate superate |
| `assert-dotnet-coverage.ps1` | Api 84,54% · Application 97,68% · Domain 96,37% · Infrastructure 96,28%, tutte sopra la baseline |
| `dotnet format whitespace --verify-no-changes` | Uscita 0 |
| Verifica strutturale sui file uniti | 40 handler e 40 richieste, nessuna dichiarazione duplicata |
| Fine riga dei file nuovi | 18 file creati con CR e senza newline finale, normalizzati a LF; 0 non conformi su 84 |

La convenzione è ora scritta in [.github/instructions/cqrs-dispatcher.instructions.md](instructions/cqrs-dispatcher.instructions.md),
così le prossime richieste (join, partecipanti, hub) nascono già nella forma giusta.

## T3, collaudo nel browser e regola «in play» — 2026-10-02

**VALIDATO nel browser.** Prima verifica della sezione Sessioni su HTTPS reale (API 5200, admin 4200,
database di sviluppo allo schema 3), con i dati di prova creati dal percorso reale.

Percorso eseguito senza difetti: istanziazione a cascata scuola → classe → corso → lezione (i livelli
ipendenti sono disabilitati e si abilitano a catena); creazione che porta alla regia; **Avvia**
(In corso, ingressi aperti, revisione 2); **Pausa** (revisione 3); **Riprendi** (revisione 4);
**Stop** con conferma, dove **Annulla non invia nulla** e **Concludi** porta a Conclusa con ingressi
chiusi (revisione 5); ricarica diretta di `/admin/sessions/:id`; rifiuto di una seconda sessione sulla
stessa classe con messaggio chiaro e valori conservati. Verificato anche il lato alunni chiamando gli
endpoint pubblici: compaiono solo la scuola e la classe con sessione in play, con titoli di corso e
lezione e **nessun dato di alunni**. Verificato infine lo **Stop da In attesa**: è il caso che la
migrazione `0003` ha corretto.

**Due correzioni da questo collaudo.**

1. **Regola «in play» estesa** (decisione dell'utente, 2026-10-02): è in play una sessione **avviata**
   (`Active` oppure `Paused`) **con ingressi aperti**. Prima la pausa nascondeva la lezione agli
   alunni: chi arrivava durante una spiegazione non vedeva nulla e non poteva entrare. Ora la lezione
   resta visibile e la pausa sospende solo l'attività; chiudere gli ingressi resta l'unico modo per
   sospendere i nuovi ingressi. Recepito nelle specifiche funzionale **0.5** e tecnica **0.26**.
2. **Dialogo di istanziazione**: con il messaggio d'errore superava l'altezza della finestra e i
   pulsanti finivano fuori dalla vista. Ora l'altezza è limitata e scorre solo il corpo del modulo:
   intestazione, messaggio d'errore e pulsanti restano raggiungibili anche su una finestra bassa.

| Controllo eseguito dall'agente | Esito |
|---|---|
| `dotnet build` e `dotnet test` Release | 203 dominio + 425 integrazione = **628 test**, 0 falliti |
| `npm run format:check`, `lint`, `test -- --watch=false` | Uscita 0; **153 test** Angular (151 admin + 2 public) |
| Collaudo browser dell'admin (HTTPS reale) | Percorso completo senza difetti; nessun errore in console oltre ai `401` attesi prima dell'accesso |
| Endpoint pubblici di scoperta (chiamate reali) | In `Active` e in `Paused` restituiscono la sessione; in `Lobby` e dopo lo Stop danno elenco vuoto o `404` |
| Dialogo su finestra alta 604 px | Messaggio d'errore, «Annulla» e «Crea sessione» tutti visibili e raggiungibili |

Il database di sviluppo contiene ora cinque sessioni, **tutte concluse**: la classe II A è libera.

## T3, seconda porzione: ciclo di vita, regia admin e scoperta pubblica — 2026-10-01

**VALIDATO.** Il ciclo di vita della sessione è completo e comandabile dall'admin, e la scoperta
pubblica è implementata.

- **Comandi**: `Pause`, `Resume`, `EndSession` (ambito classe, policy `SessionController`), oltre a
  `CreateSession` e `StartSession` della porzione precedente. Il dominio ha le transizioni `Pause`
  (`Active` → `Paused`), `Resume` (`Paused` → `Active`) e `End` (da qualsiasi stato non terminale,
  `Lobby` compreso → `Ended`, con ingressi chiusi e istante di fine).
- **Query per la pagina**: `ListSessions` (policy `Admin`, nome nuovo in §10.5) e `GetSession`
  (policy `SessionController`).
- **UI admin**: sezione «Sessioni» con elenco, istanziazione a cascata scuola → classe → corso →
  lezione e regia su `/admin/sessions/:id` con Avvia / Pausa / Riprendi / Stop (definitivo, con
  conferma) e rilettura dal server dopo ogni comando.
- **Scoperta pubblica** (§10.3, anonima e precedente al join): `GET /api/v1/public/schools`,
  `/schools/{id}/classrooms` e `/classrooms/{id}/sessions`, che mostrano soltanto la sessione **in
  play** (`Active` con ingressi aperti) e nessun dato di partecipante. Le richieste applicative
  corrispondenti sono `IQuery<T>` di Mediator invocate per tipo e **non entrano nel catalogo**: prima
  del join non esiste un contesto autenticato, quindi nessuna policy di §9.3 sarebbe applicabile.
  Chiude A-02 per la parte di scoperta.
- **Migrazione `0003_session_state_check.sql`**: corregge il vincolo fra stato e avvio introdotto
  dalla 0002, che vietava di concludere una sessione mai avviata.

| Controllo eseguito dall'agente | Esito |
|---|---|
| `dotnet restore … --locked-mode`, build Release | Uscita 0; build 0 errori, nessun warning. |
| `dotnet test … --collect:"XPlat Code Coverage" … TestResults\t3-completo` | **627 test** (203 `Domain.Tests` + 424 `IntegrationTests`), 0 falliti, 0 saltati; `ProtocolTests` resta senza casi. |
| `assert-dotnet-coverage.ps1` | Copertura non inferiore alla baseline: Api **84,54%** (baseline 83,56), Application **97,68%** (97,59), Domain **96,37%** (94,24), Infrastructure **96,28%** (94,89). |
| `test-coverage-gate.ps1` | **4/4** regressioni superate. |
| Angular `npm run format:check`, `lint`, `test -- --watch=false`, `build` | Uscita 0; **153 test** (151 admin + 2 public) riusciti; bundle admin con i chunk `session-list` e `session-detail`; `dist/site` composto. |

Due difetti trovati dai test **e corretti**: il vincolo di coerenza della 0002, che il caso d'uso
`EndSession` da `Lobby` ha rivelato; e un caso di prova Angular con le asserzioni in ordine
invertito, che non esercitava davvero «Riprendi».

**Riconoscibilità**: non esistono ancora `session_participant`, `station`, join e credenziale del
partecipante, hub, snapshot, presenze e outbox. La regia mostra un segnaposto al posto della griglia
dei partecipanti, e la sezione Sessioni non è stata collaudata nel browser.

## T3, prima porzione: istanziazione e avvio della sessione — 2026-10-01

**VALIDATO.** `CreateSession` e `StartSession` esistono, sono raggiungibili dal dispatcher admin
sul canale HTTP con policy `SessionController` e sono coperti dai gate qui sotto. **Nessuna UI,
nessun client collegato, nessun percorso nel browser**: non esiste alcuna superficie da collaudare.

Codice della porzione:

- Migrazione `0002_sessions.sql`: tabella `lesson_session` con vincoli di stato/ingressi/istanti e
  indice unico parziale `ux_session_one_open_per_classroom` (esclude solo `Ended`). `0001_schema.sql`
  non è stato modificato: le migration scripts sono ora **due**.
- Dominio: `Domain/Sessions/LessonSession.cs` e `SessionStatus.cs` (stati `Lobby`, `Active`,
  `Paused`, `Ended` con vincoli di coerenza, nascita in `Lobby` con ingressi chiusi e revisione 1,
  avvio in `Active` che apre gli ingressi). Oltre a `Create`/`Restore`, l'unica transizione del
  dominio è `Start`: `Pause`, `Resume` ed `EndSession` **non esistono**.
- Casi d'uso `CreateSession` e `StartSession` (policy `SessionController`, area admin, canale HTTP)
  e `SessionDto`; la proprietà della sessione è verificata dal caso d'uso.
- Persistenza: `LessonSessionEntity`, `ILessonSessionRepository`, `LessonSessionRepository`,
  mapping FluentMapper e registrazione scoped.
- Trasporto: `CookieRequestContextProvider` concede ora `[Admin, SessionController]` a una
  credenziale admin valida; una sessione di un altro amministratore risponde `404 ResourceNotFound`,
  non `403`.
- Codici nuovi in `CodiErrorCodes` (409): `SessionAlreadyOpen`, `SessionRevisionConflict`,
  `SessionStateConflict`.

**Riconoscibilità**: nulla di join, `session_participant`, `station`, hub, outbox, `Pause`,
`Resume`, `EndSession`, `OpenAdmissions`, `CloseAdmissions` né frontend admin.

| Controllo eseguito dall'agente | Esito |
|---|---|
| `dotnet restore .\Qwerty.Codi.slnx --locked-mode` | Uscita 0. |
| `dotnet build .\Qwerty.Codi.slnx --configuration Release --no-restore` | Riuscita, 0 errori, nessun warning segnalato. |
| `dotnet test … --collect:"XPlat Code Coverage" --settings .\coverlet.runsettings --results-directory .\TestResults\t3-sessioni-1-final` | **561 test** (185 `Domain.Tests` + 376 `IntegrationTests`), 0 falliti, 0 saltati; `ProtocolTests` resta senza casi (condizione preesistente, fino a T4). |
| `.\.scripts\assert-dotnet-coverage.ps1 -ResultsDirectory .\TestResults\t3-sessioni-1-final` | Copertura non inferiore alla baseline. Api **83,91%** (baseline 83,56), Application **97,83%** (97,59), Domain **96,18%** (94,24), Infrastructure **96,28%** (94,89). |
| `.\scripts\test-coverage-gate.ps1` | **4/4** regressioni superate. |

44 casi di test nuovi (14 dominio, 30 integrazione). Correzione intervenuta nella porzione:
`StartSession` senza revisione attesa rispondeva `400 InvalidPayload`; allineato al precedente del
CRUD (`long? … = null` → `EntityVersionRequired`), ora **428**.

Lacune dichiarate dai test: la guardia per un contesto autenticato con `SessionController` ma senza
`AdminUserId` non è esercitata dal trasporto; `WithRowVersion` non è asserito direttamente; nessuna
transizione `Pause`/`Resume`/`EndSession` è coperta perché non esiste.

## Ciclo di vita della sessione: contratto chiarito — 2026-10-01

> **Istantanea di quella data**, precedente alla prima porzione di T3. Lo stato corrente del
> codice è nella sezione «T3, prima porzione: istanziazione e avvio della sessione — 2026-10-01»
> in cima: da allora `CreateSession` e `StartSession` **esistono e sono validati**, quindi le
> frasi di questa sezione («T3 resta DA INIZIARE», «non implementati») descrivono il momento in
> cui il contratto fu chiarito, non il codice attuale.

**IMPLEMENTATO — modifiche documentali.** Il ciclo di vita della sessione è definito dalle
specifiche funzionale **0.4** e tecnica **0.24**; nessun codice, test o migrazione cambia.

- `CreateSession` istanzia la lezione nella classe creando la **sessione del momento**: stato
  `Lobby`, ingressi chiusi, nessuna associazione permanente lezione↔classe (A-10 resta chiuso).
- Nuovo comando pubblico **`StartSession`** (policy `SessionController`): porta la sessione da
  `Lobby` ad `Active`, apre gli ingressi e imposta la vista di attesa come default di classe; è
  il «Play» dell'utente.
- `Pause`/`Resume` con ambito `Class` esistono solo fra `Active` e `Paused`; `EndSession` è
  terminale ed è ammesso anche da `Lobby`. La transizione `Lobby → Paused` non esiste.
- Per classe resta **una sola sessione non terminata**; la scoperta pubblica restituisce la sola
  sessione in play.

Modifica **documentale**: nessun codice, nessun test scritto, nessuna build eseguita. **T3 resta
DA INIZIARE.** `CreateSession` e `StartSession` sono contratti catalogati e non implementati.

## Collaudo browser dell'admin e dialoghi helm — 2026-10-01

**VALIDATO.** Chiude i due punti che il T2 lasciava aperti: la migrazione dei dialoghi a
`*hlmDialogPortal` e il collaudo dell'area docente nel browser. Verifica eseguita dall'agente
sull'albero corrente, con API HTTPS 5200 e admin HTTPS 4200 avviati in locale su un **database
temporaneo** (`TestResults/collaudo-admin-20261001`, seed di sviluppo A-09); nessun dato
operativo modificato e nessun servizio dell'utente avviato o fermato.

Codice: i cinque dialoghi CRUD (scuole elenco/dettaglio, corsi elenco/dettaglio, esercizi) usano
`*hlmDialogPortal` e l'import `BrnDialogImports` è stato rimosso. Nessun'altra modifica di codice:
gli adattamenti Signal Forms (focus, `aria-describedby`, `FormValueControl`) sono intatti.

| Controllo eseguito dall'agente | Esito |
|---|---|
| `npm run format`, `npm run lint` (root Web) | Uscita 0, nessuna segnalazione. |
| `npm run test -- --watch=false` | Uscita 0; **131** admin/librerie + **2** public, nessun fallimento. |
| `npm run build` | Uscita 0, nessun warning; admin iniziale 494,23 kB (122,43 kB trasferiti), public 214,59 kB; `dist/site` composto. |
| Healthcheck Spartan in modalità rapporto | Uscita 0, **tutti i controlli verdi**, `Helm Dialog Portal` compreso; «Nothing to be done». |
| Collaudo browser dell'admin (una postazione, HTTPS reale) | Eseguito senza difetti; console senza errori salvo i 401 attesi a sessione assente. |
| Laboratorio su 12 PC | Non eseguito: resta del T9 (deroga seed A-09 ancora aperta). |

Evidenze del collaudo browser: accesso valido e credenziali errate (avviso `role="alert"`, nessuna
navigazione); guard di sessione su indirizzo diretto e uscita; dialoghi di creazione e modifica
aperti dal portale con focus nel primo campo e `aria-expanded` sul pulsante; `Esc` chiude e riporta
il focus al pulsante; invio non valido bloccato, errore `role="alert"` collegato via
`aria-describedby` e focus mantenuto sul campo; campi disabilitati durante la mutazione con spinner
globale e notifica; creazione di scuole, esercizi, corsi e lezioni; sequenza riordinata con le
**frecce da tastiera** (messaggio di stato e focus che segue la riga), salvata e **riletta dopo il
ricaricamento**; sidebar con voce attiva e titolo coerente, pagina iniziale con i tre riquadri,
percorso nei dettagli; **percorso mobile** (pannello laterale aperto dal pulsante, collegamento che
naviga e chiude il pannello); apertura del dialogo e invio con i soli tasti.

## Istantanea del T2 prima del collaudo — 2026-10-01

> **Istantanea di quella data.** Catalogo, schema e incremento corrente sono nella sezione più
> recente in cima a questo registro, perché cambiano a ogni consegna. Le frasi al presente di
> questa sezione («19 comandi», «otto tabelle applicative», «la migrazione unica
> `0001_schema.sql`», «T3–T9 restano DA INIZIARE») descrivono il T2 al
> 1 ottobre 2026, non il codice attuale.

**VALIDATO LOCALMENTE; il collaudo nel browser è chiuso dalla sezione sopra.**
Rianalisi da `f059b9b`, confronto con `549e798` e `78d133c`, poi correzioni e verifiche
sull'albero di lavoro corrente. La [mappa di ripresa](../docs/RIPRESA-SVILUPPO.md) descrive
route, catalogo eseguibile, schema e prossimo incremento.

Per decisione dell'utente sono **eliminate dal prodotto** associazioni corso-classe,
iscrizioni e contenuti/immagini. Le specifiche funzionale **0.4** e tecnica **0.24** prevedono
join diretto alla sessione con scuola/classe/nome, lezioni composte da esercizi e un solo
editor della lezione. A-10 è chiuso; la concorrenza fra due editor A-11 è fuori perimetro.

Correzioni completate: sezione della sidebar attiva anche nei dettagli; URL corso/lezione
incoerente rifiutato prima di caricare la sequenza; catena loading/antiforgery/notification
registrata e spinner globale montato. Nuove prove automatiche della configurazione HTTP,
del guscio e della route; **44 casi** diretti CHECK/FK/unicità/limiti nello schema consolidato.

Gate eseguiti dall'agente: restore NuGet locked, build Release **0 errori/0 warning**,
**517/517 test .NET** (171 dominio + 346 integrazione), **131+2 test Angular**;
ProtocolTests resta senza casi. Copertura XPlat isolata in
`TestResults/t2-admin-schema-20261001-final`: Api **84,78%**, Application **98,37%**,
Domain **96,45%**, Infrastructure **96,17%**; gate verde con baseline invariata e
**4/4** regressioni del gate. Lint, formattazione Angular, bundle delle due app e `dist/site`
verificati; audit runtime **0 vulnerabilità**. Dettaglio nelle evidenze sotto.

Il catalogo ha 19 comandi (bootstrap solo locale) e 11 query; lo schema ha otto tabelle
applicative più schema_migration. La migrazione unica `0001_schema.sql` non aggiorna i
vecchi database di sviluppo. Prove su database temporanei, nessun dato operativo modificato;
nessun servizio operativo avviato o fermato. Le evidenze 646 .NET e 90+2 Angular sotto
sono storiche e non descrivono il modello attuale.

Per riprendere: behavior §4.4 e T3 (sessione, scoperta/join, credenziale,
hub/snapshot/presenza). T3–T9 restano DA INIZIARE. La deroga seed di sviluppo A-09 resta aperta
prima del laboratorio.

## Evidenze della verifica T2 — 2026-10-01 (istantanea)

> **Istantanea della verifica T2 del 2026-10-01.** Conteggi, copertura e controlli sono quelli di
> allora; lo stato corrente è nella sezione «T3, prima porzione: istanziazione e avvio della
> sessione — 2026-10-01» in cima.

| Controllo eseguito dall'agente | Esito |
|---|---|
| Restore locked e build Release della soluzione `.slnx` | Uscita 0; build 0 warning/0 errori. Primo errore del nuovo test corretto usando parametri DbCommand portabili, poi build verde. |
| Test Release con collector XPlat | Uscita 0; 171 dominio + 346 integrazione = 517, nessun fallimento/salto. ProtocolTests senza casi. |
| Gate copertura, raccolta dedicata `TestResults/t2-admin-schema-20261001-final` | Api 84,78%; Application 98,37%; Domain 96,45%; Infrastructure 96,17%; baseline invariata; 4/4 regressioni gate. |
| Angular `npm run test -- --watch=false` | Uscita 0; 131 admin/librerie + 2 public, nessun fallimento. |
| Angular lint e format:check | Uscita 0; nuovo app.config.spec formattato con Prettier dopo la prima segnalazione. |
| Build produzione Angular | Uscita 0, nessun warning; admin iniziale 494,23 kB, public 214,59 kB; `dist/site` composto. |
| Audit runtime | Uscita 0, 0 vulnerabilità. |
| Healthcheck Spartan in modalità rapporto | Uscita 0; **tutti i controlli verdi**, nessuna migrazione pendente dopo il passaggio a `*hlmDialogPortal` dei cinque dialoghi CRUD. |
| Controllo documentale finale | 64 documenti, 38 blocchi YAML/frontmatter, 518 link locali e 28 ancore senza errori; catalogo 19 comandi/11 query e otto tabelle applicative coerenti. `git diff --check` pulito. |
| Collaudo grafico/tastiera e certificato HTTPS (una postazione) | Eseguito il 1 ottobre 2026; evidenze nella sezione «Collaudo browser dell'admin e dialoghi helm». Il laboratorio su 12 PC resta del T9. |

Node usato tramite la versione già attiva di nvm, senza installazioni o cambi di versione.
Il restore legge la configurazione NuGet del profilo; le verifiche autorizzate sono state
eseguite con accesso al runtime/profilo quando il sandbox lo impediva. Database dei test temporanei.

## Registro storico delle porzioni precedenti

Le sezioni fino alle evidenze conservano gli esiti sulle rispettive versioni del codice.
Le frasi «tutte le richieste implementate» e i riferimenti a media/iscrizioni o a sei voci
del menu descrivono quelle versioni; per lo stato attuale prevale la sezione sopra.

## Tutte le form admin: Angular Signal Forms e layout Spartan — 2026-10-01

> **Aggiornamento 2026-10-01.** Il **collaudo nel browser** è stato eseguito senza difetti (sezione
> «Collaudo browser dell'admin e dialoghi helm — 2026-10-01»): copre i dialoghi CRUD, non l'aspetto
> responsivo né l'uso completo da tastiera, che restano da verificare. Per decisione dell'utente del
> 2026-10-01 questi due residui (aspetto responsivo delle form e percorso tastiera completo) sono
> **rinviati al T9**.

**VALIDATO LOCALMENTE; collaudo grafico nel browser DA VALIDARE.** Migrati tutti i campi
applicativi esistenti: accesso, creazione/rinomina scuole, classi con anno scolastico,
corsi, lezioni, aggiunta delle attività, contenuti Text/Image, iscrizioni, selettori gerarchici,
filtri booleani e upload immagini. L’inventario è in [ADMIN-SIGNAL-FORMS](../docs/ADMIN-SIGNAL-FORMS.md).
Nessun FormsModule, ngModel o ngSubmit rimane nelle feature admin. Blockly è un workspace,
non una form di dati; archiviazione, associazione e riordino restano azioni esplicite.

Modelli autonomi signal/FieldTree, schema form, binding FormField e invio FormRoot con azioni
asincrone. I validatori impediscono chiamate non valide; un invio non valido mostra errori
Spartan associati via ARIA e focalizza il primo campo. I campi sono disabilitati durante la
mutazione; doppio invio, annullamento e reset sono coperti. Una bozza invalida di testo non
blocca un’immagine. Gli editor dei contenuti escludono i campi del tipo inattivo dallo schema.
La password non viene alterata, i nomi visualizzati mantengono maiuscole e accenti.
Le mutazioni rileggono lo snapshot server e usano la versione aggiornata; un 409 conserva la bozza.

Nuovi Field, Native Select e Textarea generati con CLI Spartan 1.5.0, senza nuove dipendenze
né cambio del Node attivo in nvm. Regole e controllo File riusabili in `@shared/forms`:
anno consecutivo, testo entro i limiti del contratto, iscrizioni fino a 30 grafemi con
Intl.Segmenter. L’upload valida dimensione (non vuota, fino a 5 MiB); il tipo reale resta
riconosciuto dai byte sul server. Il picker si resetta per permettere la riselezione.

Una nuova prova ha trovato un difetto del focus nella select: con il provider CVA upstream,
Angular 22 sceglie l’adattatore di compatibilità prima del model e non usa il focus custom.
Il Native Select locale ora implementa FormValueControl<string> con model(), senza provider
CVA, espone focus() e propaga aria-describedby alla select interna. L’input disabled usa la
trasformazione da unknown richiesta dal contratto Signal Forms. Questi adattamenti sono
documentati nella skill e vanno conservati quando si rigenera Helm.

Conservate le modifiche a sidebar, home, guard, catalogo delle sezioni e stati vuoti già presenti
o realizzate in parallelo nell’albero condiviso. Le prove finali riguardano l’albero corrente,
incluse queste modifiche: non sono attribuite tutte alla migrazione delle form.

Controlli eseguiti dall’agente il 1 ottobre 2026:

- Root Web: npm run lint e format:check riusciti; npm run test -- --watch=false:
  **90/90 admin/librerie e 2/2 public**. Dodici nuovi casi delle form, con ulteriori asserzioni
  su dati/versione riletti e focus della select. Test delle aree API, sessione, media,
  rinomina forcellini → Forcellini e navigazione conservati e verdi.
- npm run build: admin e public più dist/site, senza warning. Admin iniziale **424,29 kB**
  (107,58 kB trasferiti), public **214,59 kB**; budget invariati. Audit runtime:
  **0 vulnerabilità**. Healthcheck Spartan in modalità rapporto: tutti i controlli verdi.
- Root Codì: restore locked-mode riuscito, build Release **0 warning/0 errori**,
  **646/646 test .NET** (254 dominio, 392 integrazione), nessun salto; ProtocolTests ancora
  senza casi. Le prove HTTP verificano anche confini admin/public, accesso e rinomina persistita.
  Nessun codice backend modificato; copertura non rimisurata in questa porzione.
- Skill nuova validata con quick_validate; **62 documenti**, **38 documenti/blocchi YAML**
  e **475 link locali** verificati senza errori; git diff --check riuscito.

Nuova skill `.agents/skills/codi-signal-forms` e istruzioni `.github/instructions/admin-signal-forms`;
collegate da AGENTS, regole comuni, Angular, profili frontend/test/revisore e skill esistenti.
Aggiornati guide root/Web/Spartan, matrice e specifica tecnica 0.21 (§14.1/§24).
Requisiti funzionali, API, DTO, dipendenze e UI public invariati da questa migrazione.
Non sono stati avviati o fermati servizi dell’utente né scritti dati operativi.
Restano DA VALIDARE nel browser aspetto responsivo e uso completo da tastiera; i test DOM
verificano binding, errori e focus, non il rendering grafico reale.

## Pagina iniziale, titolo nella barra e stati vuoti — 2026-10-01

> **Aggiornamento 2026-10-01.** Il **collaudo nel browser** è stato eseguito senza difetti (sezione
> «Collaudo browser dell'admin e dialoghi helm — 2026-10-01»): copre menu, pagina iniziale, guard e
> percorso mobile; il **collasso a icona** resta da verificare. Per decisione dell'utente del
> 2026-10-01 il collasso a icona è **rinviato al T9**.

**VALIDATO LOCALMENTE.** Il test transitoriamente rosso delle form è stato corretto;
anche l’ultima esecuzione completa della porzione Signal Forms è verde.

Su richiesta dell'utente, dopo la navigazione dell'area docente:

- **Pagina iniziale** `features/admin/home` (`admin-home`), montata dalla radice dell'area: elenca
  le sei sezioni in riquadri che sono collegamenti, con una riga di spiegazione. La radice **non
  reindirizza più all'accesso**: chi ha una sessione vede le sezioni, che era il motivo per cui non
  le trovava.
- **Guard di sessione** `features/admin/admin-session-guard` sulla route dell'area: senza sessione
  riporta a `/login`, come faceva il vecchio reindirizzamento, e non mostra sezioni vuote. È un
  controllo di *navigazione*: il permesso decisivo resta sul server. Non richiede la sessione al
  server a ogni clic se il client ne ha già una verificata; se il server non risponde lascia
  passare, perché il guasto lo mostra la pagina e non un modulo di accesso.
- **Catalogo condiviso** `features/admin/admin-sections.ts`: etichette, icone, destinazioni e testi
  sono in un posto solo, usati sia dal menu del guscio sia dai riquadri della pagina iniziale,
  così i due elenchi non possono divergere.
- **Titolo nella barra del guscio**: dice la sezione corrente (utile soprattutto per le quattro
  superfici che condividono la pagina dei corsi), e per le pagine fuori menu mostra il titolo
  dichiarato dalla route. Il nome dell'applicazione nella testata del menu è ora un collegamento
  alla pagina iniziale. L'accesso continua a portare a `/schools` dopo l'entrata: è una riga da
  cambiare se si preferisce la pagina iniziale.
- **Stati vuoti uniformi**: nuovo componente Helm `empty`, e componente condiviso
  `shared/components/empty-state` (`app-empty-state`) — primo contenuto reale del barrel `@shared`.
  Tredici paragrafi di stato vuoto nelle pagine scuole e corsi sono diventati lo stesso riquadro,
  **mantenendo i testi esistenti**; i paragrafi di caricamento restano quelli di prima.

**Intervento fuori dal mio perimetro, da segnalare**: il componente `ui/native-select` generato
per la migrazione delle form **non compilava** — `disabled` era dichiarato
`input<boolean, BooleanInput>` mentre Signal Forms lo richiede su `FormValueControl` come
trasformazione da `unknown` (`TS2416`), e questo fermava build e test dell'intera app. Corretto in
una riga (`input<boolean, unknown>`), senza cambiare il comportamento. Il file appartiene al lavoro
in corso dell'utente sulle form: se viene rigenerato, la correzione va riapplicata.

Controlli eseguiti dall'agente (root Web): `npm run lint` pulito; `npm run format:check` conforme;
`npm run build` con **0 avvisi**, admin iniziale 424,29 kB (107,56 kB di trasferimento) e public
214,59 kB; `npm run audit:runtime` **0 vulnerabilità**. Test: **90 casi admin/librerie e 2 del frontend, tutti
verdi**. I dieci casi nuovi di questa porzione sono 3 del guscio (titolo nella barra, pagina fuori
menu, collegamento alla pagina iniziale), 4 del guard di sessione, 2 della pagina iniziale e 1 della
radice con sessione.

**Lavoro in parallelo**: nello stesso pomeriggio l'utente stava migrando le form alle Signal Forms
(`ui/field`, `ui/native-select`, `ui/textarea`, `shared/forms`). Durante una mia esecuzione un caso
nuovo di `course-list.spec` è risultato rosso (`valida separatamente testo e immagine…`, attesa di
`focus()` sull'elemento interno del selettore); l'utente l'ha sistemato e l'ultima esecuzione della
suite è **tutta verde**. Le modifiche delle due porzioni convivono negli stessi file: chi riprende
questi file deve aspettarsi entrambe.

Restano aperti: il **collaudo nel browser** (menu, collasso a icona, pagina iniziale, guard),
il percorso mobile del pannello, non coperto dai test perché in jsdom manca `matchMedia`, e il
codice dell'overlay del CDK nel bundle iniziale (invariato, +7 kB dall'instradamento nuovo).

Specifiche, contratti e nomi pubblici invariati: nessun endpoint, DTO o migrazione cambia.
Aggiornati `src/Qwerty.Codi.Web/README.md` e `docs/SPARTAN-UI.md`. Nessuna scrittura sui dati
operativi e nessun servizio avviato o fermato.

## Navigazione dell'area docente: guscio con sidebar Spartan — 2026-10-01

> **Aggiornamento 2026-10-01.** Il **collaudo nel browser** è stato eseguito senza difetti (sezione
> «Collaudo browser dell'admin e dialoghi helm — 2026-10-01»): copre menu, navigazione e pannello su
> schermo stretto; il **collasso a icona** resta da verificare. Per decisione dell'utente del
> 2026-10-01 il collasso a icona e la verifica di tutte le voci della sidebar sono **rinviati al T9**.

**VALIDATO LOCALMENTE.** Segnalazione dell'utente: le sezioni di «Organizzazione e contenuti»
non erano raggiungibili dall'admin. La causa non era un lavoro mancante ma l'assenza di
navigazione: le pagine esistevano e funzionavano, ma solo come indirizzi da digitare, perché
`/admin/schools` e `/admin/courses` non si collegavano fra loro e la radice porta all'accesso.

Nuovo `features/admin/admin-shell`: un guscio che tiene il menu delle sezioni a sinistra e la
pagina della route a destra. Il menu ha due gruppi, `Organizzazione` (Scuole e classi, Corsi) e
`Contenuti` (Lezioni e attività, Contenuti, Immagini, Iscrizioni); le quattro sezioni della
pagina corsi usano lo stesso stato `?tab=` che la pagina già legge dall'indirizzo, quindi menu e
linguette restano d'accordo e un collegamento apre la sezione giusta. L'uscita dall'area è
scesa nel piè di pagina del menu: le due pagine non hanno più un pulsante «Esci» proprio né il
collegamento incrociato fra scuole e corsi. Su schermo stretto il menu diventa un pannello
laterale aperto dal pulsante in alto; la voce corrente è segnata con `aria-current="page"`, e le
voci sono collegamenti veri.

Un test ha trovato un difetto reale prima della consegna: passare a `routerLink` una stringa che
contiene `?` — `'/courses?tab=lessons'` — la trasforma in **un solo segmento codificato**
(`/courses%3Ftab%3Dlessons`), quindi la voce appariva corretta nell'aspetto ma il clic portava a
un indirizzo inesistente. La destinazione si compone ora da percorso e `queryParams` separati, e
la prova clicca la voce e verifica l'indirizzo risultante.

Componenti: generati `sidebar`, `sheet`, `separator`, `skeleton` e `tooltip` con il CLI 1.5.0
(destinazione `projects/admin/src/app/ui`). Aggiunta la dipendenza `@ng-icons/lucide`, **allineata
a 36.1.0** come `@ng-icons/core`: il generatore proponeva l'intervallo `>=32.0.0 <34.0.0`, che
avrebbe mescolato due major. `eslint.config.mjs` riconosce al codice Helm il prefisso `hlm` anche
sugli elementi e l'alias di input `class`, che è parte della sua API di composizione.

Controlli eseguiti dall'agente, dalla root Web:

- `npm run lint` pulito e `npm run format:check` conforme («All matched files use Prettier code
  style»), dopo una passata di `npm run format` sui sorgenti generati e sui due `tsconfig`.
- `npm run test -- --watch=false`: **68 test** admin/librerie (63 più 5 nuove prove del guscio:
  voci presenti, voce corrente, distinzione delle sezioni che condividono `/courses`, clic che
  cambia indirizzo, uscita che revoca la sessione e riporta all'accesso) e **2** del frontend.
- `npm run build`: due bundle riusciti, nessun avviso. Admin iniziale **417,15 kB** (104,47 kB di
  trasferimento) contro **329,90 kB** misurati con la stessa procedura sulla configurazione
  precedente; Blockly resta nel chunk lazy a 754,32 kB e il guscio è un chunk proprio da 86,79 kB.
  Il foglio di stile dell'admin passa da 29,4 kB a 56,4 kB: le classi della sidebar sono globali.
- `npm run audit:runtime`: **0 vulnerabilità**.

Limiti e punti aperti di questa porzione:

- **Il collaudo nel browser non è stato fatto**: verificare menu, collasso a icona, pannello su
  schermo stretto e navigazione fra le sei voci richiede API avviata e una sessione docente. Le
  prove automatiche montano il guscio in jsdom, dove non c'è `matchMedia`: il percorso mobile del
  pannello non è coperto da nessun test.
- Nell'esecuzione più recente il **codice dell'overlay del CDK** (circa 60 kB non compressi, 17,5 kB
  in più sul trasferimento del bundle iniziale) entra nel bundle iniziale dell'admin: è condiviso fra
  il guscio e `provideSpartanHlm()` di `app.config.ts`, e il bundler lo colloca nel chunk
  dell'ingresso invece che in quello del guscio. Non è un difetto funzionale e il budget di 500 kB
  resta rispettato, ma va capito prima di aggiungere altre viste di area.

Specifiche e contratti invariati: nessun endpoint, nome pubblico o DTO cambia. Aggiornati
`src/Qwerty.Codi.Web/README.md` e `docs/SPARTAN-UI.md`. Nessuna modifica ai dati operativi,
nessun servizio avviato o fermato.

## Rinomina scuola: correzione maiuscole e prova del dato persistito — 2026-10-01

**VALIDATO LOCALMENTE.** Segnalazione dell'utente: `forcellini` → `Forcellini` sembrava
riuscire ma conservava la f minuscola. `School.Rename` confrontava il nome normalizzato
e considerava invariata ogni correzione di maiuscole o accenti. Il confronto ora riguarda
il nome visualizzato, tolti gli spazi ai bordi; la normalizzazione resta la chiave di unicità.
Le correzioni visibili si persistono e avanzano la versione; salvare lo stesso testo resta
inerte. Aggiornato il commento di UpdateSchoolHandler, senza cambiare API o migrazioni.

Il test Angular precedente verificava soltanto il comando inviato. Ora verifica anche
nome riletto e mostrato, chiusura del campo e nuova versione nella modifica successiva,
usando il caso `forcellini` → `Forcellini`. La nuova prova HTTP crea davvero la scuola
nel database temporaneo, rinomina e controlla nome/ID/versione nel DTO, nel dettaglio
GetSchool e nella lista ListSchools da richieste successive. Casi: nome differente,
sola maiuscola iniziale e accento. Aggiornati i test che prima ammettevano il comportamento
errato, mantenendo il caso inerte per testo identico e spazi ai bordi.

Evidenze: prima della correzione la nuova prova HTTP falliva nei due casi equivalenti
normalizzati; dopo la correzione **646/646 test .NET** (254 dominio, 392 integrazione),
build Release 0 warning/0 errori. Copertura in `TestResults/school-rename-20261001`,
gate verde e baseline invariata: Api 85,38%, Application 97,96%, Domain 97,24%,
Infrastructure 96,19%. **63/63 test Angular admin/librerie**, lint e format:check riusciti;
build produzione admin senza warning. ProtocolTests ancora senza casi, public non modificato.
Controllati 59 documenti, 36 blocchi/documenti YAML e 444 link locali senza errori.

Aggiornate matrice di copertura e istruzioni `.github/instructions/test-dotnet`: un 200
o un comando registrato nello stub non dimostrano la persistenza. Specifiche e contratti
invariati. Nessuna modifica ai dati operativi e nessun avvio/arresto dei servizi dell'utente.
Per caricare la correzione nel processo già avviato occorre riavviare l'API; click nel browser
non collaudato in questa porzione. I servizi avviati dall'agente nella porzione precedente
erano stati tutti fermati su richiesta dell'utente prima di questa segnalazione.

## Separazione HTTP admin/public e correzione accesso — 2026-10-01

**VALIDATO LOCALMENTE.** Strategia e inventario in [API-ENDPOINTS](../docs/API-ENDPOINTS.md).
Un backend e un registro CQRS, due gruppi versionati `/api/v1/admin` e `/api/v1/public`.
Gli endpoint dedicati sono organizzati in `Api/Endpoints/Admin` e `Endpoints/Public`;
il dispatcher è condiviso e limita il catalogo per area prima di leggere il payload.
Admin/SessionController appartengono ad admin, le tre policy partecipante a public;
LoginAdmin è anonimo in admin, JoinSession in public, BootstrapAdminAccount solo locale.
Fuori area: 404 anche con cookie admin; nell'area corretta restano canale, 401/403,
antiforgery e controlli applicativi della risorsa. Nessun alias delle vecchie API.

Le app forniscono `API_AREA` esplicitamente. Corretto il 404 dell'accesso: URL dedicati
di login/logout/profilo e media ora usano il prefisso admin; il bootstrap admin è autonomo.
Corretto anche il proxy Vite: `/api/**` intercetta le rotte annidate, mentre `/api`
intercettava solo quel percorso esatto. `Location` degli upload punta al download admin.
Il fallback SPA non trasforma le vecchie rotte media o le API assenti in HTML con stato 200.

Verifiche effettive:

- Restore .NET con lockfile, build Release **0 warning/0 errori**, **641/641 test .NET**:
  252 dominio e 389 integrazione, inclusi 19 casi di area HTTP e due nuovi casi di fallback.
  ProtocolTests ancora senza casi (T4). Raccolta finale in
  `TestResults/api-areas-validated-20261001`; gate verde e baseline invariata:
  Api 85,38%, Application 97,96%, Domain 97,24%, Infrastructure 96,19%.
  Quattro regressioni del gate superate; formattazione C# sui file del perimetro riuscita.
- **65/65 test Angular**: 63 admin/librerie e 2 public. Nuove prove per selezione dell'area
  e flusso bootstrap → login → profilo → logout, errore di rete e profilo 401.
  Lint, format:check, build produzione di entrambe le app e audit runtime riusciti,
  senza warning di build e **0 vulnerabilità**. Node nvm e lockfile invariati.
- Prova reale HTTPS su database/storage temporanei: pagine 4200 e 4300, bootstrap
  admin via 4200 e public via 4300 **200**; login e profilo admin **200**, query admin
  **200**, query admin nel gruppo pubblico via 4300 **404**, upload **201** con Location
  corretta, download **200**, logout **204**, profilo successivo **401**.
  Nessuna modifica ai dati operativi durante questa prova.
- Terminata l'istanza temporanea e il server public di collaudo; riavviata l'API sulla
  configurazione normale HTTPS 5200. Admin lasciato avviato su HTTPS 4200 con proxy nuovo.

Aggiornati specifica tecnica 0.20 (§10, §14.2 e rimandi), README root/Web, matrice,
AGENTS, guida `.github`, regole comuni, istruzioni CQRS/Angular/test, profili backend,
frontend e revisore, skill caso-uso-cqrs/pagina-angular. La specifica funzionale non cambia.
Controllati 59 documenti, 36 blocchi/documenti YAML e 444 link locali senza errori;
nessun workflow CI introdotto. Le evidenze storiche sottostanti possono citare vecchie
rotte: per il contratto corrente seguire questa sezione e la guida endpoint.

Resta da collaudare il click nel browser e la gestione completa dei cookie nel browser:
la prova effettuata è HTTP tramite HTTPS e proxy. Nella sessione precedente Computer Use
si è fermato perché non riusciva a determinare con affidabilità l'URL della finestra.
Scoperta, join, credenziali, media e salvataggi alunni non sono implementati da questo
riordino: gli adattatori pubblici sono pronti, i casi d'uso arriveranno in T3 e successivi.

## Rimozione CI GitHub — 2026-10-01

Su richiesta dell'utente eliminato il workflow di GitHub Actions. Verifiche .NET e
Angular, copertura e audit restano locali; la CI non è più un requisito o un'attività
pendente. Aggiornate specifica tecnica 0.19, README, guide e regole operative.
I riferimenti nelle evidenze storiche e nel registro revisioni descrivono la configurazione
precedente e non richiedono di ripristinarla. Non introdurre nuova CI senza richiesta esplicita.

Verifica: nessun workflow Actions rimasto; controllati 58 documenti, 36 blocchi/documenti
YAML e 422 link locali senza errori. Formattazione della guida Web e `git diff --check`
riusciti. Nessuna modifica al codice applicativo o ai comandi di verifica locali.

## Avvio rapido Windows — 2026-10-01

Aggiunti nella root `Codi/` quattro launcher: `start-server.bat` (API, profilo HTTPS
5200 senza browser), `start-admin.bat` (`npm run start`, HTTPS 4200), `start-public.bat`
(`npm run start`, HTTPS 4300) e `start-angular.bat` (entrambe le app in terminali
separati). Usano percorsi relativi alla propria posizione e i runtime nel PATH; non
installano dipendenze e non selezionano versioni nvm. Errori visibili con pausa; arresto
del servizio tramite Ctrl+C. README root e Web aggiornati, CRLF dei `.bat` dichiarato
in `.editorconfig`. I launcher Angular entrano rispettivamente in `projects/admin` e
`projects/public`: i due manifest locali definiscono il proprio `start`, con dipendenze
e lockfile comuni nella root Web. Gli alias della root restano disponibili.

**VALIDATO LOCALMENTE.** I due script `start` applicativi entrano nella root Web prima
di eseguire `ng serve`, risolvendo proxy e certificati nello stesso percorso condiviso.
Verifica finale del 2026-10-01:

- Eseguiti realmente `start-admin.bat` e `start-public.bat` da una cartella diversa,
  con Node già attivo tramite nvm. Avviati HTTPS 4200 e 4300; risposte HTTP 200 su
  `/admin/login` e `/`, con base href `/admin/` e `/`. Nessuna modifica di dati;
  entrambi i server sono stati fermati dopo la prova.
- Alias della root `start`, `start:admin`, `start:frontend` verificati con `-- --help`:
  selezionano la corretta app e inoltrano gli argomenti al comando Angular, uscita 0.
- `start-angular.bat` eseguito con npm simulato in due terminali: entrambi chiamano
  `npm run start`, rispettivamente da `projects/admin` e `projects/public`, uscita 0.
  Launcher API verificato nella prova precedente con dotnet simulato; non è stata
  avviata l'API operativa durante questi controlli.
- Gate della root Web riusciti: lint, formattazione, **61/61 test Angular**,
  build produzione admin/public senza warning e audit runtime **0 vulnerabilità**.
- Allineati README root/Web, specifiche, guida Spartan, matrice, `.github/README.md`,
  regole comuni, istruzioni Angular, profilo e prompt frontend, skill pagina-angular,
  comandi di verifica e descrizione CI. Specifica tecnica 0.18; dipendenze e lockfile
  invariati. I gate locali verificano entrambe le app con una sola installazione.
- Controllo statico: **58 documenti**, **37 documenti/blocchi YAML**, **423 link locali**,
  nessun riferimento mancante; **8/8 skill** valide. Confermati manifest, cartelle,
  porte, SSL, base href, lockfile comune e F5 senza browser. `git diff --check` riuscito.

Queste prove validano avvio e coerenza documentale; non sono un nuovo collaudo del login,
dei dati amministrativi o della lezione. La discovery nell'editor resta da provare.

## Passaggio storico — due app Angular e Spartan admin

**VALIDATO LOCALMENTE il 2026-10-01.** Workspace Web con `projects/admin` (`web.admin`)
e `projects/public` (`web.frontend`), nomi richiesti dall'utente. Dipendenze e lockfile comuni;
contratti, trasporto e antiforgery comuni in `libraries/api`, motore Blockly in
`libraries/exercise-engine`. Spartan 1.5.0 e Tailwind 4 sono confinati all'admin.
Pagine amministrative esistenti migrate a button/input/label/card; UI alunni provvisoria,
senza ingresso alla lezione ancora implementato. Backend e contratti CQRS restano condivisi;
permessi e endpoint dedicati distinguono i canali.

Avvio dalla cartella Web: `npm start` (alias `start:admin`) su
`https://localhost:4200/admin/`; `npm run start:frontend` su `https://localhost:4300/`.
Entrambe usano il proxy API/hub verso `https://localhost:5200`. F5 Visual Studio usa
`launchBrowser: false`. Node **24.15.0**, npm **11.12.1**, già attivi tramite nvm;
nessuna installazione o selezione di una nuova versione Node.

Dalle cartelle delle app il comando uniforme è `npm run start`: in `projects/admin`
avvia admin, in `projects/public` avvia public. I `package.json` locali sono soltanto
manifest di avvio; installazione, lint, test e build si eseguono nella root Web.

Skill nella cartella del progetto: `.agents/skills/spartan` ufficiale, acquisita da
spartan-ng/spartan, e `.agents/skills/codi-spartan` per il workflow locale.
Entrambe passano `quick_validate.py`; nell'ufficiale è stato rimosso solo il campo YAML
`user-invocable`, incompatibile con quel validatore Codex. AGENTS e pagina-angular
le collegano; [SPARTAN-UI.md](../docs/SPARTAN-UI.md) raccoglie documentazione, catalogo e
comandi. Letti la skill ufficiale e tutti i suoi riferimenti, oltre alle guide ufficiali
di configurazione, temi, manutenzione e form; i componenti futuri richiedono la lettura
della rispettiva pagina, non sono dichiarati tutti verificati.

Evidenze effettive del 2026-10-01:

- `npm ci` con il runtime nvm: riuscito, lockfile riproducibile.
- Dopo la rinomina: `npm run format:check`, `npm run lint`, `npm test -- --watch=false`,
  `npm run build`, `npm audit` e `npm run ng -- g @spartan-ng/cli:healthcheck`: tutti riusciti.
  **61 test Angular** (59 admin/librerie, 2 alunni), nessun warning di build;
  audit completo **0 vulnerabilità**, healthcheck senza migrazioni necessarie.
  Override limitati alle dipendenze transitive Nx axios, brace-expansion e smol-toml.
- `dotnet restore .\Qwerty.Codi.slnx --locked-mode`, build Release e test con copertura:
  **620/620 test .NET** (252 dominio, 368 integrazione), build 0 warning/0 errori.
  Include 16 nuovi casi di hosting statico/fallback SPA, senza database operativo.
  Gate e controlli del gate verdi; copertura Api 84,86%, Application 97,96%,
  Domain 97,24%, Infrastructure 96,19%, baseline invariata.
  Risultati in `TestResults/spartan-20261001`. ProtocolTests ancora senza casi (T4).
- `npm start` e `start:frontend` avviati con HTTPS e certificati locali esistenti:
  browser su 4200 reindirizza a `/admin/login` e mostra il form docente; browser su 4300
  mostra la pagina alunni. Nessun login o modifica dati. API spenta durante questa prova,
  quindi `/auth/me` segnala il previsto errore proxy; non è una prova del login integrato.
  Server Angular fermati dopo la verifica.
- `build` produce `dist/site` con alunni alla radice e admin sotto `/admin/`; nessuna
  distribuzione nel wwwroot dell'API, nessuna pubblicazione o modifica dati operativi.

Restano fuori da questa consegna: accesso/join e aula live T3, design definitivo alunni,
collaudo del login integrato e delle UI complesse sui PC del laboratorio e
discovery delle skill in una nuova sessione dell'editor. Prossimo incremento: T3 secondo
§21.1; T2 è già completato. I passaggi storici sotto rimangono evidenze delle fasi precedenti.

## Stati e prove

- **DA INIZIARE**: nessun percorso implementato.
- **IN CORSO**: porzione incompleta o prerequisiti aperti.
- **IMPLEMENTATO — DA VALIDARE**: codice e test scritti, controlli richiesti ancora senza esito.
- **VALIDATO**: criteri della porzione soddisfatti con comando/scenario, data ed esito registrati.
- **BLOCCATO**: impedimento concreto con domanda o dipendenza identificata; riguarda solo la porzione interessata.

## Inventario storico iniziale — 30 settembre 2026

- Soluzione .NET con quattro progetti applicativi e tre progetti xUnit.
- `Domain` e `Application`: progetti senza logica applicativa.
- API: solo `GET /health/live`, nessun dispatcher/login/hub.
- Infrastructure: `SqliteConnectionFactory`; non ancora registrata nel bootstrap API.
- Quattro test di integrazione del prototipo di persistenza: rilettura, rollback esplicito, versione obsoleta, lettura RepoDB con alias SQL.
- Test di dominio e protocollo senza casi; Web e docs contengono soltanto `.gitkeep`.
- SDK in `global.json`; pacchetti nei singoli csproj. Mediator, Angular e Blockly ancora da integrare; gestione centralizzata e lockfile da realizzare.
- Nessun repository Git rilevato nella cartella o nei suoi genitori al momento dell'analisi. Ricontrollare prima di usare diff o commit.

## Incrementi

| Fase | Stato | Lavoro residuo essenziale |
|---|---|---|
| T0 — Compatibilità | **VALIDATO** il 2026-09-30 | SDK e pacchetti bloccati (`global.json`, gestione centralizzata, lockfile NuGet e npm); prototipi di persistenza, Mediator e Blockly verificati con build e test, correzioni D1–D5 incluse; build di produzione del frontend senza warning. |
| T1 — Fondazioni | **VALIDATO LOCALMENTE** | Dispatcher, accesso e schema consolidato verificati con i gate del 2026-10-01. La deroga seed A-09 resta aperta prima del laboratorio. |
| T2 — Organizzazione | **VALIDATO LOCALMENTE e NEL BROWSER** il 2026-10-01 | Scuole/classi, corsi/lezioni, anagrafica esercizi e sequenza. A-10, A-12–A-15 chiusi; A-11 fuori perimetro. Il collaudo browser dell'admin è chiuso (sezione «Collaudo browser dell'admin e dialoghi helm — 2026-10-01»); restano fuori soltanto il laboratorio su 12 PC, del T9, e la deroga seed A-09. |
| T3 — Codi live | **CHIUSO lato server e lato interfaccia** (2 ottobre 2026); ingresso con **codice di lezione** il 2026-10-09 | Fatti: ciclo di vita della sessione e regia admin, scoperta pubblica pre-join, `station` e `session_participant`, **ingresso idempotente, credenziale e contesto del partecipante** — con il codice di otto cifre come via ordinaria e la catena come ripiego —, **uscita volontaria e regia dei partecipanti**, **trasporto degli hub**, **stato desiderato comandato** (`ShowWaiting`), gli **altri comandi di contenuto** con le loro **letture**, la **griglia dei partecipanti** nella pagina di regia e la **UI degli alunni** (ingresso e prima schermata della lezione, grezza di proposito). Restano fuori da T3 il **client SignalR**, la telemetria e il player, che appartengono a T4/T7/T8, e il collaudo in aula. |
| T4 — Regia affidabile | **CHIUSO E VALIDATO LOCALMENTE** il 2026-10-05 | Regia idempotente, coordinatore, stati per destinatario, conferme, ritentativi, autorizzazione temporanea e osservazione docente. Dimostrazione finita T4 e test di 12 connessioni. Evidenze nel [rapporto T4](../docs/T4-REGIA-AFFIDABILE.md). Il laboratorio reale resta di T9. |
| T5 — Salvataggi | **CHIUSO E VALIDATO LOCALMENTE** il 2026-10-05 | Salvataggio con revisione, impronta e idempotenza, checkpoint, copie locali, permessi conclusivi e ripristino dopo riavvio del server. Il player Blockly resta di T7. |
| T6 — Autore Blockly | **CHIUSO** il 2026-10-02 | Definizione `GridPath` nel dominio, bozza, progetto a blocchi di Blockly, anteprima dell'autore con il motore `gridpath-1`, versioni immutabili con soluzione privata e note dell'autore, anteprima in sola lettura di una versione, copia nella bozza. L'assegnazione di una versione alla sequenza è di T4 (`AssignExercise`), non dell'autore. |
| T7 — Player Blockly | **CHIUSO — VALIDATO LOCALMENTE E COLLAUDATO NEL BROWSER** il 2026-10-05; **prova locale dell'alunno** il 2026-10-09 | Worker, checkpoint, ripresa dal checkpoint persistito e valutazione locale dichiarata dal browser (`ClientReported`). Due difetti d'integrazione trovati dal collaudo e corretti con test di regressione. La pagina dell'alunno ha lo **stato a riquadri con icona colorata** e la **prova locale** del programma con animazione della scena: stessa corsa isolata dell'anteprima dell'autore, ma nella pagina e senza checkpoint né telemetria (§13.1, §14.4). Il laboratorio su 12 PC resta di T9. |
| T8 — Osservazione | **CHIUSO — VALIDATO LOCALMENTE E COLLAUDATO NEL BROWSER** il 2026-10-05, collaudo del percorso il 2026-10-07 | Anteprima dei blocchi in cache in memoria (hub entro 24 KiB, endpoint dedicato entro 256 KiB), avviso ai docenti che osservano senza il contenuto, lettura nel dettaglio dell'alunno aperto con freschezza, revisione e indicazione di parzialità. Il 2026-10-07 il percorso è stato collaudato nel browser (rev. 2 e 4 pubblicate dal browser dell'alunno, «coerente col progetto salvato»); resta non esercitato in browser il ripiego HTTP oltre 24 KiB. Laboratorio su 12 PC di T9. |
| T9 — Laboratorio | DA INIZIARE | Installazione, backup, ripristino e prova di 60 minuti su 12 PC. Comprende il collaudo browser dei quattro residui dell'admin **rinviati al T9** per decisione dell'utente del 2026-10-01: aspetto responsivo delle form admin; percorso tastiera completo su tutte le form; collasso della sidebar a icona; navigazione su tutte le voci della sidebar. |

## Evidenze

| Data | Verifica | Esito e limiti |
|---|---|---|
| 2026-10-13 | Skill invocabili: link relativi corretti e guida allineata | Le nove skill nate dai prompt erano in `.github/skills/` con link scritti come se fossero nella radice `.github/`: `../copilot-instructions.md` e `../skills/<nome>/SKILL.md` puntavano a percorsi inesistenti. Corretti in `../../copilot-instructions.md` e `../<nome>/SKILL.md` (**18 link**, una riga per skill). Verifica eseguita dall'agente su tutti i `.md` di `.github/`: **0 link locali rotti**, **15 skill**, **9 invocabili** con `disable-model-invocation: true`, nome di ognuna coincidente con la cartella, frontmatter con `name` e `description`. `.github/README.md` non descrive più i prompt come scorciatoie (la cartella `prompts/` è vuota e non committata): elenca le nove skill invocabili con i link ai file. `AGENTS.md` e `sviluppo-agentico.instructions.md` allineati allo stesso criterio, con la forma dei percorsi relativa a una skill. Limiti: nessuna build o test applicativo eseguito, perché la modifica tocca solo documenti e non una pipeline o uno script di verifica; la discovery nell'editor non è dimostrata dalla verifica statica. `allinea-documentazione-2` ha un nome diverso dalla procedura `allinea-documentazione` che richiama: accettata come scelta dell'utente, non rinominata. |
| 2026-10-02 | T3, seconda porzione: ingresso idempotente e credenziale del partecipante | Build Release uscita 0 con 0 warning; **870 test .NET** (286 dominio + 584 integrazione), nessun fallimento; copertura sopra baseline in `TestResults/t3-p2-final` (Api 86,94%, Application 97,95%, Domain 96,22%, Infrastructure 96,89%) dopo un primo giro rosso su `Application` coperto con nuovi test invece che abbassando la soglia; gate 4/4; `dotnet format whitespace --verify-no-changes` a uscita 0 e 22 file nuovi o modificati normalizzati a LF. `JoinSession` e `GetParticipantContext` nel catalogo (29 comandi, 18 query), migrazione `0006` con `participant_state` e `idempotency_record`, cookie `__Host-CodiParticipant` e schema d'area, endpoint `POST /api/v1/public/join` e `GET /api/v1/public/auth/me`. Collaudo del processo reale (porta 5310, database di collaudo passato allo schema 6): ingresso `200`, contesto `200`, ripetizione da browser con credenziale `403`. Limiti: nessuna UI degli alunni e nessun modulo di ingresso, quindi nessun collaudo nel browser; l'uscita volontaria resta senza nome nel catalogo; il frontend non è stato toccato. |
| 2026-10-02 | T3, prima porzione: partecipanti e postazioni | Build Release uscita 0 con 0 warning; **817 test .NET** (286 dominio + 531 integrazione), nessun fallimento; copertura sopra baseline in `TestResults/t3-p1-20261002` (Api 84,54%, Application 97,64%, Domain 96,46%, Infrastructure 96,63%); gate 4/4; `dotnet format whitespace --verify-no-changes` a uscita 0 e nessun file con fine riga non conforme. Migrazione `0005` con `station` e `session_participant`; dominio `SessionParticipant`/`ParticipantName`/`AdmissionStatus`; un database esistente allo schema 4 è passato allo schema 5 all'avvio. Limiti: nessuna interfaccia e nessuna rotta nuova, quindi nessun collaudo nel browser; il frontend non è stato toccato. |
| 2026-10-02 | T6, chiusura dei residui: note dell'autore, anteprima di versione e `404` previsto | Build Release uscita 0 con 0 warning; **763 test .NET** (255 dominio + 508 integrazione) e **228 test Angular**, nessun fallimento; copertura sopra baseline in `TestResults/t6-chiusura-20261002` (Api 84,54%, Application 97,69%, Domain 96,22%, Infrastructure 96,48%); gate 4/4; `dotnet format whitespace --verify-no-changes`, `npm run format:check`, `lint` e `build` a uscita 0. Collaudo nel browser su database temporaneo (API 5310, admin 4310): nessun avviso con `GetExerciseDraft` rifiutata con `ResourceNotFound`; note pubblicate e mostrate; anteprima della versione 1 che ignora un ostacolo aggiunto dopo nella bozza. Limiti: la versione fissata alla sequenza resta l'assegnazione (T4), non un lavoro dell'autore; la composizione dei blocchi con il mouse non è automatizzata dall'agente. |
| 2026-10-02 | T6, seconda porzione: casi d'uso della bozza ed editor nell'admin | Build Release uscita 0 con 0 warning; **708 test .NET** (255 dominio + 453 integrazione) e **176 test Angular**, nessun fallimento; copertura sopra baseline (Application 97,69% dopo aver aggiunto i test dei percorsi di rifiuto, che il primo giro aveva lasciato scoperti); `dotnet format whitespace --verify-no-changes`, `npm run format:check`, `lint` e `build` a uscita 0. In `Application/Exercises`: `ExerciseDefinitionJson`, `ExerciseDraftDto`, `SaveExerciseDraft` e `GetExerciseDraft`. Cataloghi: **25 comandi e 14 query**. Collaudo nel browser su database temporaneo: creazione, 404 della bozza, salvataggio, rilettura dopo ricarica, secondo salvataggio con la versione. Limiti: nessun progetto a blocchi composto, nessuna anteprima, nessuna pubblicazione; database di sviluppo non toccato (resta allo schema 3). |
| 2026-10-02 | T6, prima porzione: definizione `GridPath` e scheletro delle versioni | Build Release uscita 0 con 0 warning; **687 test .NET** (255 dominio + 432 integrazione), nessun fallimento; copertura sopra baseline (Domain 96,22%, Infrastructure 96,48%); `dotnet format whitespace --verify-no-changes` uscita 0 dopo la normalizzazione a LF di 14 file. 52 casi di dominio nuovi (definizione, scena, movimento) e 7 di persistenza. Migrazione `0004` con `exercise_draft`, `exercise_version` (immutabile, con trigger che rifiuta l'UPDATE) e `exercise_version_private`. Limiti: nessuna interfaccia, nessun caso d'uso, nessun endpoint; la definizione non è ancora componibile da un docente e il motore non esiste. Il database di sviluppo resta allo schema 3. |
| 2026-10-02 | Refactoring dell'`Application`: un file per entità e natura | 65 file assorbiti più uno riscritto, 22 file nuovi: l'`Application` passa da 127 a 84 file `.cs` (`Organization` 30→12, `Sessions` 20→4, `Lessons` 8→2, `Admin` 6→2, `Exercises` 4→5). Nessun cambio a nomi dei tipi, `RequestName`, policy, DTO o comportamento: i record e gli handler sono spostati verbatim. Build Release uscita 0 con **0 warning e 0 errori**; **628 test .NET** (203 dominio + 425 integrazione); API avviata in Release con `Registro delle richieste: 24 comandi, 13 query`; gate di copertura **4/4** e baseline superata (Application 97,68%); `dotnet format whitespace --verify-no-changes` uscita 0; 40 handler e 40 richieste, nessun duplicato. Convenzione scritta nelle istruzioni CQRS. Commit `642b415` pubblicato. Limiti: il riordino non cambia il comportamento, quindi non aggiunge copertura; nessuna verifica nel browser, che qui non serve. |
| 2026-10-02 | Collaudo nel browser della sezione Sessioni e due correzioni | Percorso completo su HTTPS reale (API 5200, admin 4200, schema 3): istanziazione a cascata, Avvia, Pausa, Riprendi, Stop con conferma, rifiuto della seconda sessione sulla stessa classe, ricarica diretta dell'indirizzo, Stop da In attesa. Endpoint pubblici: sessione restituita in `Active` e in `Paused`, assente in `Lobby` e dopo lo Stop. Correzioni: regola «in play» estesa alla pausa con ingressi aperti (specifiche 0.5/0.26, decisione dell'utente) e dialogo di istanziazione con altezza limitata e corpo scorrevole. Gate: build Release, **628 test .NET** (203 + 425) e **153 test Angular**, tutti verdi. Limiti: nessun client alunni collegato (il join arriva con la porzione successiva); la griglia dei partecipanti è un segnaposto. |
| 2026-10-02 | Fine riga: `.gitattributes` e riallineamento dell'albero di lavoro (A-07) | Aggiunto `.gitattributes` (`* text=auto eol=lf`; `*.bat` e `*.cmd` in CRLF; binari esclusi) e ricontrollato l'albero: 620 file in LF e 4 in CRLF (i launcher `.bat`, come vuole `.editorconfig`), `git status` pulito, `git diff` vuoto. `dotnet format whitespace --verify-no-changes --no-restore` esce 0: era il sintomo del rilievo. Dopo la riscrittura dei fine riga su 626 file, build Release e **627 test** (203 dominio, 424 integrazione) restano verdi. Limiti: nessuna verifica nel browser; il riallineamento non cambia il contenuto dei file, solo i fine riga. |
| 2026-10-01 | T3, seconda porzione: ciclo di vita della sessione, regia admin e scoperta pubblica | Restore locked e build Release (uscita 0, 0 errori, nessun warning); **627 test .NET** (203 dominio + 424 integrazione), nessun fallimento/salto; copertura in `TestResults/t3-completo`: Api 84,54%, Application 97,68%, Domain 96,37%, Infrastructure 96,28%, sopra baseline; `assert-dotnet-coverage.ps1` verde e gate **4/4**. Angular: `format:check`, `lint`, `test` e `build` a uscita 0, **153 test** (151 admin + 2 public) riusciti, bundle con i chunk `session-list` e `session-detail`. Aggiunte le transizioni `Pause`/`Resume`/`End`, i comandi `Pause`/`Resume`/`EndSession`, le query `ListSessions`/`GetSession`, la sezione admin delle sessioni e i tre `GET` pubblici di scoperta; migrazione `0003_session_state_check.sql` che corregge il vincolo della 0002. Conteggi: 24 comandi (uno locale) e 13 query. Limiti: nessun `session_participant`, `station`, join, hub, outbox; la regia mostra un segnaposto al posto della griglia dei partecipanti e la sezione non è stata collaudata nel browser. |
| 2026-10-01 | T3, prima porzione: istanziazione e avvio della sessione | Restore locked (uscita 0); build Release 0 errori/0 warning; **561 test** (185 dominio + 376 integrazione), nessun fallimento/salto; copertura in `TestResults/t3-sessioni-1-final`: Api 83,91%, Application 97,83%, Domain 96,18%, Infrastructure 96,28%, baseline invariata; gate 4/4. 44 casi nuovi (14 dominio, 30 integrazione). `CreateSession` e `StartSession` dal dispatcher admin con policy `SessionController`. Nessuna UI e nessun percorso nel browser. |
| 2026-10-01 | Avvio API su database di sviluppo non allineato dopo il consolidamento dello schema | `dotnet run`/`start-server.bat` falliva con `SchemaMigrationException` perché `src/Qwerty.Codi.Api/data/codi.db` proveniva dalle vecchie migrazioni incrementali. Il file è stato archiviato come `src/Qwerty.Codi.Api/data/codi.db.pre-consolidamento-20261001` (nessuna cancellazione) e il servizio ha ricreato lo schema 1 con journal WAL e l'admin della deroga A-09. Limiti: nessuna build o test eseguita; nessun dato operativo toccato. |
| 2026-10-01 | Rianalisi statica del sorgente `f059b9b` e controllo documentale eseguiti dall'agente | Verificati 64 documenti, 38 frontmatter/blocchi YAML, 510 link locali e 28 ancore senza errori, tramite `python ..\tmp\check_codi_docs.py` (script locale di questa revisione, uscita 0). Confrontati nomi del catalogo con i record: 19 comandi, 11 query; otto tabelle applicative nello script unico. Verificati anche i metodi .NET citati dalla matrice nei file di test collegati; rimossi due riferimenti a test cancellati. `git diff --check` riuscito. Nessuna build/test/audit applicativa eseguita: nuova implementazione DA VALIDARE. |
| 2026-09-30 | `dotnet build .\Qwerty.Codi.sln --configuration Release --no-restore` | Riuscita, 0 warning, 0 errori. Avviata durante l'analisi prima della lettura del divieto locale; non ripeterla senza istruzione dell'utente. Usa asset già ripristinati, non dimostra restore pulito né esecuzione dei test. |
| 2026-09-30 | Lettura del codice e delle personalizzazioni | Inventario sopra riportato. Nessun esito runtime dei quattro test disponibile in questa analisi. |
| 2026-09-30 | Validazione statica della configurazione agentica | Validatore skill: 6/6 valide. Parsing YAML e controlli di riferimenti: 34 documenti YAML, 10 agenti, 9 prompt, 9 istruzioni, 6 skill e 121 link locali, nessun errore. Controllati anche destinazioni handoff/delega, nomi dei profili, riferimenti § e coerenza versione 0.7. Non dimostra discovery o comportamento nell'editor. |
| 2026-09-30 | Configurato il ripristino dei pacchetti dentro `Codi/packages` | Aggiunti `NuGet.config` (`globalPackagesFolder=packages`) e `Directory.Build.props` (`RestorePackagesPath=$(MSBuildThisFileDirectory)packages/`). I due valori devono restare identici; `RestorePackagesPath` ha la precedenza su `NUGET_PACKAGES`. |
| 2026-09-30 | Ripristino dei pacchetti nella cartella di progetto | Eseguito dall'utente: `obj/*/project.assets.json` riporta `C:\Development\CorsoInformaticaPrimaria\Codi\packages\` in `packageFolders` e le cartelle dei pacchetti sono presenti in `Codi/packages`. Non dimostra esito di build o test. |
| 2026-09-30 | T0-persistenza: robustezza della factory, bootstrap RepoDB e test | `SqliteConnectionFactory` smaltisce la connessione se apertura o PRAGMA falliscono e `EnableWriteAheadLoggingAsync` restituisce il modo effettivo; aggiunto `RepoDbSqliteBootstrap`; nuovi test per PRAGMA, WAL, smaltimento su errore e rollback al dispose senza commit, più inizializzazione RepoDB nel prototipo esistente. Controllo statico dell'editor sui file toccati: nessun errore. Build e test NON eseguiti in questa fase. |
| 2026-09-30 | `dotnet build .\Qwerty.Codi.sln --configuration Release` (eseguita dall'utente) | Api e ProtocolTests riusciti; IntegrationTests fallito con 1 errore e 2 warning su `SqliteConnectionFactoryTests.cs`: `ServerVersion` astratto non implementato e `CS8765` sui setter di `ConnectionString` e `CommandText`. Corretti entrambi (`ServerVersion` e `[AllowNull]`). |
| 2026-09-30 | `dotnet test .\tests\Qwerty.Codi.IntegrationTests\Qwerty.Codi.IntegrationTests.csproj --filter Category=Integration` (eseguita dall'utente) | Build riuscita e 8 test: 7 riusciti, 1 fallito. Unico fallimento: `LetturaConRepoDb_MappaColonneSnakeCaseSuProprietaPascalCase` con `TypeLoadException` su `RepoDb.DbHelpers.SqLiteDbHelper.GetScopeIdentity` (assembly `RepoDb.SqLite 1.1.4.2`): confermata l'ipotesi di A-05. Riusciti PRAGMA effettivi, WAL, smaltimento su errore e rollback al dispose senza commit. |
| 2026-09-30 | Sostituzione del provider RepoDB (risoluzione A-05) | `Qwerty.Codi.Infrastructure.csproj`: rimossi `RepoDB 1.16.0` e `RepoDB.SqLite 1.1.4`, aggiunti `RepoDb 1.14.0` e `RepoDb.Sqlite.Microsoft 1.14.0` (coppia allineata); `RepoDbSqliteBootstrap` ora usa `RepoDb.SqliteBootstrap.Initialize()`. Richiede un nuovo restore; nessuna build o test ancora eseguita su questa combinazione. |
| 2026-09-30 | `dotnet test .\tests\Qwerty.Codi.IntegrationTests\Qwerty.Codi.IntegrationTests.csproj --filter Category=Integration` dopo la sostituzione (eseguita dall'utente) | Build riuscita; bootstrap RepoDB funzionante (nessun `TypeLoadException`). 7 test su 8 verdi. Resta rosso `LetturaConRepoDb_MappaColonneSnakeCaseSuProprietaPascalCase` con `InvalidOperationException`: nessuna coercizione da `System.String` a `System.Guid` per `PrototypeRow.Id`. Il riepilogo segnala anche 1 warning non identificato. |
| 2026-09-30 | Correzione della porzione T0-persistenza (F1–F3 della revisione) | `PrototypeRow.Id` è ora la stringa canonica minuscola del UUID e l'asserzione del prototipo confronta `id.ToString("D")`; la factory non lascia che un errore di dispose maschera la causa originale; aggiunto il test di smaltimento su apertura annullata; i test con connessione fittizia sono classificati `Unit`. Controllo statico dell'editor sui file toccati: nessun errore. |
| 2026-09-30 | `dotnet test .\tests\Qwerty.Codi.IntegrationTests\Qwerty.Codi.IntegrationTests.csproj` (eseguita dall'utente) | Build riuscita, nessun warning segnalato, 9 test su 9 riusciti (1,1 s). Verificati PRAGMA effettivi, WAL effettivo, smaltimento su errore e su apertura annullata, rollback al dispose senza commit, mapping snake_case→PascalCase con UUID canonico sulla coppia `RepoDb 1.14.0` + `RepoDb.Sqlite.Microsoft 1.14.0`. **Chiude la porzione T0-persistenza.** |
| 2026-09-30 | T0-compatibilità Mediator: aggiunto il riferimento ai pacchetti | `Qwerty.Codi.IntegrationTests.csproj` referenzia `Mediator.Abstractions` 2.1.7 e `Mediator.SourceGenerator` 2.1.7 (`PrivateAssets="all"`). Nessuna verifica: il prototipo e i test si scrivono dopo il restore, leggendo l'API sui documenti del pacchetto ripristinato in `Codi/packages`; l'API non si deduce dal nome del pacchetto. |
| 2026-09-30 | `dotnet restore .\Qwerty.Codi.sln --force` (eseguita dall'utente) | Fallita con `NU1102`: con l'ID `Mediator` non esiste la 2.1.7 (nuget.org espone solo 1.0.2123, un omonimo). La v2 non ha meta-pacchetto: servono i due satellite, `Mediator.Abstractions` e `Mediator.SourceGenerator` alla stessa versione. Corretto il csproj; restore da rieseguire. |
| 2026-09-30 | `dotnet restore .\Qwerty.Codi.sln --force` dopo la correzione (eseguita dall'utente) | Riuscita: `Mediator.Abstractions` 2.1.7 e `Mediator.SourceGenerator` 2.1.7 sono in `Codi/packages`. La documentazione XML del pacchetto è vuota, quindi l'API è stata verificata con il language server sull'assembly ripristinato: nessun errore. |
| 2026-09-30 | T0-compatibilità Mediator: prototipo e test scritti | Nuovi `tests/Qwerty.Codi.IntegrationTests/Dispatch/MediatorCompatibilityPrototype.cs` (richiesta con risposta, sonda sul token, notifica con due destinatari, registro iniettato) e `MediatorCompatibilityTests.cs` (risposta dell'handler, propagazione del token, fan-out della notifica). Il mediator si risolve da uno scope per non dipendere dalla durata configurata. Build e test NON eseguiti in questa fase. |
| 2026-09-30 | `dotnet test .\tests\Qwerty.Codi.IntegrationTests\Qwerty.Codi.IntegrationTests.csproj` (eseguita dall'utente) | Build riuscita e 12 test su 12 in 1,1 s: i 9 della persistenza più i 3 di compatibilità Mediator. **Chiude la porzione T0-compatibilità Mediator**: Mediator 2.1.7 con generatore di sorgenti compila, registra gli handler nel container, propaga il token fino all'handler e distribuisce la notifica a entrambi i destinatari su .NET 10. |
| 2026-09-30 | Scaffold Angular creato con il CLI (eseguito dall'utente) | `npx @angular/cli@latest new` in `src/Qwerty.Codi.Web`: Angular 22.2.0, TypeScript 6.0.2, vitest 5, jsdom 30, prettier 3.8, npm 11.12.1 su Node 24.15.0 (le versioni sono quelle generate dal CLI, non scelte a mano). Il progetto è **zoneless**: `provideBrowserGlobalErrorListeners`, nessuna dipendenza `zone.js`. Route ancora vuote e `tsconfig` senza alias di percorso. Lockfile e `node_modules` restano dentro la cartella del progetto. |
| 2026-09-30 | `npm install blockly` (eseguito dall'utente) | Fallita con `ERESOLVE`: `blockly@13.3.0` dichiara `peer jsdom ">=27.4.0 <30.0.0"`, mentre lo scaffold fissa `jsdom@^30.0.0`. Risolto allineando `jsdom` alla finestra dichiarata da Blockly e aggiungendo `blockly` `^13.3.0` a `package.json`. `--force` e `--legacy-peer-deps` scartati perché accettano una risoluzione non validata dall'autore di Blockly. |
| 2026-09-30 | `npm install` con `package.json` allineato (eseguito dall'utente) | Riuscito: 271 pacchetti, 0 vulnerabilità. `blockly` 13.3.0 è in `node_modules` dentro la cartella del progetto, quindi le sue API sono state lette dai tipi installati. |
| 2026-09-30 | T0-Blockly: round trip del workspace nello scaffold Angular | Nuovi `src/app/exercise-engine/workspace-serialization.ts` (salva con la serializzazione Blockly, ricarica sostituendo il contenuto, sopprime gli eventi e li ripristina anche in errore, rifiuta i testi che non sono uno stato), `prototype-block.ts` e `workspace-serialization.spec.ts` (round trip, ricarica ripetuta senza duplicati, rifiuto dell'input non valido, soppressione eventi già attiva nel chiamante). Pagina minima in `src/app/features/exercise-authoring/blockly-workspace-prototype/` con route lazy. Controllo del language server sui file nuovi e sui tipi di Blockly in `node_modules`: nessun errore. |
| 2026-09-30 | `npm run test -- --watch=false` e `npm run build` (eseguiti dall'utente) | Vitest: 10 test su 10 (8 del round trip, 2 dello scaffold), 1,45 s. Build riuscita: initial 233,39 kB (65,25 kB di trasferimento), chunk lazy Blockly 753,44 kB (158,31 kB). Il budget di 500 kB non è toccato perché Blockly è fuori dal bundle iniziale. Tre warning `CommonJS or AMD dependencies` per `blockly/core`, `blockly/msg/en` e `blockly/blocks`. **Chiude la porzione T0-Blockly.** |
| 2026-09-30 | Dichiarazione della dipendenza CommonJS di Blockly | `angular.json`: `allowedCommonJsDependencies: ["blockly"]` nel builder di build, per dichiarare accettata la dipendenza CommonJS invece di lasciare il warning a ogni build. È un filtro dei warning: nessun effetto sul comportamento. Il pacchetto espone `import: ./index.mjs`, ma il bundler risolve `default: ./index.js`; se una versione futura facesse risolvere l'ESM, la dichiarazione si può rimuovere. Build non rieseguita. |
| 2026-09-30 | Blocco delle dipendenze NuGet (§3.2) | Nuovo `Directory.Packages.props` con `ManagePackageVersionsCentrally` e le otto versioni già adottate e validate in T0; `Directory.Build.props` attiva `RestorePackagesWithLockFile`; rimosso l'attributo `Version` dalle quattro csproj che lo avevano. Controllo statico: nessun errore e nessun `PackageReference` con `Version` residuo. |
| 2026-09-30 | `dotnet restore`, `dotnet build --configuration Release` e `dotnet test` sull'intera soluzione (eseguiti dall'utente) | Restore riuscito e `packages.lock.json` presente in `src/Qwerty.Codi.Infrastructure` (verificato): la gestione centralizzata non ha prodotto errori NU1008 e il lockfile viene generato. Build Release riuscita con 1 warning; test: 12 su 12 riusciti. **Chiude la porzione del blocco delle dipendenze NuGet.** |
| 2026-09-30 | Warning `CS0618` sul bootstrap RepoDB e correzione | La build segnala che `SqliteBootstrap.Initialize()` è deprecato: «This class will soon to be hidden as internal class. Use the 'GlobalConfiguration.Setup().UseSqlite()' method instead». `RepoDbSqliteBootstrap` ora usa `GlobalConfiguration.Setup().UseSqlite()`. Controllo del language server: nessun errore. |
| 2026-09-30 | `dotnet build --configuration Release` e `dotnet test` dopo la correzione (eseguiti dall'utente) | Build Release **senza warning** e 12 test su 12 riusciti (1,4 s): la nuova inizializzazione registra il provider e il test RepoDB lo dimostra a runtime. Restano i due soli warning attesi `No test is available`, per i progetti di test ancora privi di casi. **Chiude T0.** |
| 2026-09-30 | Chiusura dei rilievi D1–D5 del prototipo Blockly | D1 corretto in modo diverso da come l'aveva descritto la revisione: verificando i tipi installati, Blockly **solleva** per un tipo non registrato (`Block@throws When the prototypeName is not valid or not allowed`), quindi il rischio concreto non era lo scarto silenzioso ma un workspace lasciato a metà. `deserializeWorkspaceJson` ora conta i blocchi dello stato, confronta l'esito dopo il caricamento e, su rifiuto, ripristina il contenuto precedente; il messaggio arriva all'utente dal tipo `InvalidWorkspaceStateError`. D2: `registerPrototypeBlock` idempotente. D3: host Blockly con `role="group"` e nome accessibile, pulsanti a 48 px e `:focus-visible`. D4: route spostata su `admin/workspace-prototype`, dentro l'area docente di §14.1. D5: alias `@exercise-engine/*` in `tsconfig.json` al posto degli import relativi profondi. Due test nuovi (blocco ignoto, registrazione ripetuta). |
| 2026-09-30 | `npm run test -- --watch=false` (eseguita dall'utente) | Fallita con `TS5090`: senza `baseUrl` i percorsi di `paths` devono essere relativi. Alias corretto in `./src/app/exercise-engine/*`. Il language server non aveva segnalato l'errore: il controllo statico non sostituisce la build. Test non eseguiti. |
| 2026-09-30 | `npm run test -- --watch=false` dopo la correzione dell'alias (eseguita dall'utente) | 12 test su 12 in 1,42 s: 10 del prototipo (inclusi i due nuovi sul blocco ignoto e sulla registrazione ripetuta) e 2 dello scaffold. La generazione del bundle è parte di `ng test`, quindi l'alias risolve anche in compilazione. **Valida le correzioni D1–D5.** |
| 2026-09-30 | `npm run build` (eseguita dall'utente) | Build di produzione riuscita **senza warning**: il filtro `allowedCommonJsDependencies` elimina i tre avvisi CommonJS. Initial 233,38 kB (65,23 kB di trasferimento) e Blockly nel chunk lazy a 754,38 kB (158,69 kB): budget rispettati. **Chiude T0 anche nella forma di produzione.** |
| 2026-09-30 | T1, prima porzione: configurazione, avvio del database e readiness | Nuovi in Infrastructure: `Configuration/CodiOptions.cs` (sezione `Codi` di §18.2), `Persistence/SqliteDatabase.cs` (percorso risolto contro la base del servizio, con validazione), `Persistence/DatabaseBootstrap.cs` (directory dati, provider RepoDB, WAL verificato). `SqliteConnectionFactory` costruisce la stringa di connessione dalle impostazioni: chiavi esterne anche nella stringa (§20.6), pooling disattivato, `busy_timeout` dal valore configurato (§8.1). In Api: `Configuration/CodiServiceCollectionExtensions.cs` (binding, validazione, `ValidateOnStart`, percorso risolto), `Health/DatabaseHealthCheck.cs`, `appsettings.json` e `Program.cs` (preparazione prima dell'ascolto, `GET /health/ready`). Nuovi test: risoluzione dei percorsi, validazione della configurazione, preparazione con WAL e provider RepoDB usabile, attesa configurata. Controllo del language server: nessun errore; build e test NON eseguiti. |
| 2026-09-30 | `dotnet build .\Qwerty.Codi.sln --configuration Release` su T1 (eseguita dall'utente) | Unico errore: `CS0246`, `HostingEnvironment` non trovato — l'implementazione concreta di `IHostEnvironment` non è pubblica. Sostituito con uno stub di test che espone `ContentRootPath`. Nuova build non eseguita. Da notare: il language server non aveva segnalato l'errore, come già con l'alias di `tsconfig`. |
| 2026-09-30 | `dotnet build` e `dotnet test` sulla prima porzione di T1 (eseguiti dall'utente) | Build Release riuscita con i **soli due warning attesi** `No test is available` (Domain.Tests e ProtocolTests) e 19 test su 19 in 2,2 s. Il conteggio conferma i quattro nuovi della preparazione del database e i due della configurazione: le API in discussione (`SqliteConnectionStringBuilder`, `ValidateOnStart`, `MapHealthChecks`, `SqliteDatabase.FromOptions`) funzionano a runtime, non solo in compilazione. **Chiude la prima porzione di T1.** |
| 2026-09-30 | T1, seconda porzione: migrazioni numerate e schema iniziale | Nuovi in `Infrastructure/Migrations/`: `MigrationScript` (versione, nome, SQL, impronta SHA-256 calcolata dallo script e normalizzata sui fine riga), `MigrationCatalog` (numerazione continua da 1, duplicati e buchi rifiutati, script letti come risorse incorporate), `MigrationRunner` (applicazione atomica per migrazione con transazione, ispezione senza scritture, divergenze per impronta errata, versione sconosciuta o buco nella sequenza, punto di cancellazione fra migrazioni), `MigrationStatus`, `SchemaMigrationException` e `Scripts/0001_admin_identity.sql` (`admin_user`, `credential_session` con vincoli di §7.2). In `Persistence/`: `DatabasePreparation`, `DatabaseReadiness` e `DatabaseReadinessProbe`; `DatabaseBootstrap.PrepareAsync` ora restituisce journal e versione di schema; `DatabaseHealthCheck` delega alla sonda e distingue journal da schema. Registrazioni DI aggiornate. Test nuovi: catalogo, runner, vincoli dello schema iniziale e prontezza. Controllo del language server: nessun errore; build e test NON eseguiti. |
| 2026-09-30 | `dotnet build` e `dotnet test` sulla seconda porzione di T1 (eseguiti dall'utente) | Build Release riuscita con i soli due warning attesi e **44 test su 44** in 2,6 s, esattamente i 19 precedenti più i 25 nuovi. Il conteggio conferma che le migrazioni sono state applicate davvero: i test dei vincoli dello schema passano solo se `0001` è stata eseguita su un database reale e `SQLITE_CONSTRAINT` è il codice di errore atteso. **Chiude la seconda porzione di T1.** |
| 2026-09-30 | T1, terza porzione: dispatcher CQRS | Nuovi in `Application/Dispatch/`: `CodiPolicy`, `CodiRequestKind`, `CodiRequestChannel`, `ICodiRequest` con nome pubblico e policy come membri statici, `ICodiCommand<T>`, `ICodiQuery<T>`, `ILocalOnlyRequest`, `CodiRequestContext`, `AnonymousBootstrapRequests` (elenco chiuso di §10.4), `CodiErrorCodes`, `CodiRequestException`, `CodiRequestDescriptor`, `CodiRequestRegistry` (scoperta, unicità dei nomi per canale, regole dell'accesso anonimo), `CodiRequestExecutor` (canale, policy, deserializzazione, invocazione), `ICodiRequestInvoker` con `MediatorRequestInvoker<,>`, `CodiJson` e `CodiDispatchServiceCollectionExtensions`. In `Api/Dispatch/`: `DispatcherEndpoints` (i due endpoint di §10.1), `CodiErrorStatus`, `CodiRequestExceptionHandler`, `ICodiRequestContextProvider` con implementazione anonima, `DispatchServiceCollectionExtensions`. `Program.cs` compone dispatcher, gestore delle eccezioni e registro risolto prima dell'ascolto. Test nuovi: registro, esecuzione e traduzione degli errori. Controllo del language server: nessun errore; build e test NON eseguiti. |
| 2026-09-30 | `dotnet build` sulla terza porzione (eseguita dall'utente) | Fallita con **2 errori `CS0121`** e 17 warning. Causa: `Mediator.SourceGenerator` emette in ogni assembly che lo referenzia un tipo **pubblico** di registrazione (`MediatorDependencyInjectionExtensions`, `Mediator`, `MediatorOptions` in `Microsoft.Extensions.DependencyInjection`). Con il generatore in `Application` e nel progetto di test, la chiamata `AddMediator` è ambigua in quest'ultimo (`MediatorCompatibilityTests.cs:70`, `CodiRequestExecutorTests.cs:296`). I warning erano `CS0436` (tipi generati in conflitto fra i due assembly) e `MSG0005` (richieste di prova senza handler). Il language server non aveva segnalato nulla: non vede il codice generato. |
| 2026-09-30 | Correzione della terza porzione dopo la build | Il generatore resta **solo** in `Application`: rimosso da `Qwerty.Codi.IntegrationTests.csproj` (resta `Mediator.Abstractions` per le richieste di prova). Ne derivano le tre conseguenze risolte: niente più ambiguità né conflitti di tipi generati, e nessun `MSG0005` perché il generatore non analizza più questo progetto. I test di esecuzione non possono più comporre un mediator per le proprie voci, quindi usano un'invocazione controllata (`RecordingInvoker`) su un catalogo esplicito di descrittori (nuovo `CodiRequestRegistry.FromDescriptors`); un test verifica comunque il percorso reale fino all'invocazione vera, asserendo l'errore esplicito quando il mediator non è registrato. `MediatorCompatibilityTests` non esegue più handler di prova: verifica che la registrazione generata renda risolvibile il mediator e che il mediator provenga dall'assembly delle richieste. Il file `MediatorCompatibilityPrototype.cs` resta inerte nel progetto di test e va rimosso. Controllo del language server: nessun errore; build e test NON eseguiti. |
| 2026-09-30 | `dotnet build` dopo la correzione (eseguita dall'utente) | Altri **3 errori `CS0103`** su `log` in `CodiRequestExecutorTests.cs`: due test usavano ancora la forma `var (executor, _)` mentre le nuove asserzioni leggono l'aggregato delle invocazioni. Corretti. Il controllo statico non li aveva segnalati: il language server non aveva rianalizzato il file appena modificato. Build e test ancora da rieseguire. |

| 2026-09-30 | `dotnet test` dopo la correzione (eseguita dall'utente) | **73 test su 74**, i sette progetti compilati e i due soli warning attesi. Il conteggio conferma le parti che contano: la registrazione generata produce il mediator e il mediator proviene dall'assembly delle richieste; il percorso reale fino all'invocazione vera solleva l'errore esplicito quando il mediator manca; registro, canale, policy, payload e traduzione degli errori funzionano a runtime. Unico fallimento, `ExecuteAsync_ConCorpoIncompleto_SollevaInvalidPayload`: un corpo `{}` per un comando con un campo obbligatorio veniva accettato con il campo a `null` invece di essere rifiutato. **Corretto in `CodiJson.Payload`** con `RespectRequiredConstructorParameters`: il vincolo «il corpo deve soddisfare il record» diventa una regola del serializzatore, non una difesa da ripetere in ogni caso d'uso. Build e test: **74 su 75**. L'opzione esiste e funziona — il corpo incompleto ora è rifiutato — ma la metà «annullabile» è risultata **falsa**: `RespectRequiredConstructorParameters` considera obbligatorio anche un parametro annullabile privo di valore predefinito. Regola adottata, misurata e non dedotta: un campo è obbligatorio a meno che il record della richiesta gli dia un valore predefinito, quindi l'opzionalità si dichiara nel record. Aggiornati il record di prova e il commento dell'opzione; build e test da rieseguire, attesi 75. |
| 2026-09-30 | `dotnet test .\Qwerty.Codi.sln` (eseguita dall'utente) | **75 test su 75**, i sette progetti compilati, i due soli warning attesi. **Chiude la terza porzione di T1**: il dispatcher di §10.1 funziona a runtime — registro, canale, policy, payload e traduzione degli errori — e le due proprietà strutturali sono verificate (il mediator è generato in `Application` e il percorso reale arriva fino all'invocazione). Resta senza test automatico il solo instradamento dei due endpoint, che richiede il servizio avviato. |

| 2026-09-30 | T1, quarta porzione: identità amministrativa persistente | Nuovi in `Domain/Admin/`: `AdminUsername` (ammissibilità e normalizzazione: spazi, accenti e maiuscole non creano account distinti) e `AdminUser` (nome digitato e nome normalizzato insieme, impronta della password, marca di sicurezza, stato, versione di riga, `Create`/`Restore` con validazione dei dati letti). Nuovi in `Application/Abstractions/`: `IAdminUserRepository` con esiti di ritorno per i conflitti previsti e `IAdminPasswordHasher` (impronta e verifica, algoritmo fuori dal dominio). Nuovo in `Infrastructure/Persistence/Repositories/`: `AdminUserRow` e `AdminUserRepository` su `admin_user`, con identificativo in stringa canonica e conversioni concentrate nel mapping (A-06), inserimento che distingue il conflitto di unicità dagli altri vincoli violati ed esistenza di amministratori attivi per la condizione del bootstrap. Registrato in DI con durata limitata all'operazione. Test nuovi: 18 casi sulla normalizzazione, 10 sull'entità e 5 sul repository. **Il progetto `Domain.Tests` non è più vuoto**: il suo warning `No test is available` scompare. Controllo del language server: nessun errore; build e test NON eseguiti. Manca ancora il comando `BootstrapAdminAccount` con il suo handler, l'implementazione dell'impronta, la modalità di riga di comando e i test attraverso il dispatcher reale. |

| 2026-09-30 | `dotnet build` sulla quarta porzione (eseguita dall'utente) | 2 errori in `AdminUserRepository`: `CS0019` e `CS0021`, perché `Count` e l'indice non esistono su `IEnumerable<T>`, che è ciò che restituisce RepoDB. Sostituiti con `FirstOrDefault`, che è anche più onesto: il nome normalizzato è unico, quindi la riga è al più una e non serve materializzare una collezione. Da notare: **quinta volta** che il controllo statico non vede un errore che il compilatore trova — il language server non analizza i file scritti nello stesso turno, oltre a non vedere il codice generato. Build da rieseguire. |
| 2026-09-30 | `dotnet test` sulla quarta porzione (eseguita dall'utente) | **107 su 108**. L'unico fallimento è un'attesa **sbagliata nel test**, non un difetto del codice: avevo elencato `"Mae\tstra"` fra le scritture equivalenti di «Maestra», ma la regola adottata riduce gli spazi interni a uno solo invece di eliminarli, quindi `mae stra` è diverso da `maestra` — ed è la scelta corretta, perché eliminando gli spazi `AnnaMaria` e `Anna Maria` diventerebbero lo stesso account. Il caso è stato spostato nella teoria che documenta la regola. Il resto della porzione passa: 18 casi di normalizzazione, 10 sull'entità, 5 sul repository. Build e test da rieseguire. |
| 2026-09-30 | `dotnet build` e `dotnet test` sulla parte scritta della quarta porzione (eseguiti dall'utente) | Build Release riuscita e **108 test su 108** in 1,3 s, con **un solo warning** `No test is available`, quello di ProtocolTests: `Domain.Tests` ora ha 28 casi e il suo avviso è scomparso, come previsto. Validati dominio, astrazioni e repository amministrativo; la porzione resta aperta su comando, handler, impronta, riga di comando e test end-to-end. |
| 2026-09-30 | T1, quarta porzione: comando, handler, impronta e riga di comando | Nuovi in `Application/Admin/`: `BootstrapAdminAccountCommand` (richiesta anonima e locale, nome e policy dichiarati dal record), `AdminAccountCreatedDto` (solo riferimenti all'account, mai password o impronta) e `BootstrapAdminAccountHandler`, che verifica nell'ordine nome utilizzabile, password presente e assenza di amministratori attivi, poi delega l'unicità alla tabella e distingue i due conflitti con due codici. Nuovi codici in `CodiErrorCodes` (`InvalidName`, `ValidationFailed`, `BootstrapNotAllowed`, `AdminAccountAlreadyExists`) con i loro stati in `CodiErrorStatus`. Nuovi in `Api/Authentication/`: `AspNetCorePasswordHasher` su `PasswordHasher<TUser>` del framework condiviso e la relativa registrazione. `Program.cs`: modalità locale `--bootstrap-admin`, con la password letta da `CODI_BOOTSTRAP_PASSWORD` e mai dalla riga di comando (§9.1), che esce senza mettere in ascolto il servizio. Test nuovi: 6 end-to-end attraverso il percorso reale — creazione con impronta verificabile e non in chiaro, secondo bootstrap rifiutato, canale HTTP che non conosce il comando, nome non utilizzabile, password vuota e nome occupato da un account non attivo. Controllo del language server: nessun errore; build e test NON eseguiti. |

| 2026-09-30 | `dotnet build` sulla quarta porzione completa (eseguita dall'utente) | 1 errore `CS0246` in `BootstrapAdminAccountTests`: mancavano due `using` — `Microsoft.Extensions.DependencyInjection` per `ServiceProvider` e `Qwerty.Codi.Api.Authentication` per l'impronta. Il secondo restava nascosto dal primo, perché il compilatore non analizza i corpi dei metodi di un tipo che ha già una dichiarazione errata: conviene correggere i `using` guardando il file intero, non solo l'errore riportato. **Sesta conferma** del pattern: il controllo statico non analizza i file scritti nello stesso turno. Aggiunti entrambi; build e test da rieseguire. |
| 2026-09-30 | `dotnet test` sulla quarta porzione e sul primo pezzo della quinta (eseguita dall'utente) | **140 test su 140**, build Release riuscita e il solo warning noto. **Chiude la quarta porzione di T1**: i 6 test end-to-end del bootstrap confermano ciò che l'esecuzione manuale aveva mostrato una volta, e i 26 nuovi confermano le regole del blocco e della credenziale. |
| 2026-09-30 | `dotnet test` sull'accesso amministrativo (eseguita dall'utente) | **144 su 149**: i 9 test dell'accesso amministrativo passano, e falliscono i 5 del bootstrap, per una ragione che vale più dell'errore: **Mediator costruisce la cache dei servizi alla prima invocazione e vi risolve tutti gli handler registrati**, non solo quello invocato. Mancava `ICredentialSessionRepository` nella composizione dei test del bootstrap, che non c'entrava con `LoginAdminHandler`, e per questo nessuna richiesta funzionava più — con un errore che nominava un handler estraneo alla prova. Corretto quel costruttore e documentato il vincolo dove si compone: chi registra deve coprire le dipendenze dell'intero catalogo. Implicazione per le porzioni successive: ogni nuovo handler con dipendenze nuove va riflesso in ogni composizione, servizio compreso, e un test che esegue una richiesta qualsiasi è la guardia che lo rileva. Build e test da rieseguire. |
| 2026-09-30 | `dotnet run --project .\src\Qwerty.Codi.Api -- --bootstrap-admin Maestra` (eseguita dall'utente) | Il registro all'avvio riporta `1 comandi, 0 query, 2 richieste anonime non ancora implementate`: la scoperta trova il comando reale e la diagnosi nomina esattamente le due mancanti, `JoinSession` e `LoginAdmin`. La preparazione applica `schema 1` in `journal_mode wal`, l'handler ha creato l'account (`AdminUserId = a1595746-19c9-420c-8386-6630ef898d05`, `Username = Maestra`) e la password non compare in nessuna riga. Prova che funzionano insieme: riga di comando, preparazione del database, registro con le regole dell'accesso anonimo, deserializzazione, mediator generato in `Application`, handler, repository e impronta. Non prova il secondo bootstrap rifiutato né l'uscita con codice diverso da zero, e non sostituisce i 6 test automatici: un'esecuzione riuscita mostra che funziona una volta, i test che continua a funzionare. |
| 2026-09-30 | T1, quinta porzione: accesso amministrativo, dominio e persistenza | Nuovo in `Domain/Credentials/`: `CredentialSession` con tipo di soggetto, marca di sicurezza portata dal ticket, scadenza assoluta, revoca che si registra una volta sola e ricostruzione validata; `CredentialPrincipalKind`. In `Domain/Admin/AdminUser`: tentativi falliti, blocco dopo la soglia che **non si prolunga** mentre è attivo e scatta di nuovo dopo la scadenza, azzeramento sull'accesso riuscito, istante di ultima modifica e `WithRowVersion`. Nuove astrazioni: `ICredentialSessionRepository` e `IAdminUserRepository.TryUpdateAsync` con controllo della versione di riga. In `Infrastructure/Persistence/Repositories/`: `CredentialSessionRow` e `CredentialSessionRepository` (inserimento, lettura, revoca condizionata all'assenza di una revoca precedente), `AdminUserRow` e `AdminUserRepository` estesi alle nuove colonne e all'aggiornamento. Registrato in DI con durata limitata all'operazione. Test nuovi: 6 sul blocco, 13 sulla credenziale, 5 sul repository delle credenziali e 2 sull'aggiornamento dell'account. Controllo del language server: nessun errore; build e test NON eseguiti. Mancano i comandi `LoginAdmin` e `LogoutAdmin` con i loro handler, i due endpoint dedicati di §10.3 con lo schema a cookie `__Host-CodiAdmin` e il contesto reale al posto di quello anonimo. |
| 2026-09-30 | `dotnet test` dopo la correzione della composizione (eseguita dall'utente) | **149 test su 149** e build Release riuscita. **Chiude il livello applicativo della quinta porzione di T1** — regole del blocco, credenziale persistita, repository e comandi `LoginAdmin`/`LogoutAdmin` — e conferma a runtime il vincolo di Mediator misurato poco prima: l'esecuzione che falliva su cinque test non correlati ora passa senza aver toccato `LoginAdminHandler`. |
| 2026-09-30 | T1, quinta porzione: trasporto dell'accesso amministrativo (§9.1, §10.3) | Nuovi in `Api/Authentication/`: `CodiClaimTypes` (identificativo e marca della credenziale nei claim), `AdminCookieAuthentication` (schema `CodiAdmin`, cookie `__Host-CodiAdmin` con `Secure`, `HttpOnly`, `SameSite=Strict` e durata assoluta), `CookieRequestContextProvider` (rilegge la credenziale a ogni richiesta e la esige utilizzabile: la revoca toglie il permesso a un ticket ancora formalmente valido, §9.2) e `AdminAuthenticationEndpoints` (`POST /auth/admin/login` e `POST /auth/admin/logout`, che emettono e revocano il cookie e delegano ai comandi). `ICodiRequestContextProvider` è diventato asincrono, perché una credenziale si verifica contro il database e non solo contro il cookie; il provider è registrato `Scoped`, perché come `Singleton` avrebbe dipeso da servizi con durata minore. Controllo del language server: nessun errore; build e test NON eseguiti. |
| 2026-09-30 | Composizione verificabile e validazione delle durate (§18.2, §20.2) | La composizione dei servizi è stata estratta in `CodiServiceCollectionExtensions.AddCodiApi`, chiamata sia da `Program` sia dai test: se i test ricopiassero le registrazioni, potrebbero divergere da quelle vere senza che nulla lo segnali. `Program` chiede `ValidateOnBuild` e `ValidateScopes` in tutti gli ambienti, così una durata incoerente o una dipendenza mancante emergono all'avvio invece che alla prima richiesta. Nuovo `tests/Qwerty.Codi.IntegrationTests/Composition/CodiApiCompositionTests.cs`: costruzione validata, esecuzione reale di `LoginAdmin` su un database preparato dalla composizione (deve arrivare `CredentialsRejected`, non un errore di risoluzione) e verifica che senza cookie non si ottenga nulla. Controllo del language server: nessun errore; build e test NON eseguiti. |
| 2026-09-30 | Scaffold Web di T1 (§20.5, §20.7) | Nuovi `eslint.config.js` (flat config con regole Angular e integrazione con Prettier), `.prettierignore`, gli script `lint`, `format:check`, `audit:runtime` e `format`; i barrel `src/app/core/api/index.ts` e `src/app/shared/components/index.ts`; gli alias `@core/*` e `@shared/*` accanto a `@exercise-engine/*`; README del frontend riscritto con i comandi. I pacchetti di lint sono stati aggiunti come intervalli **non verificati** (nessun accesso al registro): `angular-eslint`, `eslint`, `@eslint/js`, `typescript-eslint`, `eslint-config-prettier`. `npm install` è un prerequisito per tutti i comandi del frontend, quindi il lockfile è da rigenerare. |
| 2026-09-30 | Antiforgery delle mutazioni e log strutturati (§10.2, §18.4, §17.2) | Nuovo `Api/Authentication/CodiAntiforgery.cs`: impostazioni del token (cookie `__Host-CodiAntiforgery` leggibile dal frontend, `Secure`, `SameSite=Strict`, intestazione `X-Codi-Antiforgery`) e middleware che verifica le **mutazioni autenticate con cookie**, dispatcher compreso, lasciando passare letture e richieste senza credenziale. Il rifiuto è un errore applicativo previsto: nuovo codice `AntiforgeryRejected` (400) con la sua mappatura in Problem Details. I due endpoint del dispatcher ora registrano una riga strutturata per richiesta con kind, nome, canale, esito, codice di errore e `traceId`, senza corpo: il payload può contenere dati di persone e contenuti dei progetti. Controllo del language server: nessun errore; build e test NON eseguiti. |
| 2026-09-30 | `dotnet package search coverlet.collector` (eseguita dall'utente) | Corrispondenza reale su nuget.org: **10.1.0** (l'altro risultato, `Tamp.Coverlet.V6`, è un pacchetto diverso). La versione ipotizzata 6.0.2 era sbagliata ed è stata corretta in `Directory.Packages.props`. La ricerca interroga anche una sorgente locale `Component space (file:///C:/SAML/Bin)` che non contiene il pacchetto: se un giorno ospitasse un identificativo omonimo avrebbe la precedenza su nuget.org, quindi conviene tenerla d'occhio. Resta da verificare la compatibilità del collector 10.x con la piattaforma di test: lo dirà il primo `dotnet test` con la raccolta attiva. Restano non verificate le versioni dei pacchetti di lint del frontend. |
| 2026-09-30 | `dotnet restore` sui tre progetti di test (eseguita dall'utente) | `MSB4025`: commento XML non valido, perché conteneva la sequenza `--` (vi avevo scritto l'opzione `--collect`). Corretto nei tre csproj; il restore va ripetuto. Il language server non segnala l'XML di MSBuild, quindi l'unico controllo possibile è il restore o la build: nessun test di regressione è scrivibile per un file di progetto, e per §20.3 la verifica è il comando. |
| 2026-09-30 | `dotnet build .\Qwerty.Codi.sln --configuration Release --no-restore` (eseguita dall'utente) | Sette progetti su otto compilati; **Api fallita con `CS0246`** su `ICodiRequestContextProvider` in `CookieRequestContextProvider.cs`: mancava il using verso `Qwerty.Codi.Api.Dispatch`, perché quel tipo vive nell'assembly dell'API e non in quello applicativo. Il language server aveva dichiarato "nessun errore" sul file: **ottava occorrenza** della stessa lezione in questa sessione. Aggiunto il using anche al test di composizione, che usa lo stesso tipo e sarebbe fallito al giro successivo. Build e test da rieseguire. |
| 2026-09-30 | `dotnet build .\Qwerty.Codi.sln --configuration Release --no-restore` dopo la correzione del using (eseguita dall'utente) | `CS1061` in `CodiServiceCollectionExtensions.cs`: mancava il using verso `Qwerty.Codi.Api.Authentication`, dove vive `AddCodiAuthentication`. **Nona occorrenza** della stessa lezione: il metodo di estensione è invisibile finché non si importa il suo namespace, e il language server non l'ha segnalato. Corretto; build da rieseguire. |
| 2026-09-30 | `dotnet test .\Qwerty.Codi.sln --configuration Release --no-build` (eseguita dall'utente) | **163 test: 158 riusciti, 5 falliti** (3 di composizione, 2 di antiforgery). I due difetti sono diversi per natura, e il primo è un difetto di produzione trovato dal test appena scritto: **Mediator registra mediator e handler come `Singleton`**, mentre i repository hanno durata limitata all'operazione (§4.3) — `ValidateScopes` lo rifiuta all'avvio, quindi il servizio non sarebbe partito affatto. Corretto dichiarando `ServiceLifetime.Scoped` in `AddCodiRequests`: la validazione voluta per la composizione ha pagato al primo giro. Il secondo è un difetto dei test: il cookie del token antiforgery è `Secure`, quindi la verifica pretende una richiesta HTTPS e i due casi che generavano o validavano il token su HTTP fallivano con `InvalidOperationException` invece dell'errore previsto. Corretto nei test con `IsHttps = true`. Il messaggio dell'errore di antiforgery, incapsulato, resta un `InvalidOperationException` e non un rifiuto applicativo: una configurazione incoerente non deve presentarsi come token sbagliato, ed è il comportamento voluto. Il progetto `Qwerty.Codi.ProtocolTests` resta senza casi con il solo warning noto. |
| 2026-09-30 | `MediatorOptions.ServiceLifetime` — vincolo di conoscenza | Il codice del generatore non è emesso su disco e la documentazione XML del pacchetto è vuota, quindi il nome del membro non è stato verificabile offline: la forma adottata è quella nota di Mediator 2.x. Se fosse sbagliata, la build lo direbbe con `CS1061` su una sola riga. Da annotare come limite dichiarato, non come verifica. |
| 2026-09-30 | `dotnet test .\Qwerty.Codi.sln --configuration Release --no-build` dopo le due correzioni (eseguita dall'utente) | **163 test: 161 riusciti, 2 falliti**. La correzione della durata è **verificata**: gli errori di validazione delle durate sono spariti, quindi `MediatorOptions.ServiceLifetime` esiste e ha l'effetto atteso. Restano due difetti miei, entrambi di prova e non di codice: (1) `Assert.Empty` sulle richieste anonime mancanti era falso per costruzione — `JoinSession` manca davvero e arriva con T3 — quindi l'asserzione ora fissa quello stato atteso e fallirà quando verrà implementata; (2) il caso del token valido non poteva riuscire perché `GetAndStoreTokens` legge i cookie della richiesta e ne **fissa la lettura**: scrivere il cookie sullo stesso contesto dopo la generazione non ha effetto, quindi il token si genera su un contesto e si verifica su un altro, come fa il browser. Lezione per i test futuri: su `DefaultHttpContext` l'ordine fra scrittura delle intestazioni e prima lettura dei cookie conta. |
| 2026-09-30 | Build Release e avvisi dell'analizzatore xUnit `xUnit1030` | Cinque avvisi in due file di test: `ConfigureAwait(false)` dentro metodi di test, che l'analizzatore scoraggia perché può scavalcare i limiti di parallelizzazione. Rimosso dai cinque punti; resta solo nella funzione di appoggio `PrepareDatabaseAsync`, che non è un metodo di test e quindi non è interessata. T0 si era chiuso con build Release **senza warning**: la regola vale anche per i test, e un avviso nuovo è una regressione della stessa natura di un errore. |
| 2026-09-30 | Primo giro del gate di copertura (eseguito dall'utente) | Due cose misurate: **`coverlet.collector` 10.1.0 funziona** con la piattaforma di test — i report esistono — e il filtro `Include` di `coverlet.runsettings` **corrisponde** ai quattro assembly dell'applicazione, che compaiono nei report con il loro nome. Un difetto mio però: l'aggregazione prendeva il **minimo** fra i report, e ogni progetto di test produce un report in cui gli assembly che non esercita valgono zero; la baseline risultante era zero per tutti, cioè un guardrail incapace di fallire. Corretto in `assert-dotnet-coverage.ps1`: le righe si uniscono fra i report, e una riga è coperta se almeno una prova l'ha eseguita. La baseline riempita con quegli zeri è stata svuotata: si rigenera dalla prossima misura. Su questa macchina `pwsh` non esiste, c'è Windows PowerShell 5.1, e README, matrice e stato ora usano il percorso relativo. |
| 2026-09-30 | `npm view` sui pacchetti di lint (eseguita dall'utente) | Versioni reali: `angular-eslint` 22.5.0 (peer `eslint ^9||^10`, `typescript-eslint ^8`), `typescript-eslint` 8.71.0 (peer `typescript >=4.8.4 <6.1.0`), `eslint` 10.11.0, `@eslint/js` 10.0.1, `eslint-config-prettier` 10.1.8. `package.json` allineato: la coppia `eslint`/`@eslint/js` resta alla stessa major, perché miscelare 9 e 10 è la combinazione che non regge, e `typescript-eslint` resta alla 8, che è quella richiesta da `angular-eslint`. TypeScript 6.0.2 è **dentro** la finestra dichiarata, che si ferma a 6.1.0: alzare TypeScript a 6.1 romperebbe il lint, quindi la versione non si aggiorna senza ricontrollare quella finestra. La configurazione ESLint è stata convertita in forma **ESM** (`eslint.config.mjs`): importare funziona sia per i pacchetti ESM sia per quelli CommonJS, richiedere no. Resta da **rimuovere** `eslint.config.js`, la versione CommonJS: con due file di configurazione ESLint si ferma invece di sceglierne uno. |
| 2026-09-30 | Gate del frontend: `npm install`, `lint`, `format:check`, `test`, `build`, `audit:runtime` (eseguiti dall'utente) | Installazione riuscita, **0 vulnerabilità** di runtime e configurazione ESM accettata: la conversione a `eslint.config.mjs` era giusta. `npm run test`: **12 test su 12** in 1,36 s. `npm run build`: initial 233,38 kB (65,23 kB di trasferimento) e Blockly nel chunk lazy a 754,38 kB (158,69 kB), budget rispettati. `npm run lint`: 2 errori di stile in `workspace-serialization.ts`, dove `WorkspaceState` era un tipo letterale con firma di indice; la definizione è diventata `Record<string, unknown>`, che soddisfa entrambe le regole senza cambiare il tipo effettivo — nessun comportamento cambia, quindi nessun test nuovo. `npm run format:check`: **27 file non formattati**, compresi i file dello scaffold Angular che non sono mai passati da Prettier; è una normalizzazione una tantum da eseguire con `npm run format`, e finché non viene eseguita questo gate non è verde. Lint e formattazione sono i due soli gate del frontend ancora aperti; test, build e audit sono validati. |
| 2026-09-30 | Secondo giro dello script di copertura (eseguito dall'utente) | Lo script ha segnalato un calo su `Api`, `Domain` e `Infrastructure` che **non è una regressione**: in `TestResults` c'erano **12 cartelle di report, cioè quattro esecuzioni mescolate**, e l'unione delle righe è coerente solo dentro la stessa esecuzione — un report vecchio porta righe di codice che oggi non esiste più e sposta il rapporto in una direzione arbitraria. Quindi anche i numeri registrati poco prima (Api 0,7327, Application 0,9855, Domain 0,9484, Infrastructure 0,9903) **non sono una misura attendibile** e sono stati rimossi. Corretto lo script: considera solo i report dell'esecuzione più recente, dichiara quanti ne trova e quanti ne ignora. README e matrice ora prescrivono di cancellare `TestResults` prima della raccolta, così l'input è di una sola esecuzione. Il calo segnalato dallo script è un esempio di guardrail che funziona: ha fermato un numero, non un difetto. |
| 2026-09-30 | Terzo giro dello script di copertura, con i report di una sola esecuzione (eseguito dall'utente) | `Report trovati: 3, considerati: 3` e i quattro assembly **con valori identici alla baseline** — eppure lo script dichiarava un calo su tre. **La mia diagnosi precedente era sbagliata**: mescolare i report non aveva alterato i numeri, che sono gli stessi di una misura pulita. La causa vera è un difetto mio nello script: la baseline la scrivo arrotondata a quattro decimali e la misura la confrontavo intera, quindi `0,732681 < 0,7327` e ogni assembly arrotondato per eccesso risultava in calo a ogni esecuzione — un guardrail che si accende da solo, l'errore opposto ma della stessa natura della baseline a zero. Corretto allineando la precisione del confronto a quella della baseline; la tabella ora mostra quattro decimali, perché con due un calo reale al quarto apparirebbe come due numeri identici. La restrizione all'esecuzione più recente resta, ma come precauzione contro un'unione incoerente, **non** come causa del calo osservato. |
| 2026-09-30 | Gate di copertura verde (eseguito dall'utente) | `Report trovati: 3, considerati: 3` e i quattro assembly con `ok` alla precisione della baseline: Api 73,27%, Application 98,55%, Domain 94,84%, Infrastructure 99,03%. **Resta da provare la rotta negativa**: alzare a mano un valore della baseline e verificare che lo script fallisca. Un guardrail di cui si è vista solo la rotta verde non è verificato, ed è lo stesso errore che ha prodotto prima una baseline a zero e poi un calo inesistente. Nota di codifica: i messaggi dello script sono comparsi in Windows PowerShell 5.1 con i caratteri accentati errati, perché il file è UTF-8 **senza BOM** e 5.1 interpreta come ANSI i file privi di BOM; va risalvato con il BOM, e il requisito è documentato nella sezione NOTES dello script. |
| 2026-09-30 | `.gitignore` della soluzione e verifica di quello del frontend | Creato il `.gitignore` di radice: esclusi gli artefatti di compilazione (`bin/`, `obj/`), la cartella dei pacchetti ripristinati (`packages/`, ricostruibile dai lockfile), risultati e copertura (`TestResults/`), i **dati operativi** (database SQLite con i file `-wal` e `-shm`, backup) e segreti e certificati. Restano versionati `packages.lock.json`, `global.json`, `Directory.*.props`, `NuGet.config`, `coverlet.runsettings` e `scripts/coverage-baseline.json`: sono contratti, non cache. Un solo file alla radice basta perché le regole valgono anche nelle sottocartelle; il frontend conserva il proprio, già coerente. **Nella cartella non risulta ancora un repository Git**, quindi i file sono preparatori e la loro efficacia va verificata quando il repository esisterà: un `.gitignore` non provato è un'ipotesi, non una verifica. || 2026-09-30 | Gate del frontend tutti verdi (eseguiti dall'utente) | `npm run lint` pulito, `npm run format:check` → «All matched files use Prettier code style!», `npm run test -- --watch=false` → **12 test su 12** in 1,26 s, `npm run build` → initial 233,38 kB (65,23 kB di trasferimento) con Blockly nel chunk lazy a 754,38 kB (158,69 kB), `npm run audit:runtime` → **0 vulnerabilità**. Osservazione da non perdere: `npm run format` ha riportato «unchanged» su **tutti** i file, mentre il primo `format:check` ne segnalava 27 — fra i due comandi qualcosa li ha formattati (una prima esecuzione di `format` non incollata, oppure l'editor). Non cambia l'esito, ma una riscrittura silenziosa a metà strada è il tipo di cosa che dopo non si spiega: se ricapita, vale la pena capire chi l'ha fatta. |
| 2026-09-30 | Cambio di politica: gli agenti eseguono i controlli | Su richiesta esplicita dell'utente cambia la convenzione operativa: build, restore, test, lint, formattazione, bundle e raccolte di copertura li esegue l'agente, leggendone l'output dal terminale e riportando l'esito reale. Riscritti `copilot-instructions.md` (sezione «Verifica e consegna»), `AGENTS.md` e la specifica tecnica §20.1, insieme ai nove prompt, alle istruzioni d'area, alle skill e alle guide; in §20.5 è caduta l'affermazione che i gate fossero **DA CREARE**, perché esistono e sono validati. Restano vietati installazioni di software di sistema, scrittura su dati operativi, pubblicazione e distribuzione. `analista-specifica` e `revisore-cqrs` restano in sola lettura e i due prompt che li invocano sono stati riallineati. Rimosso `tools:` da otto profili su dieci: un elenco esplicito con identificativi non verificati avrebbe potuto togliere **in silenzio** proprio la capacità di eseguire. **Da validare**: che i profili senza `tools:` abbiano davvero accesso al terminale, e che `revisore-cqrs` resti bloccato in sola lettura. |
| 2026-09-30 | `dotnet test .\Qwerty.Codi.sln --configuration Release --no-build` (eseguita dall'utente) | **163 test su 163, nessuno fallito, nessuno ignorato** in 3,1 s, con il solo avviso noto `No test is available` di `Qwerty.Codi.ProtocolTests`. **Valida la quinta porzione di T1** — trasporto dell'accesso, antiforgery, contesto di richiesta reale, composizione con validazione delle durate — insieme ai 149 test già validati prima. Il percorso verificato a runtime è: cookie con durata assoluta e marca di sicurezza, revoca che toglie il permesso a un ticket ancora valido, antiforgery sulle mutazioni autenticate, catalogo delle richieste coerente con l'elenco anonimo di §10.4 e durate delle dipendenze compatibili. Il build che ha preceduto l'esecuzione non è stato incollato: **l'assenza di warning è DA VALIDARE**, non dedurla da questa riga. |
| 2026-09-30 | Revisione e correzione dei sei difetti (agente, su richiesta dell'utente) | Antiforgery: GET /public/bootstrap, cookie interno HttpOnly e request token distinto, rinnovato dopo login. Logout: corpo vuoto e contesto verificato associato dall'esecutore, nessun ID autorevole dal JSON. Login: rilettura e rivalutazione su conflitto, limite dei retry, nessuna credenziale dopo blocco concorrente. Username: limite anche sul nome digitato. Blockly: shadow di riserva validati ma contati solo se attivi. Copertura: chiave file+riga, assembly mancanti rifiutati, cartella per singola raccolta e quattro test del gate aggiunti alla CI. Le vecchie percentuali di copertura del registro erano alterate dal calcolo e sono superate dalla misura seguente. |
| 2026-09-30 | Gate finali eseguiti dall'agente | dotnet restore della soluzione con --locked-mode: exit 0; build Release con --no-restore: exit 0, 0 errori, 0 warning. Test Release con --no-build e collector XPlat: **49 dominio + 124 integrazione = 173/173**, nessun fallimento/skip; ProtocolTests resta senza casi. Risultati dedicati in TestResults/fixes-20260930-final. Frontend con CLI locali delle dipendenze già installate: **15/15 test**, lint e Prettier conformi, build di produzione senza warning, bundle iniziale 233,38 kB. Nessuna nuova dipendenza. |
| 2026-09-30 | Baseline corretta e gate negativo | Rigenerata scripts/coverage-baseline.json dai tre report della sola raccolta finale, motivazione in docs/TEST-COVERAGE-MATRIX.md. Gate positivo: exit 0, Api 83,56%, Application 97,59%, Domain 94,24%, Infrastructure 94,89%. test-coverage-gate.ps1: quattro casi, incluso calo e assembly mancante, **4/4**. I test antiforgery usano chiavi effimere e non accedono più alle chiavi del profilo utente. Restore ha richiesto lettura della configurazione NuGet fuori sandbox; i test finali sono riusciti dentro la sandbox. |
| 2026-09-30 | Refactoring della persistenza richiesto dall'utente sul modello ProjectAnt | Analizzati BaseRepository, IRepository, DbContext, UnitOfWork e registrazione dell'esempio. Codì adotta IRepository con CRUD/Query/Exists, entità POCO separate dal dominio, FluentMapper in Infrastructure e repository interni derivati dal base. IUnitOfWorkContext crea scope e connessione dedicati; Repo risolve istanze condivise e il chiamante decide commit/rollback. Login, bootstrap, logout e verifica del cookie usano il percorso nuovo. Eliminati SQL CRUD e aperture per metodo dai repository. Migrazione 0001, schema, pacchetti e lockfile invariati; nessun dato operativo toccato. Guida docs/PERSISTENZA.md e specifica tecnica 0.8. |
| 2026-09-30 | Verifica finale del refactoring eseguita dall'agente | dotnet format whitespace sui file C# indicati: exit 0. Build soluzione Release --no-restore: exit 0, **0 warning / 0 errori**. Test con collector XPlat in TestResults/uow-refactor-20260930-final: **190/190** (49 Domain.Tests + 141 IntegrationTests), nessun fallimento/skip; ProtocolTests senza casi. **17 nuovi casi** su CRUD, mapping, filtri SQL parametrizzati, isolamento, commit/rollback, cancellazione, FK, guasto di BEGIN e login atomico. Gate di copertura: exit 0 su tutte le soglie originali, nessun aggiornamento baseline. Misure: Api 83,65%, Application 97,72%, Domain 94,24%, Infrastructure 95,38%. Non rieseguiti frontend, restore o audit in questa porzione: nessun cambiamento a frontend o dipendenze. |

| 2026-09-30 | T2, prima porzione: schema e dominio dell'organizzazione | Migrazione `0002_organization.sql` con `school`, indice unico su `normalized_name` (decisione A-01: nome unico anche fra le archiviate), CHECK su nome e flag. Dominio: `Domain/Common/NameNormalization` (regole condivise, `AdminUsername` ora vi delega senza cambiarne il comportamento) e `Domain/Organization/SchoolName` e `School` con creazione, rinomina che distingue la modifica dal nome equivalente, archiviazione registrabile una volta e ricostruzione validate. Controllo del language server: nessun errore; build eseguita subito dopo. |
| 2026-09-30 | Persistenza dell'organizzazione | `SchoolEntity` con UUID canonico, flag intero e date epoch millisecondi; `ISchoolRepository` con `TryInsertAsync` (conflitto di unicità distinto dagli altri vincoli) e `TryUpdateAsync` (campi di nome e archiviazione con controllo di versione); mapping FluentMapper e registrazione scoped. Nessun `Guid` nei DTO di persistenza, come richiede A-06. |
| 2026-09-30 | Casi d'uso dell'organizzazione | `CreateSchool`, `UpdateSchool`, `ArchiveSchool` (comandi, policy `Admin`) e `ListSchools`, `GetSchool` (query, policy `Admin`). Codici nuovi: `SchoolAlreadyExists` 409, `ResourceNotFound` 404, `EntityVersionRequired` 428, con titoli e stati in `CodiErrorStatus`; i contatori a 64 bit diventano stringhe decimali nel payload (`LongAsStringJsonConverter` in `CodiJson`, opzione che il commento del file prevedeva di attivare con i primi DTO che li usano). Verifica del nome già in uso dentro la stessa transazione della scrittura. |
| 2026-09-30 | `GET /auth/me` | Profilo minimo del contesto (§10.3): non legge i claim ma rilegge la credenziale come il dispatcher, quindi una sessione revocata risponde 401 anche con il cookie ancora nel browser. Usato dalle pagine amministrative per distinguere «sessione assente» da «errore». |
| 2026-09-30 | Frontend dell'area amministrativa | Nuovi `core/api`: `ApiService` (i due metodi del dispatcher), `AdminSessionService` (bootstrap, accesso, uscita, contesto), `ApiError` (Problem Details → codice, stato, `traceId`, `requiresSignIn`), `antiforgeryInterceptor` con lettura del request token, `models.ts` (scuola con `rowVersion` come stringa). Pagine lazy `/admin/login` e `/admin/schools` con Signals e `OnPush`; `proxy.conf.json` per lo sviluppo; route e `provideHttpClient` aggiornati. Controllo del language server: nessun errore; i gate npm sono stati eseguiti subito dopo. |
| 2026-09-30 | `dotnet build .\Qwerty.Codi.sln --configuration Release` sulla prima porzione di T2 (eseguita dall'agente) | Fallita due volte su `NameNormalization`: `CS9135` (un pattern relazionale pretende una costante, quindi `maxLength` come parametro non è utilizzabile in `is > 0 and <= maxLength`) e `CS8604` (possibile null passato a `Normalize`). Corrette entrambe ristrutturando il metodo con uscite anticipate: nessuna delle due era stata segnalata prima della build. |
| 2026-09-30 | Primo giro dei test sulla prima porzione di T2 (eseguito dall'agente) | **3 test di migrazione rossi, non un difetto del codice**: `MigrationRunnerTests` fissava il numero di migrazioni a 1 in tre asserzioni. Adeguati a `catalog.LatestVersion` e `catalog.Scripts.Count`, mantenendo le asserzioni sul contenuto (nome, impronta, tabella `school`). Stessa cosa per `DatabaseBootstrapTests`, che ora confronta la versione di schema con quella del catalogo distribuito. |
| 2026-09-30 | Primo gate di copertura sulla prima porzione di T2 (eseguito dall'agente) | **Calo reale**: Api 80,05%, Application 97,43%, Domain 93,89%. Causa individuata riga per riga nei report: i test in-process coprono i casi d'uso ma non il trasporto, quindi restavano scoperti gli stati nuovi di `CodiErrorStatus` e le guardie di errore dei comandi. Aggiunti i test HTTP dell'organizzazione e dell'accesso (`SchoolHttpTests`, `CodiHttpServer` riutilizzabile) e i casi mancanti sui percorsi di errore; nessuna soglia è stata abbassata. |
| 2026-09-30 | Gate finali della prima porzione di T2 (eseguiti dall'agente) | `dotnet restore .\Qwerty.Codi.sln --locked-mode`: uscita 0. Build Release `--no-restore`: riuscita, **0 errori, 0 warning**. Test Release `--no-build`: **266/266** (81 Domain.Tests + 185 IntegrationTests), nessun fallimento o salto; `ProtocolTests` resta senza casi. Copertura in `TestResults/68f501ff1ce44c91acb2b057240ff276`: gate verde **contro la baseline invariata** (Api 84,40%, Application 98,42%, Domain 95,42%, Infrastructure 95,66%). Verificato anche `scripts/test-coverage-gate.ps1`: **4/4**. Frontend: `npm run test` **39/39**, lint pulito, `format:check` conforme, build di produzione senza warning (iniziale 285,61 kB, pagine amministrative in chunk lazy), `audit:runtime` 0 vulnerabilità. |
| 2026-09-30 | Errore di procedura durante l'analisi, corretto | Il progetto di sonda creato in `%TEMP%` per leggere l'API di Mediator è stato **aggiunto alla soluzione** da qualcosa nell'ambiente: `dotnet build` lo compilava come ottavo progetto. Rimosso con `dotnet sln remove` e soluzione verificata di nuovo a sette progetti. Le sonde future vanno fatte fuori dal repository e la soluzione va ricontrollata dopo. |

| 2026-09-30 | T2, seconda porzione: schema, dominio e persistenza di classi e corsi | Migrazione `0003_organization_classrooms_courses.sql` con `classroom` (chiave esterna su `school`, unico nome+anno nella scuola, anno in forma canonica imposta da `GLOB`), `course` (titolo 1–160, descrizione fino a 2000 caratteri non annullabile) e `course_classroom` (unico corso+classe, flag di attività). Dominio `Organization`: `SchoolYearNormalization` (forma canonica `AAAA/AAAA`, barra e trattino equivalenti, anni consecutivi), `Classroom` (scuola immutabile, modifica di nome e anno, archiviazione una volta sola), `Course` e `CourseClassroom` (associazione che si disattiva e si riattiva sulla stessa riga). Entità POCO, interfacce e repository su `BaseRepository`: la classe e l'associazione hanno l'esito di conflitto previsto, il corso usa il solo CRUD comune. Mapping FluentMapper e registrazioni scoped. **Nota di denominazione**: il validatore dell'anno si chiama `SchoolYearNormalization` e non `SchoolYear`, perché dentro `Classroom` la proprietà `SchoolYear` renderebbe ambigua la chiamata statica; è stato il compilatore a segnalarlo con `CS0120`. |
| 2026-09-30 | Casi d'uso di classi, corsi e associazioni | Comandi `CreateClassroom`, `UpdateClassroom`, `ArchiveClassroom`, `CreateCourse`, `UpdateCourse`, `ArchiveCourse`, `AttachCourseToClassroom`, `DetachCourseFromClassroom` e query `ListClassrooms`, `GetClassroom`, `ListCourses`, `GetCourse`, `ListCourseClassrooms`, tutte con policy `Admin`. Codice nuovo `ClassroomAlreadyExists` (409) con il suo stato e il suo titolo. Lettura, verifica della coppia già usata e scrittura condizionata stanno nella stessa transazione non differita; l'esistenza della scuola e delle due entità da associare si verifica nella stessa unità di lavoro, così una chiave esterna violata non diventa un errore interno. L'associazione è idempotente: ripeterla riattiva la riga esistente e, se l'inserimento trovasse la coppia già presente, la richiesta rilegge il valore di ritorno invece di trattarlo come eccezione. |
| 2026-09-30 | Frontend di classi e corsi | `models.ts` esteso con `Classroom`, `Course` e `CourseClassroom`; la pagina `/admin/schools` gestisce le classi della scuola selezionata (creazione con nome e anno proposto dal calendario, rinomina con la versione letta, archiviazione confermata) e la nuova pagina `/admin/courses` gestisce corsi e associazioni, scegliendo una scuola e poi una sua classe. Il pulsante di associazione mostra «Associa» o «Distacca» secondo lo stato corrente. Route e titolo aggiornati, README del frontend con le aree implementate. |
| 2026-09-30 | Prima esecuzione dei test sulla seconda porzione (eseguita dall'agente) | Un fallimento reale di dominio: `SchoolYearNormalization` rifiutava `9998/9999` perché il limite superiore dell'anno era applicato anche al secondo. Corretto il limite (9999) lasciando che sia la consecutività a escludere `9999/10000`. Il caso era già scritto nel test, quindi il difetto è emerso dalla prima esecuzione senza aggiungere test. |
| 2026-09-30 | Prima compilazione dei test HTTP (eseguita dall'agente) | `CS1061` su `school.Id`: l'esito JSON di un comando è un `JsonElement`, non un DTO. Corretto leggendo `GetProperty("id")`. |
| 2026-09-30 | Primo gate di copertura sulla seconda porzione (eseguito dall'agente) | **Calo su `Application`: 93,47%** contro 97,59%. Le righe scoperte erano i percorsi di errore dei nuovi comandi (versione mancante o obsoleta, validazione di titolo e descrizione, modifica senza cambiamenti, archiviazione ripetuta) e due guardie di formato delle entità. Aggiunti nove test di caso d'uso e uno di entità; nessuna soglia abbassata. Restano scoperte solo le righe difensive non raggiungibili senza concorrenza reale. |
| 2026-09-30 | Gate finali della seconda porzione di T2 (eseguiti dall'agente) | Build Release: **0 errori, 0 warning**. Test Release: **373/373** (134 Domain.Tests + 239 IntegrationTests), nessun fallimento o salto. Copertura in `TestResults/1edabefb4e344ddd911711bf3da955a8`: gate verde **contro la baseline invariata** (Api 84,99%, Application 98,21%, Domain 95,41%, Infrastructure 96,05%). Verificato anche `scripts/test-coverage-gate.ps1`: **4/4**; restore bloccato: uscita 0. Frontend: **49/49** test, lint e formattazione conformi, build di produzione senza warning (iniziale 286,04 kB; `school-list` e `course-list` in chunk lazy). |
| 2026-09-30 | T2, terza porzione: schema, dominio e persistenza di lezioni, contenuti e asset | Nuova migrazione `0004_lessons_contents_media.sql`: `lesson` (titolo 1–160, descrizione fino a 2000 caratteri, indice `course_id` secondo il dizionario), `content_item` (tipi `Text` e `Image`, `body_json` con `json_valid` e limite di lunghezza), `media_asset` (chiave di memorizzazione unica, stato `Ready`/`Deleted`, impronta SHA-256 di 64 caratteri), `lesson_activity` (posizione unica per lezione, temporanee negative ammesse e zero escluso, coerenza fra `kind` e riferimento). La chiave esterna di `exercise_version_id` resta rimandata a T6, come `participant_id` lo era a T3 nella `0001`. Dominio in `Domain/Lessons` (`Lesson`, `LessonActivity`, `LessonActivityKind`) e `Domain/Contents` (`ContentItem`, `ContentBody`, `ContentKind`); entità POCO, `ContentBodyJson` come forma di `body_json`, quattro interfacce, quattro repository su `BaseRepository`, mapping FluentMapper e registrazioni scoped. Controllo del language server: nessun errore; gate eseguiti subito dopo. |
| 2026-09-30 | Prima esecuzione dei test sulla terza porzione di T2 (eseguita dall'agente) | **450 test: 2 fallimenti**, entrambi difetti dei test e non del codice. (1) Il riordino riusava la versione di riga già consumata dalla prima fase: `UpdateAsync` alza la versione sull'entità passata, quindi la seconda fase deve riusare l'entità aggiornata invece di ricostruirla dal dominio. (2) Un caso tentava di inserire dal database un contenuto di tipo `Demo`, che il vincolo della tabella rifiuta correttamente: il rifiuto di un tipo non riconosciuto si prova al livello del mapping, dove la riga si costruisce senza database. Corretti entrambi, con un nuovo file di test di mapping, e rieseguiti. |
| 2026-09-30 | Gate finali della terza porzione di T2 (eseguiti dall'agente) | `dotnet restore .\Qwerty.Codi.sln --locked-mode`: uscita 0. Build Release `--no-restore`: **0 errori, 0 warning**. Test Release `--no-build` con collector XPlat: **457/457** (186 Domain.Tests + 271 IntegrationTests), nessun fallimento o salto; `ProtocolTests` resta senza casi. Copertura in `TestResults/763bc65e31b44f279db7873c1d2b8411`: gate verde **contro la baseline invariata** (Api 84,99%, Application 98,28%, Domain 95,60%, Infrastructure 96,39%). `scripts/test-coverage-gate.ps1`: 4/4. Build e test sono stati rieseguiti dopo l'aggiunta delle etichette di categoria ai nuovi test di dominio, con lo stesso esito e gli stessi numeri. Frontend non eseguito: nessuna modifica al frontend in questa porzione, le sue evidenze restano quelle della verifica precedente. |
| 2026-09-30 | Fine riga del codice e `.editorconfig` | `dotnet format whitespace .\Qwerty.Codi.sln --verify-no-changes` segnala `ENDOFLINE` su molti file, compresi file **non toccati** da questa porzione (per esempio `Domain/Admin/AdminUser.cs`): `.editorconfig` prescrive `end_of_line = lf` mentre l'albero di lavoro è in CRLF, migrazione `0003` inclusa. Non è un gate di §20.5 e non è stato corretto: convertire solo i file nuovi lascerebbe il repository misto e la conversione in blocco tocca file estranei alla porzione. Va deciso in blocco, eventualmente con `.gitattributes` quando il repository Git esisterà. |
| 2026-09-30 | Lettura dell'esito di `scripts/test-coverage-gate.ps1` | Lo script non chiama `exit`: dopo l'esecuzione `$LASTEXITCODE` conserva il codice del caso negativo interno (1) anche quando i quattro casi passano, come confermato in questa sessione. L'esito va letto da `$?` o dall'output, non da `$LASTEXITCODE`: non è un fallimento del runner. |
| 2026-09-30 | Decisione dell'utente sugli asset e su `Demo` | Su richiesta dell'utente del 30 settembre 2026 gli asset multimediali appartengono a T2 (schema in `0004`, caricamento e download in questa porzione) e il contenuto di tipo `Demo` **non si implementa** finché non ha un contratto: il dizionario §7.3 lo elencava fra i tipi iniziali senza definirlo. Registrato in §7.3 della specifica tecnica e fra i buchi noti della matrice di copertura. |
| 2026-09-30 | T2, quarta porzione: casi d'uso di lezioni, contenuti, asset e iscrizioni | Nuovi in `Application/Lessons` (7 richieste: `CreateLesson`, `UpdateLesson`, `ArchiveLesson`, `ReplaceLessonActivities`, `ListLessons`, `GetLesson`, `ListLessonActivities`), `Application/Contents` (5 richieste più `ContentBodyFactory`), `Application/Enrollments` (4 richieste), `Application/Media` (3 richieste più `MediaAssetContentService` per i byte) e `Domain/Media` (`MediaAsset`, `MediaAssetStatus`, `ImageSignature`) e `Domain/Enrollments` (`Enrollment`, `EnrollmentDisplayName` con conteggio degli elementi di testo). Persistenza: migrazione `0005_enrollment.sql`, entità e repository di iscrizioni, `TryInsertAsync` sugli asset. Trasporto: i due endpoint di §10.3 (`POST /admin/media-assets` multipart, `GET /media-assets/{id}` con `Range`), lo storage su disco (`FileSystemMediaStorage`, `MediaStorageDirectory`, `MediaStorageBootstrap`), la voce `Codi:MediaDirectory` di §18.2 con validazione all'avvio e controllo di prontezza, e i codici `PayloadTooLarge` (413) e `StorageUnavailable` (503) di §10.7. Controllo del language server: nessun errore; gate eseguiti subito dopo. |
| 2026-09-30 | Primo avvio reale degli endpoint nuovi (eseguito dall'agente) | L'API non rispondeva: **tutte** le richieste tornavano `500`. La console ha mostrato la causa vera — `Body was inferred but the method does not allow inferred body parameters` su `MediaAssetContentService` — perché i parametri dei delegati di ASP.NET Core si risolvono dal contenitore e un tipo non registrato viene interpretato come corpo della richiesta. Registrato il servizio scoped; la diagnosi non nominava la registrazione mancante, quindi è annotata come trappola ricorrente. **Effetto collaterale da dichiarare**: l'avvio è stato fatto con `dotnet run` sull'applicazione reale, che ha **applicato le migrazioni `0002`–`0005` al database di sviluppo** `src/Qwerty.Codi.Api/data/codi.db` creato dall'utente nel pomeriggio e vi ha aggiunto la directory `data/media`. Nessun dato è stato cancellato e il file è escluso dal versionamento, ma è una scrittura su dati operativi locali che sarebbe dovuta restare fuori: la prossima volta la diagnosi va fatta su un database temporaneo, per esempio avviando l'API con `Codi:DataDirectory` e `Codi:MediaDirectory` puntati a una cartella temporanea. |
| 2026-09-30 | Due difetti di produzione trovati dai nuovi test (corretti) | (1) `RegisterMediaAsset` lasciava uscire un `ArgumentException` del dominio come `500`: il filtro del `catch` confrontava i nomi delle proprietà del record invece dei nomi dei parametri del dominio. Ora ogni metadato inutilizzabile diventa un errore di validazione. (2) `ReplaceLessonActivities` riscriveva il contenuto sulla riga della posizione: la sequenza risultava giusta ma l'attività cambiava contenuto, e un'assegnazione di T4 avrebbe mostrato altro materiale. Ora ogni casella cerca l'attività che mostra quel contenuto e la sposta, conservandone l'identificativo; la prova è il test sull'inversione d'ordine. |
| 2026-09-30 | Prima misura della copertura della quarta porzione (eseguita dall'agente) | **Calo su due assembly**: Api 80,85% (baseline 83,56%) e Application 96,41% (97,59%). Le righe scoperte erano i percorsi di errore del codice nuovo: modulo senza file, corpo non multipart, file oltre il limite, byte mancanti, metadati non validi, versione obsoleta, prontezza dello storage. Aggiunti test mirati (7 di caso d'uso sui percorsi di errore e 4 HTTP) senza abbassare la baseline. |
| 2026-09-30 | Deleghe agli specialisti (esito misto) | La delega a `esperto-test` non ha prodotto report: ha lasciato sette file di test corretti (dominio di asset e iscrizioni, storage su file) e non ha completato il resto, che è stato scritto dall'agente. La delega a `esperto-frontend-angular` ha consegnato le pagine amministrative complete con i suoi gate. Lezione: una delega non è conclusa finché non c'è il report, e il lavoro parziale va verificato file per file prima di accettarlo. |
| 2026-09-30 | Gate di chiusura di T2 (eseguiti dall'agente) | `dotnet restore --locked-mode`: uscita 0. Build Release `--no-restore`: **0 errori, 0 warning**. Test Release `--no-build` con collector XPlat: **602/602** (252 Domain.Tests + 350 IntegrationTests), nessun fallimento o salto; `ProtocolTests` resta senza casi. Copertura in `TestResults/8b927fdf6ea14a5aad5ebcddf1ffe237`: gate verde **contro la baseline invariata** (Api 85,08%, Application 97,96%, Domain 97,24%, Infrastructure 96,19%), `scripts/test-coverage-gate.ps1` 4/4. Frontend: `npm run lint` pulito, `format:check` conforme, **57/57 test**, build di produzione senza warning (iniziale 286,60 kB), **0 vulnerabilità** di runtime. |
| 2026-09-30 | Unificazione dei test di mapping (agente) | La delega a `esperto-test` e il lavoro dell'agente avevano prodotto **due file** per lo stesso mapping di asset e iscrizioni. I casi utili sono stati uniti in `EnrollmentMediaEntityTests.cs` (round trip di asset pronto e ritirato, iscrizione attiva e archiviata, rifiuto di stato non riconosciuto, dimensione impossibile, impronta troncata, tipo non ammesso, nome non utilizzabile, flag non booleano) e il file duplicato è stato rimosso. Il conteggio dei test passa da 605 a 602: i casi rimossi erano duplicati, la copertura non cambia (Api 85,08%, Application 97,96%, Domain 97,24%, Infrastructure 96,19%). |

Il runner delle quattro regressioni del gate è verificato anche su PowerShell 7 e Windows PowerShell 5.1, entrambi con esito **4/4**.

| 2026-09-30 | Account amministrativo iniziale inserito dalla configurazione (richiesta dell'utente) | Nuova sezione `Codi:SeedAdmin` (`Username`, `Password`) di §18.2, con `AdminAccountSeed` (`Api/Authentication`) chiamato da `Program` dopo la preparazione del database. Passa dalla richiesta `BootstrapAdminAccount`, quindi un amministratore già attivo rende l'inserimento inerte e non si duplicano le regole. In `appsettings.Development.json` la coppia è `Admin` / `Ottimixmo@20`. **Prova reale su database nuovo e cartella temporanea**: il log recita «Account amministrativo iniziale inserito dalla configurazione: Admin», l'API ascolta su `https://localhost:5200`, il login `POST /auth/admin/login` risponde **200** e `GET /auth/me` restituisce `{"username":"Admin","policies":["Admin"]}`. **Deroga dichiarata** alla regola di §9.1 e alla nota di `.gitignore`: la credenziale sta in un file versionato, quindi va rimossa e cambiata prima della consegna in laboratorio (punto aperto A-09). Test nuovi: `AdminAccountSeedTests` (inserimento con le credenziali che poi accedono davvero; nessun inserimento senza configurazione), 604 test su 604. |

| 2026-09-30 | Il guscio del frontend e l'indirizzo delle sezioni (segnalazione dell'utente) | **Difetto trovato dall'utente**: aprendo `https://localhost:4200/admin/login` si vedeva prima la pagina di benvenuto del modello di Angular, con «Hello, codi-web», e il modulo di accesso sotto di essa, perché `app.html` conteneva ancora il segnaposto del modello con `<router-outlet />` in fondo. Il guscio ora contiene solo l'outlet. Aggiunto il reindirizzamento della radice a `/admin/login`, l'accesso già valido non rivede il modulo e le superfici di `/admin/courses` si scelgono con `?tab=`, con collegamenti diretti e senza voci nella cronologia. Verificato nel browser: la radice mostra solo il modulo di accesso. Controlli: lint 0, formattazione conforme, build senza errori, **59 test su 59** (due nuovi: guscio e reindirizzamento, sezione dall'indirizzo). Il banco di prova della pagina corsi è passato a `RouterTestingHarness`, perché ora la sezione passa dal router. |

| 2026-09-30 | Porta HTTPS fissa `5200` per l'avvio dell'API e proxy del frontend allineato | `Properties/launchSettings.json` del progetto `Api` usa `applicationUrl: https://localhost:5200` (prima una coppia casuale `51654`/`51655`) e apre il browser su `health/live`; `proxy.conf.json` del frontend inoltra `/api`, `/auth`, `/public` e `/health` a `https://localhost:5200` (prima `5001`), mentre l'applicazione Angular resta su `https://localhost:4200`. Prerequisiti verificati: `dotnet dev-certs https --check --trust` trova tre certificati `CN=localhost` attendibili e la porta `5200` è libera. Aggiornati `README.md`, la guida del frontend e §18.3 della specifica tecnica (0.14). |

| 2026-09-30 | Conversione della soluzione al formato `.slnx` | `dotnet sln .\Qwerty.Codi.sln migrate` ha generato `Qwerty.Codi.slnx` con i sette progetti nei due nodi `/src/` e `/tests/`; il file `.sln` è stato rimosso, così resta una sola soluzione. Con il nuovo file: restore `--locked-mode` uscita 0, build Release `--no-restore` **0 errori, 0 warning**, test Release `--no-build` **602/602** (252 Domain.Tests + 350 IntegrationTests). Aggiornati i comandi di §20.5, la CI, `README.md`, la guida di verifica e i riferimenti operativi; le righe di evidenza precedenti restano come sono state eseguite, con il nome del file allora in uso. Non sono state ripetute la misura di copertura né le prove del frontend, che il formato della soluzione non tocca. |
Verificati anche i link locali dei sei documenti aggiornati e la sintassi YAML della CI con il parser locale di Prettier; questo controllo non equivale a un'esecuzione GitHub Actions.

## Punti da chiarire quando toccano il lavoro

| ID locale | Riferimenti | Contrasto o verifica necessaria | Impatto |
|---|---|---|---|
| A-01 | §7.2 / §10.4 | **Risolto** il 2026-09-30 su decisione dell'utente: il nome della scuola è unico perché normalizzato, anche fra le scuole archiviate. Il dizionario §7.2 è stato allineato al catalogo §10.4 e l'indice unico dello schema corrente `0001_schema` realizza il vincolo. | Nessuna azione aperta. |
| A-02 | §10.3 / §10.4–§10.5 | **Risolto** il 2026-10-02 per la scoperta, il join e l'uscita: i tre `GET` pubblici invocano richieste Mediator **per tipo** e non entrano nel catalogo; `JoinSession`, `GetParticipantContext` e `LeaveSession` sono nomi del catalogo con policy proprie. `POST /api/v1/public/participant/leave` è implementata dall'endpoint dedicato che esegue `LeaveSession` (`AdmittedParticipant`). | Nessuna azione aperta. Il nome è in §10.4; non inventarne altri negli agenti. |
| A-03 | §7.4 / §21.1 | Chiuso in T4: ogni assegnazione fissa una versione pubblicata e crea tentativi indipendenti. La dimostrazione finita usa questi tentativi. | Salvataggi T5 e player Blockly T7 restano separati. |
| A-04 | §9.2–§9.3 / §10.4 / §12.7 | **CHIUSO — VALIDATO LOCALMENTE** il 2026-10-02: `AcknowledgeCommand` usa `ActiveParticipantConnection`, e il contesto la concede anche quando la lezione è **terminata** ma la connessione è ancora operativa. La conferma non riapre `AdmittedParticipant`, quindi non riabilita salvataggi né esecuzioni: l'alunno può dire che cosa ha applicato mentre la sessione si chiudeva, e il docente vede l'ultimo stato reale. | Quando i salvataggi arriveranno (T5), il percorso conclusivo resta il grant di §12.7 (`FinalizingParticipant`): l'eccezione vale per la sola conferma. |
| A-05 | §3.2 / csproj Infrastructure | **Risolto.** `RepoDB.SqLite 1.1.4` con `RepoDb 1.16.0` fallisce a runtime con `TypeLoadException` (`SqLiteDbHelper.GetScopeIdentity` non implementato): l'interfaccia `IDbHelper` del core è cambiata. Adottata la coppia allineata `RepoDb 1.14.0` + `RepoDb.Sqlite.Microsoft 1.14.0`. | Nessuna azione aperta. Ricontrollare solo aggiornando il core RepoDb: core e provider vanno tenuti alla stessa versione. |
| A-06 | §7.1 / §8.3 / D-01 | **Deciso.** RepoDb non converte da solo una colonna `TEXT` in `System.Guid`, e il prototipo lo ha dimostrato. Adottato il mapping esplicito: i DTO di persistenza portano l'identificativo come stringa canonica minuscola e la conversione a `Guid` avviene nel livello di mapping verso il dominio. L'alternativa `TypeMapper`/`PropertyHandler` resta possibile ma non necessaria: l'API non è documentata nel pacchetto e non si deduce dal nome. | Nessuna azione aperta. Da rispettare in ogni repository: nessun `Guid` nei DTO di persistenza. |
| A-07 | `.editorconfig` / albero di lavoro | **Risolto** il 2026-10-02. `.editorconfig` prescrive `end_of_line = lf` (con `[*.bat] = crlf`), ma `core.autocrlf` era `true` e mancava un `.gitattributes`: l'albero era misto (260 file in CRLF, 363 in LF) e `dotnet format whitespace --verify-no-changes` non era verde. Aggiunto `.gitattributes` (`* text=auto eol=lf`, `*.bat`/`*.cmd` in CRLF, binari esclusi) e riallineato l'albero di lavoro: ora **620 file in LF e 4 in CRLF** (i soli launcher `.bat`), `git status` pulito e `dotnet format whitespace --verify-no-changes` esce 0. Gli stessi file sono stati verificati con build e test (627 riusciti). | Nessuna azione aperta. La regola vale per ogni clone, non dipende più dalla configurazione della singola macchina. |
| A-08 | §9.3 / §10.3 | **Storico, endpoint rimosso**: il caricamento media e i suoi test non sono nel sorgente corrente. La vecchia lacuna 403 non descrive una superficie eseguibile oggi. | Se i media saranno reintrodotti, ripristinare controllo prima dei byte e prove di isolamento; non dichiarare copertura dai test cancellati. |
| A-09 | §9.1 / `appsettings.Development.json` | **Deroga aperta**, su richiesta dell'utente del 2026-09-30: `Codi:SeedAdmin` crea l'account `Admin` con password `Ottimixmo@20`, cioè una credenziale conosciuta in un file versionato, mentre §9.1 vuole il primo account solo da comando locale e la nota di `.gitignore` escludeva le password dai file versionati. | **Prima della consegna in laboratorio**: rimuovere la sezione `Codi:SeedAdmin` da `appsettings.Development.json`, cambiare la password dell'account e rigenerarlo con il comando locale. Finché resta, ogni copia del repository conosce la password dell'ambiente di sviluppo. |

| A-10 | Funzionale §1.1/§4/§13; tecnica §5/§7/§10 | **CHIUSO per decisione utente**: associazioni corso-classe, iscrizioni e contenuti/immagini eliminati dal prodotto. | Specifiche 0.4/0.24 allineate; join diretto alla sessione e LessonExercise. |
| A-11 | ReplaceLessonExercisesHandler; §8.3 | **FUORI PERIMETRO**: non sono previsti due editor contemporanei della lezione. Il salvataggio verifica ma non incrementa la versione della lezione. | Nessuna modifica backend richiesta; non dichiarare protezione aggregata fra editor. |
| A-12 | AdminShell; §14.1 | **CHIUSO — VALIDATO LOCALMENTE**: confronto per segmenti della sezione, query/fragment ignorati. | Test dei dettagli scuola/corso/lezione e titolo della sezione verdi. |
| A-13 | app.config.ts; core/http; §14.2 | **CHIUSO — VALIDATO LOCALMENTE**: loading/antiforgery/notification registrati; GlobalSpinner e toaster montati. | Test sulla configurazione reale: richieste sovrapposte/annullamento, successo, errore e bootstrap/auth silenziosi. |
| A-14 | OrganizationExerciseSchemaTests; §19.2 | **CHIUSO — VALIDATO LOCALMENTE**: 44 casi diretti dello schema consolidato su database temporanei. | CHECK, FK, cancellazioni ristrette, unicità, limiti, posizioni temporanee e inventario tabelle; copertura verde. |
| A-15 | LessonDetail.reload; §14.1 | **CHIUSO — VALIDATO LOCALMENTE**: rifiutata lezione di un altro corso con messaggio comprensibile. | Test: solo GetCourse/GetLesson, nessuna lettura della sequenza né mutazione. |
| A-16 | §3.2 / §4.4 / D-01 | **Valutato e non avviato**, su richiesta dell'utente del 2026-10-02: sostituire il pacchetto `Mediator` 2.1.7 con un mediator interno al repository (`Qwerty.Codi.Mediator`), esponendo le stesse interfacce. Licenza **MIT** verificata sul `.nuspec` ripristinato; l'impatto completo è nella sezione «Mediator: valutazione chiusa senza modifiche». | Non è un lavoro di sola organizzazione: tocca pacchetti, lockfile, copertura, test di compatibilità e più documenti. Va aperto come porzione a sé, con la sua verifica; finché resta fermo, il pacchetto di terze parti è una dipendenza dichiarata e non nascosta. |
| A-17 | §11.2 (passo 2) / §10.2 | **CHIUSO — VALIDATO LOCALMENTE** il 2026-10-02: `CodiHubOriginFilter` confronta l'`Origin` con l'elenco configurato (`Codi:Realtime:AllowedOrigins`, in sviluppo 4200 e 4300) e con l'origine del server stesso, e vale per l'apertura della connessione **e** per ogni metodo. Un `Origin` assente non è un rifiuto: i client non browser non lo inviano e non hanno cookie da presentare. Confronto per intero, non per prefisso. | Nessuna azione aperta. Se in futuro l'API sarà servita da un altro origin in produzione, va aggiunto all'elenco: l'origine del server stesso resta comunque ammessa. |

Le decisioni D-01–D-14 di §22 restano nella specifica. Questi rilievi non cambiano il contratto e non bloccano automaticamente T0/T1.

## Prossima porzione proposta

Il prossimo incremento è T9: installazione, backup, ripristino e prova di 60 minuti su 12 PC,
con i quattro residui dell'admin rinviati al laboratorio.
Il **collaudo nel browser dell'ingresso con il codice** è stato eseguito il 2026-10-09 — home che sente
la lezione, codice errato, ingresso, codice valido in pausa, secondo alunno da un profilo separato,
ripiego, fine lezione che chiude l'ingresso — insieme ai collaudi di T7 (5 ottobre) e della pagina dei
partecipanti (7 ottobre). Nella stessa data è stato collaudato nel browser anche l'**ingresso in aula
con il codice** e l'**esperienza dell'alunno** (riquadri a icona e prova del programma). Nel browser
restano non esercitati soltanto il rallentamento dopo dieci codici errati e il ripiego HTTP oltre
24 KiB dell'anteprima, entrambi coperti dai test locali.
Resta aperto il **gate di formattazione .NET** dei nove file T8 con fine riga CRLF locale,
descritto nella porzione del 2026-10-09.
La specifica funzionale è 0.7 e quella tecnica 0.37: il requisito dell'ingresso è cambiato.

## Passaggio di consegne corrente

Il sorgente è quello descritto da questo registro fino al **2026-10-23**: specifica funzionale
**0.17**, tecnica **0.58**, catalogo **51 comandi e 32 query**, schema **12**. L'ingresso in aula usa
il **codice di lezione** (0.7/0.37) e la pagina dell'alunno mostra percorso, stato a riquadri e prova
locale (0.8–0.10), collaudata nel browser il 2026-10-09; T8 è collaudato nel browser il 2026-10-07 e
il player Blockly del worker il 2026-10-05. Il [rapporto T4](../docs/T4-REGIA-AFFIDABILE.md) conserva
la procedura di quel collaudo, la [mappa di ripresa](../docs/RIPRESA-SVILUPPO.md) descrive il
sorgente corrente.

L'area admin è stata rifinita il 22–23 ottobre 2026: eliminazione definitiva delle sessioni concluse
o mai avviate (`DeleteSession`) e delle versioni di esercizio che non sono le più recenti
(`DeleteExerciseVersion`); conferme in linea con pulsanti a sola icona e nessun pulsante rosso;
tipologia nell'elenco degli esercizi; solo durata massima in minuti nell'editor e nessun programma
iniziale; avvisi standard del dispatcher per salvataggio, copia ed eliminazione, con `silentSuccess`
dentro «Rendi disponibile»; badge Spartan per lo stato dell'esercizio.

**Gate al 2026-10-23, eseguiti dall'agente:** build .NET 0 errori e 0 avvisi,
`dotnet format --verify-no-changes` uscita 0, **1.397 test .NET** (398 dominio, 991 integrazione,
8 protocollo), **389 test Angular** admin, ESLint e Prettier uscita 0, bundle delle due app compilato
(avviso preesistente: iniziale admin 558,76 kB contro il budget di 500 kB).

**Aperto e noto:** collaudo nel browser delle superfici admin aggiunte il 22–23 ottobre; mappatura
dei codici di errore HTTP senza test diretto (lacuna preesistente, §10.7); deroga **A-09** sulla
credenziale di sviluppo, da chiudere prima della consegna in laboratorio; T9 (installazione, backup,
ripristino e prova su 12 PC) come prossima porzione.
Le porzioni precedenti descrivono il sorgente alla data indicata.
Le loro lacune non rappresentano il sorgente corrente.

## Passaggio di consegne — storico

**T2, terza porzione: lezioni, contenuti e asset (schema, dominio e persistenza). Verificata localmente il 2026-09-30.**

Migrazione `0004_lessons_contents_media.sql`: `lesson` con indice `course_id`, `content_item`,
`media_asset` e `lesson_activity` con unico `(lesson_id, position)` e coerenza fra tipo e
riferimento. Dominio in `Domain/Lessons` (`Lesson`, `LessonActivity`, `LessonActivityKind`) e
`Domain/Contents` (`ContentItem`, `ContentBody`, `ContentKind`). Persistenza: quattro entità POCO,
`ContentBodyJson` come forma di `body_json`, quattro interfacce e quattro repository su
`BaseRepository`, mapping FluentMapper e registrazioni scoped.

Decisioni locali registrate in §7.3 (specifica tecnica **0.11**): tipi di contenuto realizzati
`Text` e `Image`, con il riferimento all'asset nel corpo JSON (vincolo applicativo, nessuna chiave
esterna); `Demo` dichiarato non realizzato perché non definito da nessuna sezione; limiti di
titolo e descrizione della lezione allineati a quelli del corso; posizioni `1..N` con temporanee
negative ammesse durante il riordino e zero escluso; `status` di `media_asset` con i valori
`Ready` e `Deleted`. Decisione dell'utente del 30 settembre 2026: gli asset appartengono a T2 e il
contenuto `Demo` non si implementa finché non ha un contratto.

Evidenze: restore `--locked-mode` uscita 0; build Release 0 errori, 0 warning; **457/457 test
.NET** (186 Domain.Tests + 271 IntegrationTests); gate di copertura verde contro la baseline
invariata (Api 84,99%, Application 98,28%, Domain 95,60%, Infrastructure 96,39%), raccolta in
`TestResults/763bc65e31b44f279db7873c1d2b8411`; `scripts/test-coverage-gate.ps1` 4/4.

Prove ancora aperte in questa porzione: i casi d'uso e le pagine; il caricamento e il download
degli asset con lo storage locale; la controprova nel browser HTTPS reale; le regole 409
«archiviazione con sessione aperta», che richiedono T3; `ProtocolTests` senza casi; i behavior di
§4.4; laboratorio su 12 PC.

Non toccati: frontend, dati operativi, pacchetti e lockfile, migrazioni `0001`–`0003`.

**T2, seconda porzione: classi, corsi e associazioni. Validata localmente il 2026-09-30.**

Migrazione `0003` (`classroom`, `course`, `course_classroom`), dominio `Organization` con
`SchoolYearNormalization`, `Classroom`, `Course` e `CourseClassroom`, entità POCO, interfacce e
repository su `BaseRepository`, mapping FluentMapper e registrazioni scoped. Casi d'uso di §10.4
per classi e corsi e query di §10.5 per i loro elenchi e dettagli, più il codice
`ClassroomAlreadyExists` (409). Frontend: classi dentro `/admin/schools`, nuova pagina
`/admin/courses` con associazioni per scuola e classe.

Decisioni locali prese e registrate in §7.2 (specifica tecnica 0.10): anno scolastico
obbligatorio in forma canonica `AAAA/AAAA` con anni consecutivi — la funzionale §13 lo indicava
come eventuale, ma il dizionario tecnico lo include nella chiave di unicità e lo dichiara non
annullabile; descrizione del corso fino a 2000 caratteri; distacco dell'associazione come flag
abbassato, non come cancellazione.

Evidenze: build Release 0 errori, 0 warning; **373/373 test .NET** (134 + 239); gate di copertura
verde contro la baseline invariata (Api 84,99%, Application 98,21%, Domain 95,41%,
Infrastructure 96,05%), raccolta in `TestResults/1edabefb4e344ddd911711bf3da955a8`; frontend
49/49 test, lint e formattazione conformi, build di produzione senza warning.

Prove ancora aperte: percorso reale nel browser con HTTPS e certificato di sviluppo; regole 409
«archiviazione con sessione aperta», che richiedono T3; `ProtocolTests` senza casi; behavior di
§4.4; laboratorio su 12 PC.

Non toccati: dati operativi, pacchetti e lockfile, migrazioni `0001` e `0002`.

**T2, prima porzione: scuole. Validata localmente il 2026-09-30.**

Migrazione `0002` (`school`, indice unico su `normalized_name`), dominio `Organization`
(`SchoolName`, `School`, `NameNormalization` condiviso con `AdminUsername`), entità
`SchoolEntity`, `ISchoolRepository`, casi d'uso `CreateSchool`, `UpdateSchool`, `ArchiveSchool`,
`ListSchools`, `GetSchool`, codici `SchoolAlreadyExists` (409), `ResourceNotFound` (404),
`EntityVersionRequired` (428), contatori a 64 bit come stringhe decimali, endpoint `GET /auth/me`;
frontend con `ApiService`, `AdminSessionService`, interceptor antiforgery e pagine `/admin/login`
e `/admin/schools`. Evidenze: 266/266 test .NET e gate di copertura verde.

Richiesta precedente: portare l'accesso DB al modello di Qwerty.ProjectAnt.Data.
**Refactoring completato e validato localmente.**

Usare IUnitOfWorkContext.CreateAsync e Repo<TRepository>(); i repository sono interni, scoped,
derivati da BaseRepository e condividono il contesto di connessione e transazione. Il chiamante
conferma o annulla. QueryAsync/ExistsAsync accettano predicati sulle entità POCO di
Application/Persistence; FluentMapper traduce nomi C# in colonne SQL. Il dominio resta puro:
convertire con FromDomain/ToDomain. Le ricerche ordinarie non richiedono nuovi metodi WHERE.
Guida ed esempi: docs/PERSISTENZA.md; contratto tecnico 0.8 (§8.2, §20.6, §24).

Login: lettura/hashing fuori transazione; UPDATE con versione e INSERT credenziale atomici.
Un errore/cancellazione dopo INSERT annulla entrambe le scritture; un tentativo errato viene
confermato prima del rifiuto. Bootstrap: controllo e creazione nella stessa transazione.
Logout e verifica cookie adottano la UoW senza modificare i contratti HTTP. Restano corretti
antiforgery, revoca dal contesto verificato e rivalutazione del blocco dopo conflitto.

Evidenze finali: build Release 0 warning/0 errori, 190/190 test .NET e gate originale verde:
Api 83,65%, Application 97,72%, Domain 94,24%, Infrastructure 95,38%.
Baseline invariata; risultati in TestResults/uow-refactor-20260930-final. Frontend e dipendenze
non modificati: le loro evidenze restano quelle precedenti. Nessuna migrazione o dato operativo
modificati. I test HTTP usano loopback e HTTPS simulato.

Prove ancora aperte: browser HTTPS reale, tastiera/SVG Blockly sui PC del laboratorio,
bootstrap CLI come processo, efficacia .gitignore e audit npm aggiornato.
Al momento di questa evidenza storica, ProtocolTests non contiene casi. T4 aggiunge otto casi il 5 ottobre 2026. I futuri behavior transazionali/idempotenti
devono fornire una sola UoW ai partecipanti, senza aprire lavori indipendenti annidati.

Prossimo passo: T2, schema e casi d'uso di organizzazione con validazione e transazioni,
poi servizi e pagine amministrative, interceptor antiforgery e contratti generati.
Rilievi A-01–A-04 e decisioni D-xx restano applicabili quando toccano tale lavoro.
