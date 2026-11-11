---
description: "Manutenzione di agenti, prompt, skill e stato di sviluppo di Codì."
applyTo: "**/.github/**,**/AGENTS.md"
---
# Personalizzazioni agentiche

Leggi [le regole comuni](../copilot-instructions.md) e [la guida](../README.md).

- Le specifiche definiscono il prodotto; i file agentici definiscono come lavorare. Non creare un catalogo alternativo di comandi, DTO o tabelle.
- Mantieni una sola fonte per ogni workflow: le skill invocabili rimandano alle procedure condivise, i profili specialistici alle istruzioni d'area, gli ingressi AGENTS alle regole comuni.
- Frontmatter agenti: nome stabile, descrizione discriminante, strumenti coerenti. Analista e revisore restano in sola lettura. Solo il coordinatore usa il tool agent con elenco esplicito dei collaboratori.
- Non fissare modelli, segreti, percorsi assoluti personali o dipendenze da estensioni non presenti. Gli handoff puntano a nomi di agenti esistenti.
- Le istruzioni usano `applyTo` come stringa di glob separati da virgole; non allargare regole Angular a ogni HTML del workspace.
- Le skill usano almeno `name` e `description`; il nome coincide con la cartella. Risorse collegate con percorsi relativi al file che contiene il link: da una skill in `.github/skills/<nome>/` le regole comuni sono `../../copilot-instructions.md` e una procedura sorella è `../<altra>/SKILL.md`.
- Le skill invocabili con `disable-model-invocation: true` sono scorciatoie: contengono solo la procedura da seguire e rimandano alla skill di procedura, senza duplicarla. Il workflow deve restare utilizzabile anche aprendo il relativo SKILL.md. Non dichiarare discovery nativa in Codex per file .github.
- Lo stato corrente appartiene a STATO-SVILUPPO.md, non a frasi permanenti come “questo comando non è mai stato eseguito” nelle skill.
- Un template deve rendere riconoscibili le parti da adattare. Non fornire test vuoti che passano, policy mancanti presentate come complete o classi concrete duplicate da copiare alla cieca.
- Per modifiche solo documentali controlla percorsi, link, frontmatter, riferimenti e coerenza; non avviare build o test applicativi, salvo che la modifica tocchi una pipeline o uno script di verifica, nel qual caso quell'esecuzione va fatta.
