<p align="center">
  <img src="public/brand/codi-logo.svg" alt="Codì" width="364" height="116">
</p>

# Codì — workspace Angular

Un workspace Angular 22 standalone e zoneless, con due applicazioni indipendenti.
Dipendenze, `node_modules` e `package-lock.json` sono unici nella root Web; ciascuna app
ha un `package.json` minimo con il proprio comando `start`, senza dipendenze aggiuntive.
Il backend .NET e il dispatcher CQRS restano comuni (§10, §14).

| Progetto CLI   | Sorgenti              | Indirizzo di sviluppo           |
| -------------- | --------------------- | ------------------------------- |
| `web.admin`    | `projects/admin/src`  | `https://localhost:4200/admin/` |
| `web.frontend` | `projects/public/src` | `https://localhost:4300/`       |

## Avvio e verifiche

Richiede Node.js 24, npm 11 e il backend .NET su HTTPS 5200.
Per la preparazione .NET, consulta il [README principale](../../README.md#preparazione-al-primo-avvio).
Tutti i comandi di questa guida partono da `Codi/src/Qwerty.Codi.Web`, salvo indicazione diversa.

La versione di Node locale è gestita tramite nvm: i comandi usano la versione attiva e non
eseguono installazioni o cambi di versione. Dalla root Web, `npm run start` equivale a
`npm run start:admin`; `npm run start:frontend` è l'alias di avvio public.

Dalla root `Codi/` puoi usare anche [start-admin.bat](../../start-admin.bat),
[start-public.bat](../../start-public.bat) o [start-angular.bat](../../start-angular.bat)
per avviare entrambe le app in terminali separati. Le API hanno il launcher
[start-server.bat](../../start-server.bat).

### Installazione delle dipendenze

Installa una sola volta dalla radice Web.

```powershell
npm ci
```

Ripeti `npm ci` dopo una modifica del lockfile.
Le cartelle `projects/admin` e `projects/public` non richiedono installazioni separate.

### Certificati HTTPS al primo avvio

Angular legge `ssl/localhost.pem` e `ssl/localhost-key.pem`.
Se i file locali esistono e sono attendibili, conserva la configurazione corrente.
Su una macchina nuova, prepara prima il certificato ASP.NET Core.

```powershell
dotnet dev-certs https --trust
dotnet dev-certs https --check --trust
```

Esporta il certificato per lo sviluppo locale soltanto se mancano i file Angular.

```powershell
New-Item -ItemType Directory -Path .\ssl -Force | Out-Null
$certificatePath = Join-Path (Get-Location) 'ssl\localhost.pem'
dotnet dev-certs https --export-path $certificatePath --format PEM --no-password
Move-Item -LiteralPath .\ssl\localhost.key -Destination .\ssl\localhost-key.pem
```

L'esportazione genera `localhost.pem` e `localhost.key`; la rinomina corrisponde al percorso richiesto da `angular.json`.
La chiave senza password serve esclusivamente alle prove locali e resta esclusa da Git.
La [documentazione Microsoft](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-dev-certs) descrive esportazione e attendibilità.

### Avvio dei tre servizi

Avvia il backend in un terminale dalla radice Web.

```powershell
dotnet run --project ..\Qwerty.Codi.Api --launch-profile Qwerty.Codi.Api
```

Avvia l'admin in un secondo terminale dalla radice Web.

```powershell
npm run start:admin
```

Avvia l'app alunni in un terzo terminale dalla radice Web.

```powershell
npm run start:frontend
```

Apri l'[accesso docente](https://localhost:4200/admin/login) e la [pagina alunni](https://localhost:4300/).
Per fermare ogni servizio, premi Ctrl+C nel suo terminale.

Per avviare direttamente una delle app, da `projects/admin` oppure `projects/public`
esegui sempre `npm run start`. Anche i due launcher singoli usano questo comando nella
cartella dell'app scelta. Installa le dipendenze soltanto nella root Web con `npm ci`;
non servono installazioni né lockfile nelle cartelle delle app.
Gli script locali entrano nella root Web prima di eseguire Angular, così proxy e
certificati vengono risolti nello stesso modo per entrambe le app.

`npm start` nella root Web avvia la parte admin.
I cookie sono `Secure`, quindi entrambe le app usano HTTPS anche in sviluppo.

Entrambi i server usano `proxy.conf.json` per inoltrare `/api/**`, `/health/**`
e `/hubs/**` (con WebSocket) a `https://localhost:5200`. Il builder Vite richiede `/**`
per i sottopercorsi; dopo modifiche al proxy riavvia `ng serve`.

Ogni app configura `API_AREA` in `app.config.ts`: admin usa `/api/v1/admin`, public
`/api/v1/public`. `ApiService` condiviso offre `executeCommand` e `executeQuery`
nel gruppo scelto; i servizi dedicati admin usano `ADMIN_API_PREFIX` per bootstrap,
login, profilo e logout. Il server limita il catalogo per area e verifica ancora
identità, policy e proprietà. Le route delle pagine `/admin/*` sono distinte dalle API.
La [guida endpoint](../../docs/API-ENDPOINTS.md) elenca rotte disponibili e lavoro futuro.

### Controlli locali

Esegui i controlli dalla radice Web.

```powershell
npm run lint
npm run format:check
npm run test:admin -- --watch=false
npm run test:frontend -- --watch=false
npm run build
npm run audit:runtime
```

`test` esegue entrambe le applicazioni. L'admin include anche i test di `libraries/`, una volta
sola; il frontend esegue i propri test. Sono disponibili `test:admin`, `test:frontend`,
`build:admin` e `build:frontend`. `watch` osserva la build dell'admin.
I controlli locali verificano entrambi i bundle senza due installazioni.
La cache npm è locale in `Codi/.npm-cache` per effetto di `.npmrc`.

## Sviluppo quotidiano

I server Angular aggiornano il browser dopo le modifiche ai sorgenti.
Il comando `npm run watch` osserva la compilazione admin, ma non avvia un server web.

Usa questi comandi per limitare i test all'app interessata.

```powershell
npm run test:admin -- --watch=false
npm run test:frontend -- --watch=false
```

Senza `--watch=false`, i comandi mantengono attiva l'esecuzione dei test durante lo sviluppo.
I test condivisi di `libraries` appartengono alla suite admin.
Per una modifica comune, esegui entrambe le suite.

I percorsi principali hanno queste responsabilità.

| Percorso                                | Responsabilità                                  |
| --------------------------------------- | ----------------------------------------------- |
| `projects/admin/src/app/features/admin` | Pagine docente e casi d'uso dell'interfaccia    |
| `projects/admin/src/app/core`           | Autenticazione, trasporto e servizi admin       |
| `projects/admin/src/app/ui`             | Componenti Helm locali                          |
| `projects/public/src/app`               | Ingresso, sessione e lavoro degli alunni        |
| `libraries/api`                         | Contratti e trasporto comuni                    |
| `libraries/exercise-engine`             | Cataloghi Blockly, serializzazione e motori     |
| `public/brand`                          | Logo e simbolo condivisi dalle due applicazioni |

Mantieni i contratti comuni in `libraries/api` e i componenti docente nell'admin.
Le librerie comuni e l'app alunni non importano Spartan o codice admin.
Tutte le form admin usano Signal Forms e Spartan.
La [guida delle form](../../docs/ADMIN-SIGNAL-FORMS.md) descrive validazione, pubblicazione e controlli associati ai campi.

Gli editor `GridPath`, `Quiz` e `ValueProgram` condividono la barra **Rendi disponibile**.
La barra salva la bozza, verifica i dati e pubblica la revisione restituita dal server.
Il comando **Salva bozza** resta separato.
L'elenco e gli editor distinguono le bozze dagli esercizi disponibili e dalle modifiche da pubblicare.

Il [README principale](../../README.md#sviluppo-del-progetto) descrive la verifica completa di una lezione e l'uso di dati isolati.

## Logo condiviso

Le due app copiano gli stessi file da `public/brand` durante la build.
Riusa gli asset esistenti quando aggiungi una pagina.

| Asset                                       | Uso                                             |
| ------------------------------------------- | ----------------------------------------------- |
| [codi-logo.svg](public/brand/codi-logo.svg) | Logo completo per intestazione, accesso e menu  |
| [codi-mark.svg](public/brand/codi-mark.svg) | Simbolo per menu compresso e icona della scheda |

La [guida del logo](../../docs/LOGO-CODI.md) descrive colori, percorsi e tema scuro.

## Admin e Spartan

L'admin usa [Spartan UI](https://spartan.ng/documentation): `@spartan-ng/brain` è la dipendenza
per il comportamento accessibile; i componenti Helm sono sorgenti locali generati in
`projects/admin/src/app/ui`. Tailwind CSS 4 e il tema sono configurati
soltanto nel foglio globale dell'admin. Tutte le form admin usano Angular Signal Forms
e layout Spartan Field, con Input, Textarea e Native Select. Anche editor inline, filtri e
selettori usano FieldTree; il selettore della sequenza usa Native Select; upload e controllo file sono stati rimossi.
La [guida delle form](../../docs/ADMIN-SIGNAL-FORMS.md) elenca superfici, regole e verifiche.
I componenti generati conservano selettori `hlm`, alias di input e intestazioni originali del
fornitore; `eslint.config.mjs` riconosce loro il prefisso `hlm` sugli attributi e sugli elementi.
Oltre a pulsanti, input, label, card, field, native-select e textarea sono installati sidebar, sheet, separator, skeleton e
tooltip; sono presenti anche breadcrumb, dialog, pagination, select, sonner, spinner, table e tabs, con la libreria di icone `@ng-icons/lucide` alla stessa versione di `@ng-icons/core`.

Per aggiungere componenti con il generatore ufficiale, dalla radice del workspace:

```powershell
npm run ng -- generate @spartan-ng/cli:ui dialog
```

`components.json` fissa destinazione, alias e stile `vega`; gli alias Helm vengono aggiornati
dal generatore. La configurazione `tsconfig.app.json` alla radice è un ingresso di compatibilità
per il generatore; i builder delle app usano i rispettivi file nei progetti.

Le route Angular dell'admin sono locali (`login`, la radice dell'area, `schools`, `courses`,
`exercises`, `sessions`, `local-recovery`, le route di dettaglio e `workspace-prototype`): la base
`/admin/` del builder produce gli indirizzi esterni
`/admin/login`, `/admin/` (pagina iniziale), `/admin/schools`, `/admin/courses`,
`/admin/exercises`, `/admin/sessions`, `/admin/local-recovery` e `/admin/workspace-prototype`,
oltre ai dettagli riportati sotto. I collegamenti interni usano `RouterLink` o `Router`, così la base
non viene aggiunta due volte.

Le superfici di configurazione hanno una route propria per ogni livello: `/admin/schools`,
`/admin/schools/:schoolId`, `/admin/courses`, `/admin/courses/:courseId` e
`/admin/courses/:courseId/lessons/:lessonId`; le sessioni seguono lo stesso schema con
`/admin/sessions`, `/admin/sessions/:sessionId` e `/admin/sessions/:sessionId/participants`. Il nome
della riga di un elenco è il collegamento al
livello successivo, quindi ogni pagina di dettaglio è indirizzabile e ricaricabile.

Le pagine dell'area docente vivono dentro `features/admin/admin-shell`: il guscio tiene il menu
delle sezioni — nei gruppi Organizzazione, Didattica e Regia: `Scuole e classi`, `Corsi e lezioni`,
`Esercizi` e `Sessioni` — il titolo della sezione corrente e
l'uscita dall'area. Il router confronta i segmenti della sezione: nei dettagli la voce resta
attiva, ignorando query e fragment. Il titolo segue la sezione o la route fuori menu. L'accesso sta fuori dal guscio. La radice
dell'area è la pagina iniziale `features/admin/home`, che elenca le sezioni in riquadri; senza
sessione il guard `features/admin/admin-session-guard` riporta a `/login`. Etichette e destinazioni
stanno in un posto solo (`features/admin/admin-sections.ts`), condiviso da menu e pagina iniziale.

Gli elenchi senza voci usano il componente condiviso `app-empty-state`
(`projects/admin/src/app/shared/components`, alias `@shared/components`), così uno stato vuoto si
presenta allo stesso modo in tutte le pagine.

Gli elenchi usano DataTable con paginazione locale, i dettagli DetailHeader con breadcrumb;
creazione/modifica dei metadati sono in dialoghi. La lezione compone la sequenza di esercizi
con trascinamento CDK e frecce da tastiera. La libreria esercizi apre l'editor della bozza
(`/admin/exercises/:exerciseId/editor`) con composizione a blocchi, anteprima, soluzione privata e
pubblicazione; `workspace-prototype` resta una pagina separata di prova del solo round trip Blockly.

LoadingService, GlobalSpinner e interceptor di feedback globale sono collegati:
app.config.ts registra loading, antiforgery e notification; il guscio monta spinner e toaster.
La sidebar mantiene la sezione attiva nei dettagli. Un URL corso/lezione incoerente viene
rifiutato prima di caricare la sequenza.
Il [registro di sviluppo](../../.github/STATO-SVILUPPO.md) documenta gli esiti dei controlli e le verifiche nel browser.

## Frontend alunni e codice condiviso

Il frontend contiene il benvenuto su `/`, l'ingresso su `/join` e la lezione su `/lezione`. L'ingresso
chiede periodicamente al server se una lezione è in play e, quando c'è, il **codice di lezione** di
otto cifre e il nome; la scelta di scuola e classe resta dietro «Non ho il codice». La lezione apre
il collegamento con l'hub del partecipante, mostra il **percorso scuola / classe / lezione / esercizio**
con il nome dell'alunno sotto e **riquadri di stato con icona colorata**
(collegamento, permesso di lavorare, stato del programma), tiene **il progetto a sinistra e il programma a
destra** su una pagina a tutta larghezza — le colonne tornano una sotto l'altra sugli schermi stretti —
consente di **provare il proprio
programma** con l'animazione della scena — la prova vive nella pagina e non nel worker, quindi non
produce checkpoint né telemetria — e persiste i checkpoint della corsa comandata. L'interfaccia resta
volutamente grezza: la grafica definitiva non
è stata scelta. Non importa Spartan né fogli di stile dell'admin.
Il lint vieta import dell'admin o di Spartan nelle librerie comuni e nel frontend.

| Alias                | Contenuto                                                                 |
| -------------------- | ------------------------------------------------------------------------- |
| `@codi/api`          | `libraries/api`: dispatcher, errori, antiforgery e contratti comuni       |
| `@exercise-engine/*` | `libraries/exercise-engine`: catalogo prototipo e serializzazione Blockly |
| `@core/*`            | Servizi dell'admin, tra cui la sessione amministrativa                    |
| `@shared/*`          | Componenti riusabili interni all'admin                                    |
| `@spartan-ng/helm/*` | Componenti Helm locali dell'admin                                         |

I componenti non usano `HttpClient` direttamente. I futuri servizi realtime comuni vanno nelle
librerie solo quando esiste una responsabilità condivisa, evitando duplicazioni tra app.

## Bundle per lo stesso host

`npm run build` compila le due app e prepara il pacchetto statico locale `dist/site`:

```text
dist/site/
  index.html                 Frontend alunni
  main-*.js, styles-*.css
  admin/
    index.html               Admin, base /admin/
    main-*.js, styles-*.css
```

Il backend sa servire questa struttura da `wwwroot`, con fallback SPA distinto per `/admin/*`
e per il frontend. Le rotte backend sconosciute e gli asset assenti restano `404`.
La build prepara solo l'artefatto: non copia in `wwwroot`, non pubblica e non distribuisce.
In un rilascio autorizzato si colloca il contenuto di `dist/site` nel `wwwroot` dell'API.

Per verificare localmente il pacchetto compilato, ferma il backend già presente su 5200.
Poi esegui questi comandi dalla radice Web, dopo `npm run build`.

```powershell
$bundlePath = (Resolve-Path .\dist\site).Path
dotnet run --project ..\Qwerty.Codi.Api --launch-profile Qwerty.Codi.Api -- --webroot $bundlePath
```

Apri l'[app alunni compilata](https://localhost:5200/) e l'[admin compilato](https://localhost:5200/admin/).
Questa modalità usa soltanto il backend; i server Angular su 4200 e 4300 non servono.
Le modifiche ai sorgenti richiedono una nuova build per aggiornare il pacchetto statico.

Il profilo API di Visual Studio mantiene `https://localhost:5200` e `launchBrowser: false`:
F5 non apre il browser. Le due app Angular vengono avviate dai rispettivi terminali.

## Limiti della verifica locale

Il [registro corrente](../../.github/STATO-SVILUPPO.md) distingue test automatici, verifiche nel browser e limiti ancora aperti.
La verifica sui dodici computer del laboratorio resta nel collaudo di §19.5.
Gli esiti storici non certificano una nuova modifica.
