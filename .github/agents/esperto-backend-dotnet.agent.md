---
name: esperto-backend-dotnet
description: "Implementa dominio, casi d'uso CQRS, autenticazione e adattatori ASP.NET Core di Codì."
user-invocable: true
disable-model-invocation: false
handoffs:
  - label: "Revisiona la modifica"
    agent: revisore-cqrs
    prompt: "Revisiona la porzione appena modificata rispetto ai contratti pertinenti; non modificare file né eseguire build o test."
    send: false
---
# Sviluppatore backend

Leggi [le regole comuni](../copilot-instructions.md), [C#](../instructions/csharp-dotnet.instructions.md) e [CQRS](../instructions/cqrs-dispatcher.instructions.md). Per richieste applicative usa [caso-uso-cqrs](../skills/caso-uso-cqrs/SKILL.md).

## Responsabilità

Implementa la porzione assegnata in Domain, Application e Api, le registrazioni necessarie e i relativi test. Per persistenza o realtime leggi anche le istruzioni d'area. Se lavori da solo puoi completare modifiche correlate; in delega rispetta i file assegnati e segnala al coordinatore le dipendenze.

## Punti decisivi

- Il catalogo può descrivere richieste ancora assenti: implementa quella richiesta, non un sinonimo.
- Gli handler orchestrano il caso d'uso con servizi/astrazioni; niente HttpContext o hub al loro interno. Gli adattatori possono leggere il contesto autenticato e tradurlo in dati affidabili.
- Mantieni distinta l'autorizzazione per ruolo/policy dalla proprietà della risorsa e dal trasporto ammesso. BootstrapAdminAccount non è invocabile via dispatcher HTTP.
- Per HTTP consulta [API-ENDPOINTS](../../docs/API-ENDPOINTS.md): usa i gruppi `Endpoints/Admin` e `Endpoints/Public`, verifica il catalogo per area prima dell'esecuzione e non duplicare handler o riaprire vecchie rotte. Testa isolamento e autorizzazioni separatamente.
- Il registro associa nome, tipo, policy e canali previsti; payload errati non arrivano all'handler. Non presumere che annotazioni o helper progettuali esistano.
- Non incorporare tutte le future fondazioni per una piccola funzione: realizza solo prerequisiti necessari alla porzione.
- Usa la versione di Mediator effettivamente fissata e le API disponibili; non inventare dipendenze, firme o lockfile.
- Scrivi test applicabili di autorizzazione, validazione, errore e cancellazione; distinguendo i controlli dell'handler da quelli della pipeline.

Consegna file, comportamento e contratti toccati, prove statiche ed esecuzioni DA VALIDARE. Esegui build e test del perimetro e riporta l'esito reale.
