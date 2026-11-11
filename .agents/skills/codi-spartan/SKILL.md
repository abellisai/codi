---
name: codi-spartan
description: "Crea, modifica o aggiorna componenti Spartan nell'app Angular admin di Codì. Usa per form, pagine, dialog, tabelle, navigazione e temi admin; preserva la separazione dalla UI degli alunni."
---

# Componenti Spartan per Codì

Leggi [AGENTS.md](../../../AGENTS.md), [le istruzioni Angular](../../../.github/instructions/frontend-angular.instructions.md), la [skill ufficiale](../spartan/SKILL.md) e le sue regole pertinenti. Consulta [la guida del progetto](../../../docs/SPARTAN-UI.md) per fonti, percorsi e comandi.

1. Lavora da `src/Qwerty.Codi.Web`: workspace Angular CLI, progetti `web.admin` e `web.frontend`. Usa Node già attivo con nvm; non cambiare versioni o configurazioni globali.
2. Controlla `components.json`, `npm run ng -- g @spartan-ng/cli:info --json` e i file reali sotto `projects/admin/src/app/ui`. Con CLI 1.5.0, `info` può riportare `field` tramite un alias wildcard e non rilevare il CSS in un workspace multi-app: conferma l'esistenza del componente e leggi `projects/admin/src/styles.css`.
3. Cerca il componente nel [catalogo ufficiale](https://spartan.ng/components), poi leggi la pagina specifica per API, composizione e tastiera. Conferma i selettori anche nel codice della versione installata; non inventare input o import.
4. Riusa i componenti presenti. Per uno nuovo, verifica prima l'anteprima con `npm run ng -- g @spartan-ng/cli:ui --name=<nome> --dry-run`, poi genera con lo stesso comando senza `--dry-run`. La destinazione è esclusivamente `projects/admin/src/app/ui`, alias `@spartan-ng/helm`. Non sovrascrivere Helm personalizzato con un nuovo `init`.
5. Crea componenti applicativi standalone/OnPush nelle feature admin; usa Signals/RxJS e servizi tipizzati, mai `HttpClient` nel componente. Le route sono locali: `/admin/` viene dal base href. UI e commenti applicativi in italiano; conserva i commenti dei sorgenti ufficiali importati.
6. Componi Helm e Brain secondo la skill ufficiale: varianti e token semantici, etichette/errori associati, titolo nei dialog, focus e tastiera, icone registrate. Per tutte le form admin segui [Signal Forms e Spartan Field](../codi-signal-forms/SKILL.md), inclusi editor inline, selettori, filtri e upload. Importa soltanto i controlli necessari e conserva gli adattamenti di focus/descrizione dei sorgenti locali.
7. Mantieni Tailwind, Helm e servizi admin fuori da `projects/public` e `libraries`. Condividi solo contratti/trasporto `@codi/api` e motore `@exercise-engine/*`. Il design alunni resta da definire.
8. Esegui lint, formattazione, test e build pertinenti; dopo aggiornamenti esegui `healthcheck` in modalità rapporto prima di applicare migrazioni. Verifica stati di attesa/errore e accessibilità nel browser quando possibile. Non scrivere dati operativi per una prova UI.
9. Aggiorna documentazione e `.github/STATO-SVILUPPO.md` con esiti reali e limiti. Usa [verifica-consegna](../../../.github/skills/verifica-consegna/SKILL.md) per la consegna.
