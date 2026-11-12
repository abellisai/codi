<p align="center">
  <img src="src/Qwerty.Codi.Web/public/brand/codi-logo.svg" alt="Codì" width="364" height="116">
</p>

# Codì

Piattaforma didattica per una lezione di informatica in una classe di scuola primaria: un docente e
dodici PC di alunni di 8–9 anni. Comprende la regia live della lezione, esercizi a blocchi (Blockly)
con copie indipendenti sui client, salvataggi e osservazione del lavoro degli alunni.

Il nome del prodotto è **Codì**. Cartelle e identificatori tecnici usano **Codi** senza accento.
La soluzione è `Qwerty.Codi.slnx`; i namespace iniziano con `Qwerty.Codi`.
La [guida alla rinomina](docs/RINOMINA-CODI.md) descrive configurazione e compatibilità dei dati.

Questa guida descrive il progetto presente, la preparazione della macchina e il lavoro quotidiano di sviluppo.
Le specifiche definiscono i requisiti; il registro documenta le verifiche eseguite.

Per iniziare, segui queste sezioni nell'ordine indicato.

1. [Requisiti](#requisiti).
2. [Preparazione al primo avvio](#preparazione-al-primo-avvio).
3. [Avvio rapido Windows](#avvio-rapido-windows).
4. [Sviluppo del progetto](#sviluppo-del-progetto).
5. [Comandi di verifica](#comandi-di-verifica-205).

| Documento | Contenuto |
|---|---|
| [Specifica funzionale](SPECIFICHE_PIATTAFORMA_DIDATTICA.md) | Requisiti funzionali: prevale sulla specifica tecnica in caso di conflitto. |
| [Specifica tecnica](SPECIFICA_TECNICA_PIATTAFORMA_DIDATTICA.md) | Contratto tecnico: architettura, dati, protocolli, convenzioni, piano §21. |
| [Mappa di ripresa](docs/RIPRESA-SVILUPPO.md) | Stato ricostruito dal sorgente, differenze recenti e priorità per proseguire. |
| [Stato di sviluppo](.github/STATO-SVILUPPO.md) | Incrementi, porzione corrente, evidenze e passaggio di consegne. Da confrontare sempre con i file. |
| [Matrice di copertura](docs/TEST-COVERAGE-MATRIX.md) | Comportamenti coperti da test, baseline di copertura e buchi noti (§20.4). |
| [Accesso ai dati](docs/PERSISTENZA.md) | Repository base RepoDB, filtri per espressione e unit of work condivisa. |
| [Guida agli agenti](.github/README.md) | Ruoli, procedure, skill e limiti di discovery degli ambienti agentici. |
| [Frontend Angular](src/Qwerty.Codi.Web/README.md) | Prerequisiti, script e alias del progetto web. |
| [Logo Codì](docs/LOGO-CODI.md) | File SVG condivisi e applicazione del logo nelle due aree. |
| [Componenti Spartan](docs/SPARTAN-UI.md) | Catalogo ufficiale, configurazione admin e skill locali al progetto. |

Il sorgente corrente contiene fondazioni, accesso docente, organizzazione di scuole/classi e
corsi/lezioni con la sequenza degli esercizi, autore Blockly con bozza e versioni pubblicate,
aula live (sessioni, ingressi, regia, hub) e il player che esegue l'esercizio nel worker
dell'alunno, con salvataggi, copie locali e finalizzazione. L'ingresso in aula usa il **codice di
lezione** di otto cifre che il docente riceve all'avvio e detta a voce: l'alunno digita il codice e il
proprio nome, e la scelta di scuola e classe resta come ripiego. La pagina dell'alunno mostra il
**percorso scuola / classe / lezione / esercizio** con il suo nome sotto, lo stato con **riquadri a
icona colorata** — collegamento, permesso di lavorare, stato del programma — e consente di **provare il
proprio algoritmo** con l'animazione della scena, senza toccare l'esecuzione
comandata dal docente. L'admin ha le sezioni
Organizzazione, Didattica e Regia; l'app public ha l'ingresso `/join` e la lezione `/lezione`.
T0–T8 hanno evidenze locali nel [registro](.github/STATO-SVILUPPO.md): T7 e il percorso
dell'anteprima di T8 sono stati collaudati nel browser, mentre la UI definitiva degli alunni
non è ancora scelta e il laboratorio su 12 PC resta di T9. Associazioni corso-classe, iscrizioni e
contenuti/immagini sono eliminati dal prodotto per decisione dell’utente; non sono arretrati.
La [mappa di ripresa](docs/RIPRESA-SVILUPPO.md) distingue comportamento presente,
contratti futuri e verifiche mancanti.

Lo schema corrente è alla versione **12**, con gli script da
[`0001_schema.sql`](src/Qwerty.Codi.Infrastructure/Migrations/Scripts/0001_schema.sql) a
[`0012_exercise_type_value_program.sql`](src/Qwerty.Codi.Infrastructure/Migrations/Scripts/0012_exercise_type_value_program.sql).
All'avvio il backend applica le migrazioni mancanti quando quelle già registrate corrispondono agli script distribuiti.
Versioni sconosciute, sequenze incomplete o checksum divergenti impediscono l'avvio.
Per le prove usa un database isolato. Conserva i dati esistenti prima di pianificare un aggiornamento.

## Requisiti

| Requisito | Valore | Dove è fissato |
|---|---|---|
| Kit di sviluppo .NET | `10.0.401`, `rollForward: latestPatch` | [`global.json`](global.json) |
| Node.js | 24, versione locale gestita tramite nvm | [Guida del workspace Web](src/Qwerty.Codi.Web/README.md#avvio-e-verifiche) |
| npm | 11 (dichiarato da `packageManager`) | [`src/Qwerty.Codi.Web/package.json`](src/Qwerty.Codi.Web/package.json) |
| SQLite | provider `RepoDb.Sqlite.Microsoft` inizializzato all'avvio | [`src/Qwerty.Codi.Infrastructure`](src/Qwerty.Codi.Infrastructure) |

Le dipendenze **non** si scaricano nel profilo utente:

- i pacchetti NuGet finiscono in `packages/` alla radice, per effetto di [`NuGet.config`](NuGet.config)
  (`globalPackagesFolder`) e di [`Directory.Build.props`](Directory.Build.props)
  (`RestorePackagesPath`); i due valori devono restare identici;
- i pacchetti npm stanno in `src/Qwerty.Codi.Web/node_modules/`, accanto alla cartella del
  progetto, con il lockfile nella stessa cartella.

## Preparazione al primo avvio

Per una copia nuova, esegui questi comandi dalla cartella che conterrà il progetto.

```powershell
git clone https://github.com/qwertystudiosrl/AulaViva.git Codi
Set-Location .\Codi
```

Se hai già una copia locale, apri PowerShell nella sua radice, accanto a `Qwerty.Codi.slnx`.
Il nome remoto del repository resta `AulaViva`; il progetto locale si chiama Codì.

Verifica i programmi disponibili nel terminale.

```powershell
dotnet --version
node --version
npm --version
```

Ripristina i pacchetti .NET dalla radice del progetto.

```powershell
dotnet restore .\Qwerty.Codi.slnx --locked-mode
```

Installa le dipendenze delle due applicazioni dalla sola radice Web.

```powershell
Push-Location .\src\Qwerty.Codi.Web
npm ci
Pop-Location
```

Prepara il certificato HTTPS del backend sulla macchina di sviluppo.

```powershell
dotnet dev-certs https --trust
dotnet dev-certs https --check --trust
```

Le app Angular richiedono anche certificato e chiave nei percorsi configurati in `angular.json`.
Segui la [preparazione dei certificati Web](src/Qwerty.Codi.Web/README.md#certificati-https-al-primo-avvio).
Questa preparazione serve su ogni nuova macchina; i certificati non fanno parte del repository.

## Avvio locale

### Avvio rapido Windows

Completa prima la preparazione iniziale. Poi avvia il backend e le due applicazioni in terminali distinti.

```powershell
# Terminale 1, dalla radice Codi
.\start-server.bat
```

```powershell
# Terminale 2, dalla radice Codi
.\start-admin.bat
```

```powershell
# Terminale 3, dalla radice Codi
.\start-public.bat
```

Nella root `Codi/` sono disponibili quattro launcher, anche tramite doppio clic.

| File | Avvio |
|---|---|
| [start-server.bat](start-server.bat) | API su `https://localhost:5200`, con il profilo di sviluppo e senza browser automatico. |
| [start-admin.bat](start-admin.bat) | Solo admin: `npm run start` da `projects/admin`, su `https://localhost:4200/admin/`. |
| [start-public.bat](start-public.bat) | Solo alunni: `npm run start` da `projects/public`, su `https://localhost:4300/`. |
| [start-angular.bat](start-angular.bat) | Admin e public in due terminali separati. Le API si avviano con `start-server.bat`. |

Apri l'[accesso docente](https://localhost:4200/admin/login) e la [pagina alunni](https://localhost:4300/).
Le interfacce di programmazione del backend, indicate come API, rispondono su `https://localhost:5200`.
In sviluppo il backend da solo non serve le pagine Angular finché non riceve il pacchetto statico.

I launcher risolvono le cartelle rispetto alla propria posizione e usano .NET, Node e npm
presenti nel PATH, inclusa la versione Node attiva tramite nvm. Richiedono le dipendenze
web installate con `npm ci` e i certificati locali descritti nella guida frontend;
non installano pacchetti e non cambiano Node. In caso di errore il terminale rimane aperto
con il messaggio. Per fermare un servizio premi **Ctrl+C** nel relativo terminale.

### API

```powershell
dotnet run --project .\src\Qwerty.Codi.Api --launch-profile Qwerty.Codi.Api
```

L'indirizzo è `https://localhost:5200`, fissato in
`src/Qwerty.Codi.Api/Properties/launchSettings.json`: lo stesso profilo lo usa **F5** in
Visual Studio, e la stessa porta è quella a cui inoltra il proxy del frontend. Serve HTTPS e non
HTTP perché i cookie amministrativi e antiforgery sono `Secure` (§18.3): su `http` il browser non
li conserva e l'accesso non regge. Il certificato è quello di sviluppo di ASP.NET Core.
Il comando `dotnet dev-certs https --check --trust` ne verifica presenza e attendibilità sulla macchina corrente.

F5 avvia soltanto le API su 5200: `launchBrowser: false` impedisce l'apertura automatica del
browser. Node e npm dello sviluppo web usano la versione attiva selezionata tramite nvm;
gli script del progetto non cambiano la versione di Node.

All'avvio l'applicazione valida la configurazione (§18.2, §20.9), prepara il database — directory
dati, provider RepoDB, journal WAL e migrazioni (§16.1) — e risolve il registro delle richieste
prima di mettersi in ascolto (§4.4). Se la configurazione è incompleta o lo schema non è allineato,
il servizio non parte: è un comportamento voluto, non un guasto. Al primo avvio viene creato
`src/Qwerty.Codi.Api/data/codi.db` con lo schema della versione corrente.

### Primo account amministrativo

Il percorso ordinario crea il primo amministratore da **riga di comando locale**, con la password letta da una variabile d'ambiente e mai dalla riga di comando (§9.1).
La modalità locale crea l'account ed esce senza mettere in ascolto il servizio.

```powershell
$env:CODI_BOOTSTRAP_PASSWORD = "una-password-scelta"
dotnet run --project .\src\Qwerty.Codi.Api -- --bootstrap-admin Maestra
```

- Senza `CODI_BOOTSTRAP_PASSWORD` il comando si rifiuta di procedere ed esce con codice 1.
- Un secondo bootstrap è rifiutato (`BootstrapNotAllowed`) ed esce con codice 1 senza creare un
  secondo account.
- La password non compare mai nei log.

In Development è presente la deroga `Codi:SeedAdmin`: l'avvio crea l'account configurato
se non esiste un admin attivo. Quindi il comando locale non è obbligatorio nell'ambiente
di sviluppo già configurato. La deroga A-09 va chiusa prima del laboratorio; non costituisce
il contratto di produzione. Dettagli nella specifica tecnica §9.1 e nello stato.

### Endpoint di salute

| Rotta | Significato |
|---|---|
| `GET /health/live` | Il processo risponde. |
| `GET /health/ready` | Accesso al database, journal WAL e schema allineato alle migrazioni; configurazione validata all'avvio. |

Nessuna delle due espone dettagli sensibili (§18.4).

### Accesso amministrativo

| Rotta | Effetto |
|---|---|
| `GET /api/v1/admin/bootstrap` | Emette i token antiforgery dell'accesso docente e dichiara i nomi di cookie/header. |
| `GET /api/v1/public/bootstrap` | Emette i token antiforgery dell'app alunni e il ticket di ammissione, consumato da `POST /api/v1/public/join`. |
| `POST /api/v1/admin/auth/login` | Corpo JSON `{ "username": "…", "password": "…" }`; risponde `{ "username": "…" }` ed emette il cookie `__Host-CodiAdmin`. |
| `POST /api/v1/admin/auth/logout` | Revoca la credenziale, cancella il cookie e risponde `204`; senza credenziale valida risponde `401`. |
| `GET /api/v1/admin/auth/me` | Profilo minimo della credenziale amministrativa verificata, oppure 401. |

Il cookie è `HttpOnly`, `Secure` e `SameSite=Strict`, con scadenza assoluta di 8 ore e senza
rinnovo automatico (§9.1). Il prefisso `__Host-` è accettato dal browser solo su HTTPS: in
laboratorio l'area amministrativa richiede HTTPS o un proxy che lo termini.

`__Host-CodiAntiforgeryCookie` è il cookie interno del framework, `HttpOnly`.
`__Host-CodiAntiforgery` contiene invece il request token leggibile dal frontend: il client lo
copia in `X-Codi-Antiforgery` sulle mutazioni. Il login emette un nuovo request token legato
all'identità autenticata; un token anonimo precedente non basta. Il comando `LogoutAdmin` ha
corpo `{}` e revoca soltanto la credenziale corrente verificata dal server: identificativi e
contesti dichiarati nel payload non concedono autorità.

Il resto della logica applicativa passa dai due endpoint generici del dispatcher (§10.1):
`POST /api/v1/admin/execute/command/{commandName}` e `POST /api/v1/admin/execute/query/{queryName}`. L'app alunni usa gli stessi modelli sotto `/api/v1/public/execute/`;
registro e handler sono condivisi, catalogo HTTP e policy distinguono le aree. Le vecchie
rotte non sono alias. La [guida endpoint](docs/API-ENDPOINTS.md) riporta strategia,
percorsi disponibili e contratti futuri. I nomi pubblici restano quelli di §10.4–§10.5.

### Frontend

```powershell
Set-Location .\src\Qwerty.Codi.Web
npm ci
npm run start:admin
```

L'admin usa `https://localhost:4200/admin/`; in un secondo terminale `npm run start:frontend`
avvia l'app alunni su `https://localhost:4300/`. Entrambe usano lo stesso proxy verso le API.
Nella root Web, `npm run start` avvia l'admin; `start:admin` e `start:frontend` sono alias
per gli avvii delle rispettive app. Da `projects/admin` o `projects/public` il comando
è sempre `npm run start` e avvia soltanto l'app scelta. Le due cartelle hanno manifest
minimi per l'avvio; dipendenze, `node_modules` e lockfile restano comuni nella root Web.
Dettagli, librerie condivise e bundle in
[`src/Qwerty.Codi.Web/README.md`](src/Qwerty.Codi.Web/README.md).

## Sviluppo del progetto

Apri `Codi` come cartella di lavoro nell'editor.
Apri `Qwerty.Codi.slnx` in Visual Studio per il backend.
La soluzione .NET non include le due app Angular; i loro comandi partono dalla radice Web.

### Struttura del codice

| Percorso | Responsabilità |
|---|---|
| `src/Qwerty.Codi.Domain` | Invarianti e logica di dominio |
| `src/Qwerty.Codi.Application` | Casi d'uso, richieste, policy e contratti |
| `src/Qwerty.Codi.Infrastructure` | Repository RepoDB, SQLite e migrazioni |
| `src/Qwerty.Codi.Api` | Configurazione, autenticazione, endpoint e hub SignalR |
| `src/Qwerty.Codi.Web/projects/admin` | Area docente, form Spartan ed editor esercizi |
| `src/Qwerty.Codi.Web/projects/public` | Ingresso e lavoro degli alunni |
| `src/Qwerty.Codi.Web/libraries` | Trasporto comune e motori degli esercizi |
| `tests` | Test .NET di dominio, integrazione e protocollo |
| `docs` | Guide di architettura, sviluppo e verifica |

### Ciclo di lavoro

Segui questa sequenza per ogni modifica applicativa.

1. Leggi [AGENTS.md](AGENTS.md), le specifiche pertinenti e il [registro corrente](.github/STATO-SVILUPPO.md).
2. Individua il comportamento nel sorgente e nei test esistenti.
3. Avvia il backend e l'app interessata.
4. Modifica il codice e aggiungi un test del comportamento.
5. Esegui i [controlli del perimetro](#comandi-di-verifica-205).
6. Verifica il percorso nel browser.
7. Aggiorna la documentazione e registra gli esiti effettivi.

Le form admin usano Signal Forms e Spartan.
I componenti accedono al backend tramite servizi tipizzati.
La [guida endpoint](docs/API-ENDPOINTS.md) descrive la separazione fra area docente e area alunni.

Per ricompilare il backend dopo ogni modifica, usa un terminale dalla radice `Codi`.

```powershell
dotnet watch --project .\src\Qwerty.Codi.Api run --launch-profile Qwerty.Codi.Api
```

Ferma prima il backend già avviato sulla porta 5200.
Le app avviate con `npm run start` aggiornano il browser dopo le modifiche ai sorgenti.
Riavvia Angular dopo una modifica a `proxy.conf.json`.

### Dati isolati per lo sviluppo

Per una verifica indipendente, scegli una nuova directory prima di avviare il backend.
Il comando seguente crea dati di prova sotto `TestResults`, esclusi dal versionamento.

```powershell
$env:Codi__DataDirectory = Join-Path (Get-Location) ('TestResults\dev-' + [Guid]::NewGuid().ToString('N'))
dotnet run --project .\src\Qwerty.Codi.Api --launch-profile Qwerty.Codi.Api
```

Dopo l'arresto, rimuovi la variabile dal terminale per ripristinare la configurazione ordinaria.

```powershell
Remove-Item Env:\Codi__DataDirectory
```

### Verifica di una lezione

Il percorso completo usa due browser o due profili distinti.

1. Accedi all'admin.
2. Crea scuola, classe, corso e lezione.
3. Crea un esercizio `GridPath`, `Quiz` o `ValueProgram`.
4. Premi **Rendi disponibile** nell'editor.
5. Aggiungi l'esercizio alla sequenza della lezione.
6. Crea e avvia una sessione per quella classe e lezione.
7. Apri l'app alunni nell'altro browser.
8. Inserisci il codice di otto cifre e il nome dell'alunno.
9. Assegna l'esercizio dalla regia docente.

«Rendi disponibile» salva, verifica e pubblica la revisione corrente.
La pubblicazione non assegna automaticamente l'esercizio agli alunni.
«Salva bozza» conserva il lavoro per una modifica successiva.

### Problemi di avvio

| Sintomo | Controllo |
|---|---|
| `dotnet` non trova la versione richiesta | Confronta `dotnet --list-sdks` con `global.json`. |
| Angular segnala pacchetti mancanti | Esegui `npm ci` in `src/Qwerty.Codi.Web`. |
| Angular non trova i file `.pem` | Completa la [preparazione dei certificati](src/Qwerty.Codi.Web/README.md#certificati-https-al-primo-avvio). |
| Il browser rifiuta il certificato | Verifica l'attendibilità con `dotnet dev-certs https --check --trust`. |
| Le richieste inoltrate al backend falliscono | Verifica il processo su 5200 e `proxy.conf.json`. |
| La porta risulta occupata | Ferma il servizio precedente con Ctrl+C nel suo terminale. |
| Il backend rifiuta lo schema | Leggi la divergenza nel registro del server; per le prove scegli una directory dati nuova. |
| `/` su 5200 risponde 404 | Usa le porte Angular oppure avvia il [pacchetto statico compilato](src/Qwerty.Codi.Web/README.md#bundle-per-lo-stesso-host). |

## Comandi di verifica (§20.5)

Restore dei pacchetti, build, test, lint e bundle si eseguono localmente, anche tramite
l'agente nel perimetro autorizzato, riportando l'output reale. Il progetto non usa GitHub
Actions: push e pull request non avviano controlli automatici. Installazioni di sistema,
dati operativi e distribuzione richiedono una richiesta specifica.

### Backend

Dalla radice `Codi/`, con il ripristino in modalità bloccata (§3.2):

```powershell
dotnet restore .\Qwerty.Codi.slnx --locked-mode
dotnet build .\Qwerty.Codi.slnx --configuration Release --no-restore
dotnet test .\Qwerty.Codi.slnx --configuration Release --no-build
```

`--locked-mode` fa fallire il ripristino se un `packages.lock.json` è disallineato, invece di
aggiornarlo in silenzio: il ripristino locale usa le versioni bloccate.

Con la copertura (§20.4):

Il collector modifica le copie degli assembly nelle cartelle dei test.
Il processo deve avere accesso in scrittura a tali file.
Un ambiente ristretto può eseguire tutti i test e produrre comunque un rapporto vuoto.
In questo caso controlla i log del collector e ripeti la raccolta con i permessi necessari.
Il verificatore rifiuta esplicitamente un rapporto vuoto, anche insieme a rapporti validi.

```powershell
$coverageRun = Join-Path .\TestResults ([Guid]::NewGuid().ToString('N'))
dotnet test .\Qwerty.Codi.slnx --configuration Release --no-build `
  --collect:"XPlat Code Coverage" --settings .\coverlet.runsettings `
  --results-directory $coverageRun
.\scripts\test-coverage-gate.ps1
.\scripts\assert-dotnet-coverage.ps1 -ResultsDirectory $coverageRun
```

Il collector è `coverlet.collector`, le impostazioni stanno in
[`coverlet.runsettings`](coverlet.runsettings) e il confronto in
[`scripts/assert-dotnet-coverage.ps1`](scripts/assert-dotnet-coverage.ps1), che legge i report
Cobertura e li confronta con [`scripts/coverage-baseline.json`](scripts/coverage-baseline.json).
La baseline è registrata da una misura reale. Il gate unisce le righe per assembly, file e numero,
e rifiuta gli assembly previsti ma non misurati. Ogni raccolta usa una cartella nuova: report di
esecuzioni diverse non vanno mescolati. Per un aggiornamento motivato nella matrice:

```powershell
.\scripts\assert-dotnet-coverage.ps1 -ResultsDirectory $coverageRun -UpdateBaseline
```

Le soglie si abbassano solo con una motivazione documentata nella
[matrice di copertura](docs/TEST-COVERAGE-MATRIX.md) (§20.4).

### Frontend

Da `src/Qwerty.Codi.Web`:

```powershell
npm ci
npm run lint
npm run format:check
npm run test:admin -- --watch=false
npm run test:frontend -- --watch=false
npm run build
npm run audit
npm run audit:runtime
```

`npm ci` installa dalle versioni bloccate del lockfile e richiede che
`package-lock.json` sia allineato a `package.json`; se non lo è, usare `npm install` come descritto
nel [README del frontend](src/Qwerty.Codi.Web/README.md). `npm run lint` invoca ESLint (flat
config). `npm run audit` include gli strumenti di sviluppo.
`npm run audit:runtime` controlla le sole dipendenze di produzione.

Gli override di Minimatch e Zod correggono vulnerabilità transitive degli strumenti.
Minimatch usa la versione 11.0.8; il generatore Spartan usa Zod 4.6.6.
Prima di rimuovere gli override, verifica audit, lint e generatori Spartan in modalità `--dry-run`.

L'admin carica il trasporto SignalR dal percorso `@codi/api/signalr-antiforgery` soltanto nelle funzionalità che lo usano.
Il provider globale Spartan usa `@spartan-ng/helm/utils/providers`, separato dagli helper grafici.
Questi percorsi preservano il caricamento differito senza aumentare il budget del bundle.

### Esito di queste verifiche

I comandi qui sopra sono la procedura, non un esito: gli esiti reali, con data e limiti, sono
registrati in [`STATO-SVILUPPO.md`](.github/STATO-SVILUPPO.md), insieme alle regressioni corrette
e alle prove ancora da eseguire sui PC del laboratorio.

## Regole di test (§20.3)

In breve, il dettaglio è nella specifica tecnica §20.3:

- ogni funzionalità nuova e ogni correzione di difetto porta con sé un test di regressione;
- si sceglie il livello più basso che dimostra il comportamento: unitario per la logica isolata,
  integrazione solo quando l'oggetto è la cooperazione fra infrastruttura e componenti;
- la classificazione è `Trait("Category", "Unit")` o `Trait("Category", "Integration")`; i test
  unitari non usano file system, SQLite, rete, attese sull'orologio reale né stato condiviso;
- nome `Membro_Scenario_ComportamentoAtteso`, con Arrange, Act e Assert visibilmente separati;
- si asserisce il risultato osservabile dall'esterno: un test che passa deve fallire se il
  comportamento documentato viene rimosso;
- il codice dipendente dal tempo usa `TimeProvider`, i test un `FakeTimeProvider` con avanzamento
  esplicito; i test di integrazione usano un database SQLite temporaneo dedicato e sono indipendenti
  dall'ordine di esecuzione;
- §19 definisce *cosa* verificare, §20.3 *come* scriverlo.

Quali comportamenti sono già coperti, da quale test e con quali buchi noti è in
[`docs/TEST-COVERAGE-MATRIX.md`](docs/TEST-COVERAGE-MATRIX.md).

## Documentazione e personalizzazioni

Le regole operative per chi modifica il codice sono in
[`.github/copilot-instructions.md`](.github/copilot-instructions.md); i profili, le procedure e le
skill per gli agenti sono in [`.github/README.md`](.github/README.md). Codice e identificatori in
inglese; documentazione, commenti e testi dell'interfaccia in italiano.

Le form admin adottano Angular Signal Forms con layout Spartan: [guida e inventario](docs/ADMIN-SIGNAL-FORMS.md).
