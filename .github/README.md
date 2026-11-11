<p align="center">
  <img src="../src/Qwerty.Codi.Web/public/brand/codi-logo.svg" alt="Codì" width="364" height="116">
</p>

# Sviluppare Codì con gli agenti

Questa cartella contiene ruoli e procedure per portare avanti gli incrementi T0–T9. Il punto d'ingresso operativo è [copilot-instructions.md](copilot-instructions.md); [STATO-SVILUPPO.md](STATO-SVILUPPO.md) conserva avanzamento, evidenze, ambiguità e prossimo passo.

Le specifiche restano la fonte del prodotto. Questa configurazione non implementa le funzioni applicative e non dimostra che un incremento sia pronto.

## Preparazione e avvio del progetto

Apri la cartella `Codi`, accanto a `Qwerty.Codi.slnx`.
Le istruzioni operative sono nei README del progetto.

| Guida | Contenuto |
|---|---|
| [Preparazione iniziale](../README.md#preparazione-al-primo-avvio) | Versioni richieste, ripristino pacchetti e certificati |
| [Avvio Windows](../README.md#avvio-rapido-windows) | Backend su 5200, admin su 4200 e alunni su 4300 |
| [Sviluppo del progetto](../README.md#sviluppo-del-progetto) | Struttura, ciclo di lavoro e dati isolati |
| [Sviluppo Angular](../src/Qwerty.Codi.Web/README.md#sviluppo-quotidiano) | App separate, test, componenti e librerie comuni |
| [Controlli locali](../README.md#comandi-di-verifica-205) | Build, test, formattazione e audit |

Le modifiche documentali richiedono controlli sui link e sui comandi descritti.
Le modifiche applicative richiedono anche i controlli pertinenti sul codice.

## Le due applicazioni Angular

Il workspace `src/Qwerty.Codi.Web` contiene `projects/admin` (`web.admin`, Spartan,
base `/admin/`) e `projects/public` (`web.frontend`, base `/`, UI alunni da definire).
Ogni cartella applicativa usa `npm run start`: admin HTTPS 4200, public HTTPS 4300.
I manifest locali definiscono solo lo script; dipendenze, lockfile, installazione e gate
restano nella root Web. Trasporto/contratti sono in `libraries/api`, motore Blockly in
`libraries/exercise-engine`; le librerie comuni e public non importano l'admin o Spartan.

Nella root Codì: [start-server.bat](../start-server.bat) avvia API HTTPS 5200 senza
browser; [start-admin.bat](../start-admin.bat) e [start-public.bat](../start-public.bat)
usano `npm run start` nella rispettiva cartella; [start-angular.bat](../start-angular.bat)
avvia le due app in terminali distinti. Nessuno script cambia la versione Node di nvm.
La [guida Web](../src/Qwerty.Codi.Web/README.md) descrive alias della root e bundle;
le [istruzioni Angular](instructions/frontend-angular.instructions.md) fissano i confini.

## Partire e riprendere

La [mappa di ripresa dal sorgente](../docs/RIPRESA-SVILUPPO.md) descrive il modello e le
route correnti. Prima di scegliere un incremento confronta
quella mappa con lo stato e le specifiche: T0–T8 hanno evidenze locali, il collaudo nel browser
riguarda T7 e il percorso dell'anteprima di T8, e il prossimo incremento è T9 (laboratorio su 12 PC).

Per backend e trasporto Angular leggi [API-ENDPOINTS](../docs/API-ENDPOINTS.md):
`/api/v1/admin` espone amministrazione/regia, `/api/v1/public` l'area alunni.
Il registro e gli handler sono comuni, il catalogo HTTP è limitato per area e le policy
restano obbligatorie. La guida distingue rotte disponibili e funzioni future, con regole
per aggiungere endpoint senza duplicare casi d'uso. Agenti backend/frontend e revisore,
istruzioni CQRS/Angular/test e skill correlate applicano lo stesso contratto.

Apri preferibilmente **Codì come cartella di progetto**, così la directory .github è alla radice del workspace.

Per iniziare in una chat:

> Usa il coordinatore di sviluppo e la skill sviluppo-incrementale. Leggi lo stato, completa la prossima porzione disponibile e aggiorna il passaggio di consegne. Esegui i controlli pertinenti e registra l'esito reale.

Per una chat successiva:

> Riprendi Codì da .github/STATO-SVILUPPO.md, confrontalo con i file e completa il prossimo passo disponibile. Ecco gli esiti delle verifiche: …

Per un obiettivo preciso:

> Implementa il caso d'uso CreateSchool previsto dalla specifica, con persistenza e test. Prima evidenzia il conflitto A-01 sull'unicità del nome e prosegui sulle parti indipendenti.

Il coordinatore delimita una porzione, realizza codice e test, integra le modifiche, le revisiona e aggiorna il registro. Non richiede approvazioni ripetute per dettagli ordinari. Le decisioni di prodotto incompatibili con le fonti sono esposte con alternative; il lavoro indipendente continua.

## Ruoli disponibili

| Profilo | Uso | Scrittura |
|---|---|---|
| [coordinatore-sviluppo](agents/coordinatore-sviluppo.agent.md) | Sviluppo completo della porzione, integrazione e stato | Sì |
| [analista-specifica](agents/analista-specifica.agent.md) | Requisiti, contratti, differenze dal codice | No |
| [esperto-backend-dotnet](agents/esperto-backend-dotnet.agent.md) | Dominio, CQRS, policy, API | Sì |
| [esperto-database](agents/esperto-database.agent.md) | SQLite, RepoDB, migrazioni | Sì |
| [esperto-frontend-angular](agents/esperto-frontend-angular.agent.md) | Viste, servizi e accessibilità | Sì |
| [esperto-realtime](agents/esperto-realtime.agent.md) | Hub, outbox, ACK, lease, recovery | Sì |
| [esperto-blockly](agents/esperto-blockly.agent.md) | Autore, worker, serializzazione | Sì |
| [esperto-test](agents/esperto-test.agent.md) | Regressioni ed esecuzione dei gate | Sì |
| [revisore-cqrs](agents/revisore-cqrs.agent.md) | Revisione funzionale e tecnica delle aree toccate | No |
| [redattore-specifica](agents/redattore-specifica.agent.md) | Specifiche e documentazione operativa | Solo documentazione |

Il coordinatore ha accesso alla delega verso i nove profili. I profili non fissano modelli; chi modifica il codice esegue i comandi di verifica e ne legge l'output dal terminale, mentre analista e revisore restano in sola lettura. Gli handoff consentono un passaggio esplicito; la delega interna serve solo se utile e supportata dall'ambiente. Un solo autore per file condiviso, con stato aggiornato dal coordinatore. Un intervento piccolo può essere completato senza sottagenti.

## Procedure riutilizzabili

| Skill | Scopo |
|---|---|
| [sviluppo-incrementale](skills/sviluppo-incrementale/SKILL.md) | Scegliere, implementare e riprendere una porzione |
| [caso-uso-cqrs](skills/caso-uso-cqrs/SKILL.md) | Richiesta, handler, policy, test e integrazione |
| [migrazione-sqlite](skills/migrazione-sqlite/SKILL.md) | Schema, migrazioni e repository |
| [pagina-angular](skills/pagina-angular/SKILL.md) | Pagina collegata al backend |
| [allinea-documentazione](skills/allinea-documentazione/SKILL.md) | Contratti, cataloghi e rimandi |
| [verifica-consegna](skills/verifica-consegna/SKILL.md) | Evidenze e comandi pertinenti |
| [Codì Spartan](../.agents/skills/codi-spartan/SKILL.md) e [Spartan ufficiale](../.agents/skills/spartan/SKILL.md) | Componenti admin, catalogo, CLI, temi e composizione; skill locali al progetto |
| [Codì Signal Forms](../.agents/skills/codi-signal-forms/SKILL.md) | Form admin, editor inline, selettori, filtri e upload con Signal Forms e layout Spartan |

Le [skill invocabili](skills/) sono scorciatoie con `disable-model-invocation: true`, chiamate per nome con un argomento: [sviluppa-incremento](skills/sviluppa-incremento/SKILL.md), [riprendi-sviluppo](skills/riprendi-sviluppo/SKILL.md), [nuovo-caso-uso](skills/nuovo-caso-uso/SKILL.md), [nuova-migrazione](skills/nuova-migrazione/SKILL.md), [nuova-pagina-angular](skills/nuova-pagina-angular/SKILL.md), [correggi-difetto](skills/correggi-difetto/SKILL.md), [revisiona-modifica](skills/revisiona-modifica/SKILL.md), [allinea-documentazione-2](skills/allinea-documentazione-2/SKILL.md) e [prepara-verifica](skills/prepara-verifica/SKILL.md). Rimandano alle procedure della tabella invece di duplicarle. Puoi sempre chiedere in linguaggio naturale di leggere il file corrispondente.

## Copilot, VS Code e Codex

**VS Code/Copilot:** i file .agent.md usano il formato documentato per agenti e handoff. Le istruzioni per area hanno glob applyTo e le skill risiedono in .github/skills. La discovery dipende dal tipo di sessione e dalla sua versione: controlla che i profili siano elencati nelle personalizzazioni dell'editor. Le scorciatoie sono le skill invocabili di `.github/skills`: Agent Host non carica i file `.prompt.md`, quindi ogni workflow vive nelle skill. Fonti: [agenti](https://code.visualstudio.com/docs/agent-customization/custom-agents), [istruzioni](https://code.visualstudio.com/docs/agent-customization/custom-instructions), [skill](https://code.visualstudio.com/docs/agent-customization/agent-skills), [prompt](https://code.visualstudio.com/docs/agent-customization/prompt-files).

**Codex:** [AGENTS.md del progetto](../AGENTS.md) collega queste regole; nella cartella superiore è presente un ingresso limitato ad Codì per chi apre il workspace CorsoInformaticaPrimaria. I profili .agent.md restano descrizioni di ruolo consultabili, non registrazioni di subagenti Codex. Le skill .github sono utilizzabili leggendole tramite AGENTS o richiesta esplicita; la discovery nativa documentata usa .agents/skills, non va data per scontata qui. Non sono state modificate configurazioni globali né installati plugin. Fonti: [AGENTS.md](https://developers.openai.com/codex/guides/agents-md), [skill locali](https://developers.openai.com/codex/skills).

Se apri la cartella superiore in Copilot, i glob compatibili con prefissi **/ non rendono automaticamente rilevabile una .github annidata: apri Codì direttamente o verifica la discovery del tuo ambiente. In Codex l'ingresso superiore indirizza esplicitamente al progetto.

## Esecuzione dei controlli e verifica

Gli agenti scrivono implementazioni e test, eseguono restore, build, test, lint e bundle leggendone l'output dal terminale, e registrano l'esito reale. All'utente restano installazioni di sistema, dati operativi, pubblicazione e distribuzione.

Le verifiche si eseguono localmente. GitHub Actions è stato rimosso su richiesta
dell'utente; non aggiungere workflow di CI senza una nuova richiesta esplicita.

Non chiedere un gate inesistente: verificare package.json, lockfile, collector e script. Una porzione con codice e test scritti resta IMPLEMENTATO — DA VALIDARE finché non esiste l'esito pertinente. Non confondere l'assenza di errori di compilazione con il funzionamento in classe.

## Controllo dell'attivazione nell'editor

Dopo aver aperto Codì, verifica nell'editor i dieci profili, le sei procedure `.github/skills` e le tre skill `.agents/skills`, poi in una chat nuova chiedi senza modificare file:

> Qual è il prossimo lavoro incompleto? Quali controlli devi eseguire e cosa resta fuori dalla tua autorizzazione? Indica dove le hai lette.

Prova anche questi casi in sola analisi:
- Pause catalogato ma non implementato: proporre l'implementazione canonica.
- Login o salvataggio player: rispettare gli endpoint dedicati.
- BootstrapAdminAccount: negare l'esposizione HTTP anche se ha policy AnonymousBootstrap.
- Nuova vista: decidere dettagli ordinari senza fermarsi a ogni scelta grafica.
- Contrasto A-01: esplicitare il conflitto senza imporre un vincolo nascosto.
- Frontend con dipendenze installate e gate verdi: `npm ci` è eseguibile e `npm run lint`, `format:check`, `test`, `build` e `audit:runtime` sono i suoi controlli.

Questi sono scenari per verificare discovery e comportamento, non prove già eseguite nell'editor.

## Manutenzione

Aggiorna [lo stato](STATO-SVILUPPO.md) a ogni consegna implementativa. Modifica le regole soltanto per esigenze dimostrate; mantieni link, nomi dei profili e canali autorizzati coerenti. Le modifiche di processo sostanziali vanno registrate nella specifica tecnica §20.1/§24; il funzionale cambia solo se cambia il prodotto.
