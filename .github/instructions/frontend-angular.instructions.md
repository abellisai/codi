---
description: "App Angular Codì: componenti, servizi HTTP/realtime, stato e accessibilità."
applyTo: "**/src/Qwerty.Codi.Web/**"
---
# Angular

Workspace multi-app: `projects/admin` (`web.admin`, base `/admin/`, Spartan)
e `projects/public` (`web.frontend`, base `/`, UI da definire).
Trasporto e contratti comuni in `libraries/api` (`@codi/api`); motore Blockly in
`libraries/exercise-engine`. Non duplicare questi sorgenti nelle app e non importare servizi
admin, Helm o CSS dell'admin nelle librerie comuni o nel frontend. Le route del router admin
sono locali: il builder aggiunge `/admin/` agli URL esterni. `npm run test -- --watch=false`
esegue le due app e, una sola volta tramite il target admin, i test delle librerie.

`npm run start` da `projects/admin` avvia admin HTTPS 4200; da `projects/public` avvia
public HTTPS 4300. I due `package.json` locali contengono solo lo script `start`:
dipendenze, lockfile e `node_modules` restano nella root Web, dove si eseguono `npm ci`
e i gate comuni. Dalla root Web, `start`/`start:admin` selezionano admin e
`start:frontend` seleziona public. Nella root Codì i launcher `start-admin.bat` e
`start-public.bat` eseguono entrambi `npm run start` nella cartella corretta;
`start-angular.bat` apre entrambi in terminali separati. `start-server.bat` avvia
API HTTPS 5200 con il profilo senza browser; Node resta quello già attivo con nvm.
Gli script locali entrano nella root Web prima di chiamare `ng serve`, per risolvere
correttamente proxy e certificati condivisi; non duplicare `ssl/` dentro le app.

Fonti: §14, §17, §20.7. Leggi [le regole comuni](../copilot-instructions.md).

Per componenti admin usa [la skill Codì Spartan](../../.agents/skills/codi-spartan/SKILL.md), la skill ufficiale collegata e [la guida al catalogo](../../docs/SPARTAN-UI.md).
Per tutte le form admin carica [admin-signal-forms](admin-signal-forms.instructions.md) e la
[skill locale](../../.agents/skills/codi-signal-forms/SKILL.md): Signal Forms per modello,
schema e invio, Spartan Field per layout ed errori. La scelta comprende editor inline,
filtri, selettori e upload; non introduce la UI admin nelle librerie comuni o in public.

- Componenti standalone, route lazy, OnPush; Signals per stato locale/derivato, RxJS per flussi asincroni.
- Un unico punto applica lo stato autoritativo. Le route guard aiutano la navigazione, i permessi decisivi restano sul server.
- ApiService offre executeCommand ed executeQuery per il dispatcher, con nomi catalogati, DTO e errori tipizzati; niente wrapper per singola richiesta.
- Configura `API_AREA` in `app.config.ts`: admin usa `/api/v1/admin`, public `/api/v1/public`. La sessione admin usa `ADMIN_API_PREFIX`; contenuti e media sono eliminati dal prodotto. Leggi [la guida endpoint](../../docs/API-ENDPOINTS.md); le route delle pagine `/admin/*` non sono API. Il proxy Vite richiede `/api/**` per i sottopercorsi e va riavviato dopo modifiche.
- Gli endpoint dedicati di §10.3 (accesso, scoperta, file e salvataggi) usano servizi HTTP tipizzati appropriati. Non forzarli nel dispatcher e non usare HttpClient nei componenti.
- Revisioni e altri contatori a 64 bit restano stringhe decimali sul wire; nessuna conversione Number che perda precisione.
- Cookie HttpOnly per autenticazione; token antiforgery configurato separatamente. Nessuna credenziale in localStorage.
- Connessioni SignalR incapsulate nei servizi; durata coerente con sessione/area. Smaltisci sottoscrizioni, worker e workspace quando finisce il loro uso, senza una nuova connessione per ogni route.
- Gli esiti delle richieste/caricamenti superati non cambiano l'attività corrente. Riconnessione richiede nuova sincronizzazione prima di riabilitare il lavoro.
- Dashboard: 12 anteprime leggere, editor Blockly in sola lettura nel dettaglio selezionato; nessuna esecuzione dei programmi nel monitor.
- Invio, conferma e salvataggio sono distinti. Se la dashboard è offline, indica dati non aggiornati e disabilita comandi fino al nuovo snapshot.
- Italiano semplice, testo e icone insieme, focus e tastiera, etichette; area interattiva proposta 48×48 CSS px. I dettagli tecnici non compaiono nella vista alunno.
- Esporta contratti/trasporto comuni da libraries/api/index.ts; servizi admin da core/api/index.ts e componenti riusabili da shared/components/index.ts; alias invece di import profondi.
- Service worker opzionale nella prima build: se presente solo in produzione, niente cache API/negoziati hub e niente cambio motore automatico durante una sessione.
- Per player/realtime e Blockly carica anche le rispettive istruzioni; gli script npm vanno verificati sul package.json reale.

Scrivi test del comportamento pertinente. Esegui lint, test e build e riporta l'esito; `npm install` solo quando servono pacchetti nuovi.
