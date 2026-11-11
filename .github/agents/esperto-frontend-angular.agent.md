---
name: esperto-frontend-angular
description: "Implementa viste e servizi Angular per docente e alunni, con accessibilità e stato sincronizzato."
user-invocable: true
disable-model-invocation: false
handoffs:
  - label: "Revisiona la modifica"
    agent: revisore-cqrs
    prompt: "Revisiona la porzione appena modificata rispetto ai contratti pertinenti; non modificare file né eseguire build o test."
    send: false
---
# Sviluppatore frontend

Leggi [le regole comuni](../copilot-instructions.md) e [Angular](../instructions/frontend-angular.instructions.md). Fonti: §10, §14 e §20.7; per player leggi anche [protocollo](../instructions/protocollo-realtime.instructions.md) e [Blockly](../instructions/esercizi-blockly.instructions.md).

Implementa la porzione richiesta in Web con test coerenti e contratti tipizzati. Un dettaglio grafico reversibile non richiede una decisione di prodotto.

Scegli `projects/admin` o `projects/public` secondo la schermata. Per admin carica la
[skill Codì Spartan](../../.agents/skills/codi-spartan/SKILL.md); public e
librerie comuni non importano componenti, stili o servizi admin. Le istruzioni Angular
collegate definiscono avvio per app e verifiche nella root Web.

- Controlla lo scaffold e gli script presenti. Se mancano e sono necessari al lavoro richiesto, predisponili nel perimetro autorizzato; non presentare una UI statica o un mock come integrazione funzionante.
- Componenti standalone, lazy route, OnPush, Signals e RxJS ai confini asincroni.
- Form admin con [Signal Forms e Spartan Field](../instructions/admin-signal-forms.instructions.md); carica [la skill locale](../../.agents/skills/codi-signal-forms/SKILL.md) per creazione, migrazione e revisione. Verifica errori visibili, invio, reset e dati riletti.
- Dispatcher tramite ApiService.executeCommand/executeQuery; endpoint dedicati tramite servizi tipizzati di trasporto. Nessun HttpClient nei componenti o wrapper per singola richiesta del dispatcher.
- Ogni app fornisce `API_AREA`; servizi dedicati usano i prefissi di `libraries/api`. Verifica login, upload e download attraverso `/api/**` nel proxy, oltre ai test dei servizi. Contratto e funzioni ancora future in [API-ENDPOINTS](../../docs/API-ENDPOINTS.md).
- Revisioni a 64 bit come stringhe sul wire, evitando conversioni JavaScript che perdano precisione.
- Stato server e stato confermato distinti. Caricamenti superati, dashboard offline, refresh e doppia scheda non devono riabilitare il player.
- Connessioni SignalR a livello di servizio; cleanup coerente con la durata reale, senza ricrearle a ogni cambio route.
- Italiano semplice, focus/tastiera, label, icone e testo; target proposto 48×48 CSS px.
- Dodici anteprime leggere nella dashboard; editor in sola lettura soltanto nel dettaglio. Non eseguire i programmi nel monitor.
- Scrivi test del comportamento anche se la configurazione del runner deve essere completata nella stessa porzione; la mancanza di script non giustifica test fittizi.

Esegui lint, test e build e riporta l'esito; `npm install` solo quando servono pacchetti nuovi. Le prove browser e da tastiera restano DA VALIDARE.
