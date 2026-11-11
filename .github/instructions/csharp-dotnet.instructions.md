---
description: "Codice C# Codì: confini architetturali, dipendenze, cancellazione, tempo ed errori."
applyTo: "**/src/Qwerty.Codi.*/**/*.cs,**/tests/Qwerty.Codi.*/**/*.cs"
---
# C# e .NET

Leggi [le regole comuni](../copilot-instructions.md). Fonti: §4, §20.2–§20.3, §20.9.

- Target net10.0, Nullable e ImplicitUsings abilitati. Namespace file-scoped, dipendenze dal costruttore, record immutabili per DTO, collezioni readonly, sealed dove non serve ereditarietà.
- Gli endpoint e gli hub adattano il trasporto: estraggono identità, token di cancellazione e dati necessari; delegano il caso d'uso. Non contengono SQL o regole di regia.
- Gli handler possono orchestrare servizi e repository applicativi; non dipendono da HttpContext, hub o stato di connessione. Non invocano altre richieste.
- Un contesto esplicito affidabile non si costruisce accettando un'identità arbitraria dal payload.
- Propaga CancellationToken; tempo tramite TimeProvider, senza DateTime.Now/UtcNow nei percorsi dipendenti dal tempo.
- Usa gli esiti espliciti previsti dalle astrazioni; eventuali eccezioni applicative tipizzate vengono tradotte in Problem Details. Non creare una seconda convenzione di errori né trasformare indiscriminatamente errori previsti in 500.
- Smaltisci connessioni, transazioni, risposte HTTP e risorse anche nei percorsi di errore/cancellazione prima del trasferimento di proprietà.
- Log strutturati con identificativi di correlazione, mai cookie, password, nomi degli alunni o workspace completi.
- Identificatori in inglese, commenti e messaggi UI in italiano; .editorconfig governa formattazione.
- Nessun esempio progettuale è una prova che un tipo o una firma sia già disponibile. Controlla le dipendenze effettive prima di implementare.

Scrivi test pertinenti ed eseguili; riporta l'esito reale, non quello atteso.
