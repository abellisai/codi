---
description: "Specifiche, cataloghi, rimandi e guide di sviluppo Codì."
applyTo: "**/SPECIFICA_TECNICA_PIATTAFORMA_DIDATTICA.md,**/SPECIFICHE_PIATTAFORMA_DIDATTICA.md,**/Codi/docs/**/*.md,docs/**/*.md"
---
# Documentazione

Precedenza del prodotto: specifica funzionale, specifica tecnica, documenti derivati. Le istruzioni esplicite dell'utente governano il lavoro autorizzato.

- Requisito funzionale modificato: aggiorna entrambe le specifiche. Contratto tecnico o processo: aggiorna solo i documenti pertinenti. Implementazione di quanto già specificato: aggiorna stato/evidenze, senza inventare una revisione del requisito.
- Leggi la sezione e i suoi riferimenti prima di editarla. Mantieni numerazione ove possibile; se cambia, aggiorna indice e rimandi.
- § non qualificato indica la specifica tecnica. Usa “funzionale §” per l'altro documento.
- Distingui comportamento richiesto, PROPOSTA, codice presente e verifiche DA VALIDARE. Un test scritto non è un test superato.
- Non descrivere cartelle o script futuri come esistenti: usa DA CREARE. I link alle risorse attuali devono risolversi.
- I cataloghi §10.4–§10.5 sono la fonte dei nomi di richiesta; §11 definisce metodi/eventi. Non sostituire un nome di evento con quello del relativo comando.
- Forme canoniche: nome pubblico PascalCase, session_command.command_name, commandName, target.scope (Class/Participant), RequestDispatcher e OutboxDispatcher.
- Se un nome cambia, cerca gli utilizzatori e aggiorna i riferimenti pertinenti in §6, §7, §10, §11, §12 e personalizzazioni. Non duplicare il catalogo nelle istruzioni.
- Una contraddizione si registra con riferimenti ed effetto; non risolverla cambiando silenziosamente il prodotto.
- Modifica sostanziale della specifica tecnica: aggiorna intestazione e registro §24. Il registro di sviluppo raccoglie avanzamento e verifiche; non copiare lì intere specifiche.
- Italiano, frasi verificabili, esempi JSON con UUID e contatori a 64 bit come stringhe.

Per modifiche alle personalizzazioni leggi anche [sviluppo-agentico](sviluppo-agentico.instructions.md).
