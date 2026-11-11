# Spartan nell'admin Codì

Spartan è adottato soltanto da `web.admin`. Il frontend alunni è un'app indipendente;
il suo framework grafico sarà scelto successivamente. Entrambe le app condividono i
contratti e il trasporto HTTP, con endpoint e policy distinti per le rispettive funzioni.

## Skill locali

- [Procedura Codì](../.agents/skills/codi-spartan/SKILL.md): confini delle app, generazione, composizione e verifica.
- [Signal Forms admin](../.agents/skills/codi-signal-forms/SKILL.md): modello, validazione, invio e layout; [guida delle form](ADMIN-SIGNAL-FORMS.md).
- [Skill ufficiale Spartan](../.agents/skills/spartan/SKILL.md): Brain/Helm, CLI, temi, form, icone e composizione; inclusi tutti i suoi file di riferimento.

Le tre skill risiedono nel progetto, in `.agents/skills`, senza installazioni globali.
La skill ufficiale è stata acquisita il 1 ottobre 2026 da
[spartan-ng/spartan, skills/spartan](https://github.com/spartan-ng/spartan/tree/main/skills/spartan).
È materiale upstream consultabile anche direttamente; la discovery automatica dipende
dall'ambiente e dalla cartella di progetto aperta. Le procedure `.github/skills` restano
collegate tramite AGENTS.

Adattamento locale: rimosso dal frontmatter ufficiale il solo campo `user-invocable`,
non accettato dal validatore skill Codex; istruzioni e riferimenti upstream sono conservati.

## Documentazione ufficiale

Usa questo indice completo come ingresso e leggi la pagina del componente prima di implementarlo.
Le API online possono evolvere: controlla sempre anche il sorgente locale generato e il lockfile.

| Area | Fonti |
|---|---|
| Ingresso e catalogo | [Documentazione](https://spartan.ng/documentation), [tutti i componenti](https://spartan.ng/components) |
| Configurazione | [Installazione](https://spartan.ng/documentation/installation), [versioni supportate](https://spartan.ng/documentation/version-support), [CLI](https://spartan.ng/documentation/cli), [components.json](https://spartan.ng/documentation/components-json) |
| Stile | [Theming](https://spartan.ng/documentation/theming), [stili](https://spartan.ng/documentation/styles), [dark mode](https://spartan.ng/documentation/dark-mode), [tipografia](https://spartan.ng/documentation/typography), [RTL](https://spartan.ng/documentation/rtl), [Figma](https://spartan.ng/documentation/figma) |
| Manutenzione e agenti | [Aggiornamenti](https://spartan.ng/documentation/update-guide), [health checks](https://spartan.ng/documentation/health-checks), [skill](https://spartan.ng/documentation/skills), [MCP](https://spartan.ng/documentation/mcp) |
| Form | [Guida](https://spartan.ng/forms), [Reactive Forms](https://spartan.ng/forms/reactive-forms), [Signal Forms](https://spartan.ng/forms/signal-forms), [Field](https://spartan.ng/components/field) |
| Primitivi già integrati | [Button](https://spartan.ng/components/button), [Input](https://spartan.ng/components/input), [Label](https://spartan.ng/components/label), [Card](https://spartan.ng/components/card) |

Le regole ufficiali locali coprono styling, form, composizione, icone e Brain/Helm.
Per componenti successivi (dialog, tabelle, sidebar, select, ecc.) consulta la pagina specifica
dal catalogo: questo indice non equivale a una verifica preventiva di tutte le loro API.

## Configurazione reale

Dalla cartella `src/Qwerty.Codi.Web`:

```powershell
npm run ng -- g @spartan-ng/cli:info --json
npm run ng -- g @spartan-ng/cli:ui --name=dialog --dry-run
# Dopo aver controllato la destinazione nell'anteprima:
npm run ng -- g @spartan-ng/cli:ui --name=dialog
npm run ng -- g @spartan-ng/cli:healthcheck
```

Sono esempi di generazione: verifica i sorgenti prima di aggiungere un componente.
`components.json` usa stile `vega`, tema Slate e alias `@spartan-ng/helm`;
il percorso di generazione è `projects/admin/src/app/ui`. Brain è il pacchetto npm;
Helm è sorgente del progetto. Sono presenti badge, breadcrumb, button, card, dialog, empty, field, input, label, native-select,
pagination, select, separator, sheet, sidebar, skeleton, sonner, spinner, table, tabs,
textarea, tooltip e utils. Il guscio dell'area docente (`features/admin/admin-shell`)
compone la sidebar con le sezioni di configurazione; le icone usano `@ng-icons/lucide`, tenuto alla
stessa versione di `@ng-icons/core`. I componenti riusabili fra più viste stanno in
`projects/admin/src/app/shared/components` (alias `@shared/components`): oggi `EmptyState`, `DataTable` con celle personalizzate e paginazione in memoria,
`DetailHeader` con breadcrumb e `GlobalSpinner`. GlobalSpinner è montato nel guscio insieme al toaster; app.config.ts registra loading,
antiforgery e notification. I test della configurazione reale verificano il feedback HTTP.
Tailwind 4 e il preset Spartan sono importati esclusivamente in `projects/admin/src/styles.css`.

Il healthcheck del 1 ottobre 2026 in modalità rapporto è **tutto verde**: i cinque dialoghi CRUD
usano `*hlmDialogPortal` e gli import Brain non servono più. Gli adattamenti locali di Signal
Forms, focus e descrizione sono stati preservati nella sostituzione. Build, lint, formattazione e
test verdi; il collaudo grafico/tastiera nel browser è stato eseguito il 1 ottobre 2026 su database
temporaneo, senza difetti.

Con CLI 1.5.0, `info` non riconosce sempre il CSS del workspace multi-app e può indicare
`field` anche senza il relativo sorgente. Controlla i file, evitando rigenerazioni inutili.
Gli override npm sono limitati alle tre dipendenze transitive di Nx segnalate dall'audit;
il lockfile ne registra le versioni corrette.

## Form admin

Le form presenti usano Angular Signal Forms e Spartan Field: accesso, scuole/classi,
corsi/lezioni, metadati esercizi e selettore della sequenza. Creazione/modifica sono in
dialoghi. Native Select integra modello e focus; il controllo custom dei file è stato rimosso.
Contenuti, immagini e iscrizioni sono eliminati dal prodotto.
I dettagli e gli adattamenti locali sono in [ADMIN-SIGNAL-FORMS](ADMIN-SIGNAL-FORMS.md).

## Avvio e controlli

Usa la versione Node già attiva con nvm. `npm run start` da `projects/admin` avvia
l'admin su `https://localhost:4200/admin/`; lo stesso comando da `projects/public`
avvia gli alunni su `https://localhost:4300/`. Nella root Web, `start` e `start:admin`
avviano admin, mentre `start:frontend` avvia public. I due manifest applicativi contengono
solo lo script di avvio; dipendenze e lockfile restano nella root Web.
Entrambi inoltrano API e hub a `https://localhost:5200`.
Il profilo Visual Studio dell'API ha `launchBrowser: false`.
I certificati locali restano in `ssl/` e non sono versionati.

```powershell
npm run lint
npm run format:check
npm test -- --watch=false
npm run build
npm audit
```

`build` produce i due bundle e `dist/site` (alunni alla radice, admin sotto `/admin/`).
Non distribuisce i file nell'API. Per prove del comportamento usa test significativi;
per composizione e accessibilità verifica anche tastiera, focus, etichette e stati UI.
