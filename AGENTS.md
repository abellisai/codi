# Codì — ingresso per agenti

Queste istruzioni si applicano al progetto Codì. Prima di pianificare, modificare file o avviare comandi, leggi [le regole operative](.github/copilot-instructions.md).

- Il contratto funzionale è [la specifica funzionale](SPECIFICHE_PIATTAFORMA_DIDATTICA.md); il contratto tecnico è [la specifica tecnica](SPECIFICA_TECNICA_PIATTAFORMA_DIDATTICA.md).
- Per sviluppo e ripresa leggi [lo stato del lavoro](.github/STATO-SVILUPPO.md) e la skill [sviluppo-incrementale](.github/skills/sviluppo-incrementale/SKILL.md). Lo stato è un promemoria da confrontare con i file, non una prova di completamento.
- La [mappa di ripresa dal sorgente](docs/RIPRESA-SVILUPPO.md) descrive il modello corrente e le differenze dalle specifiche dopo la riorganizzazione; confrontala con i file prima di scegliere l'incremento.
- Carica le istruzioni per area elencate nelle regole operative; non presumere che i glob Copilot siano applicati da altri strumenti.
- Per API e servizi Angular leggi [l'organizzazione degli endpoint](docs/API-ENDPOINTS.md): gruppi `/api/v1/admin` e `/api/v1/public`, catalogo limitato per area e policy ancora obbligatorie. Non ripristinare le vecchie rotte condivise.
- Per componenti admin leggi la [skill locale Codì Spartan](.agents/skills/codi-spartan/SKILL.md) e la [skill ufficiale Spartan](.agents/skills/spartan/SKILL.md); entrambe sono nella cartella del progetto.
- Per tutte le form admin usa [Signal Forms con Spartan](.agents/skills/codi-signal-forms/SKILL.md) e carica [le istruzioni delle form](.github/instructions/admin-signal-forms.instructions.md), inclusi editor inline, filtri, selettori e upload.
- Build, restore, test, lint, bundle e raccolte di copertura li esegue l'agente, leggendone l'output dal terminale e riportando l'esito reale. Restano fuori dalla sua autorizzazione: installazioni di software di sistema, scrittura su dati operativi, pubblicazione e distribuzione.
- In Codex i file `.github/agents/*.agent.md` sono profili consultabili: il loro YAML non registra automaticamente agenti nativi. Le skill invocabili rimandano alle procedure condivise e possono essere aperte dal percorso. Non dichiararle installate o disponibili come comandi se non sono state rilevate dall'ambiente.
- Usa un profilo specialista quando serve. Delega soltanto quando l'utente richiede agenti/delega o il workflow attivo lo prevede e gli strumenti sono disponibili; altrimenti completa sequenzialmente con gli stessi confini.
