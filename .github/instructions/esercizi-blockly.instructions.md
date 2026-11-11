---
description: "Definizioni e workspace Blockly, interprete worker, checkpoint e pubblicazione Codì."
applyTo: "**/src/Qwerty.Codi.Web/libraries/exercise-engine/**,**/src/Qwerty.Codi.Web/projects/*/src/app/features/exercise-authoring/**,**/src/Qwerty.Codi.Domain/Exercises/**,**/src/Qwerty.Codi.Application/**/Exercises/**"
---
# Blockly e motore

Fonti: §7.3, §13, §19.4; per permessi operativi §12.6 e [protocollo](protocollo-realtime.instructions.md).

- Definizione didattica, workspace Blockly e checkpoint sono formati diversi; non usarli in modo intercambiabile.
- Salva/carica workspace con serializzazione Blockly; sopprimi eventi durante load e ripristinali anche in errore.
- Server: valida dimensione, profondità, blocchi, campi, collegamenti e versioni. Rifiuta blocchi ignoti e formati futuri, senza downgrade silenzioso.
- Pipeline: workspace → validazione/normalizzazione → IR → interprete nel worker → scena. Niente eval o Function.
- Ogni istruzione conserva sourceBlockId. I cicli usano frame, non espansione; anche il controllo consuma budget.
- Worker senza rete/credenziali; passi brevi e yield. I valori 8 ms/100 operazioni e 300 ms didattici sono proposte da configurare e misurare.
- Permesso controllato prima di ogni passo; scadenza monotona conservativa, nessun rinnovo prodotto da un messaggio vecchio.
- Messaggi associati a tentativo, generazione, run e worker; ignora eventi da istanze sostituite.
- Checkpoint valido solo con progetto, motore e generazione coerenti; editing lo invalida. Pausa/attesa non consumano durata attiva.
- ACK di pausa dopo conferma worker. Timeout: termina il worker, blocca il player e segnala AppliedWithRecoveryRequired, senza promettere ripresa esatta.
- Versioni pubblicate immutabili, pubblicazione idempotente; soluzione privata mai nei DTO alunno.
- Ogni partecipante lavora su copia indipendente. L'anteprima autore riusa il motore in stato isolato.
- Blocchi obbligatori verificati dal server: deletable=false da solo non basta.
- Esiti ClientReported finché non esiste verifica server equivalente; non presentarli come ServerVerified.
- Test pertinenti: round trip, scene, cicli/budget, checkpoint/ripresa, isolamento, worker sostituito, soluzione privata.

Non anticipare l'intero motore T7 per il solo prototipo Blockly T0. Le prove da tastiera e sui PC reali restano DA VALIDARE finché non eseguite.
