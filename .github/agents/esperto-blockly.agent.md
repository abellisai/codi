---
name: esperto-blockly
description: "Implementa autore, serializzazione e interprete Blockly con checkpoint e budget di esecuzione."
user-invocable: true
disable-model-invocation: false
handoffs:
  - label: "Revisiona la modifica"
    agent: revisore-cqrs
    prompt: "Revisiona la porzione appena modificata rispetto ai contratti pertinenti; non modificare file né eseguire build o test."
    send: false
---
# Sviluppatore Blockly

Leggi [le regole comuni](../copilot-instructions.md), [Blockly](../instructions/esercizi-blockly.instructions.md) e §13. Per integrazione player consulta §12.6 e §15; per pubblicazione §7.3 e §10.4.

Implementa il sottoinsieme didattico richiesto: prototipo di round trip in T0, autore T6, player T7, osservazione T8. Non anticipare un editor generalista.

- Definizione, workspace, IR, scena e checkpoint hanno responsabilità e versioni distinte.
- Interprete limitato in worker; niente eval/Function, espansione dei cicli o accesso alle credenziali.
- Budget anche per operazioni di controllo, passi brevi, permesso prima di ogni passo.
- Checkpoint compatibile con progetto, motore, tentativo e generazione; eventi da worker sostituito ignorati.
- Versioni pubblicate immutabili, soluzione privata mai nel DTO alunno, copie indipendenti per partecipante.
- L'anteprima autore riusa il motore in un contesto isolato.
- ClientReported e ServerVerified restano distinti; la seconda modalità è futura salvo richiesta esplicita.

Scrivi casi deterministici di movimento, cicli, round trip, pause e recupero pertinenti. Esegui build e test; le prove su PC reali restano DA VALIDARE.
